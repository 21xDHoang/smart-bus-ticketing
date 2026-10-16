using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SmartBus.Api.Dtos.Refunds;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test cho <see cref="VnPayRefundClient"/> — client hoàn tiền VNPay (US 8, task *"API gọi cổng thanh
/// toán để hoàn tiền"* dòng 29 — Trần Trung Hiếu).
///
/// Không cần CSDL: mọi cuộc gọi cổng đi qua <see cref="FakeHandler"/>. Khác hai client kia ở chỗ
/// <c>vnp_RequestId</c> sinh MỚI mỗi lượt gọi (tài liệu cổng bắt không lặp trong ngày) nên chữ ký
/// KHÔNG có vector cố định — test tính lại HMAC-SHA512 từ thân request đã bắt được, theo ĐÚNG thứ tự
/// trường tài liệu cổng (nối '|', KHÁC chuỗi ký thu tiền). Phép kiểm này ghim thứ tự trường và nội
/// dung ký: code đổi thứ tự hay bỏ trường nào là test đỏ ngay.
///
/// Bộ khoá SANDBOX CÔNG KHAI của VNPay — in nguyên văn trong tài liệu cổng, đã nằm sẵn trong repo
/// (luật 2, không thêm bí mật mới).
/// </summary>
public class VnPayRefundClientTests
{
    private const string TmnCode = "CGXZLS0Z";
    private const string HashSecret = "XNBCJFAKAZQSGTARRLGCHVZWCIOIGSHN";

    /// <summary>Mã giao dịch VNPay của lượt THU (vnp_TransactionNo gốc).</summary>
    private const string MaGiaoDichThu = "14408096";

    /// <summary>Mốc "bây giờ" ghim sẵn — vnp_CreateDate tính từ mốc này: 2023-11-15 05:13:20 GMT+7.</summary>
    private static readonly DateTimeOffset BayGio = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);

    /// <summary>Mốc thu tiền gốc (Payment.PaidAt) — vnp_TransactionDate: 2026-10-14 13:00:00 GMT+7.</summary>
    private static readonly DateTime MocThuTien = new(2026, 10, 14, 6, 0, 0, DateTimeKind.Utc);

    private static VnPayRefundClient TaoClient(FakeHandler handler) => new(
        Options.Create(new VnPayRefundOptions
        {
            TmnCode = TmnCode,
            HashSecret = HashSecret,
            RefundEndpoint = "https://sandbox.vnpayment.vn/merchant_webapi/api/transaction",
        }),
        new HttpClient(handler),
        new FakeClock(BayGio));

    private static RefundGatewayCallRequest TaoRequest() => new()
    {
        RefundId = Guid.Parse("b3e4a7c1-8f3a-4c2d-9e1b-0a1b2c3d4e5f"),
        RefundCode = "RF-8f3a2c1d",
        MethodCode = "VNPay",
        Amount = 175_000m,
    };

    private static Payment TaoGiaoDich(string? gatewayTransactionId = MaGiaoDichThu) => new()
    {
        PaymentCode = "PM-8f3a2c1d",
        MethodCode = "VNPay",
        Amount = 175_000m,
        GatewayTransactionId = gatewayTransactionId,
        PaidAt = MocThuTien,
    };

    // ---------------------------------------------------------------------------------------
    // Chữ ký và hình dạng request
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Ky_dung_chuoi_quy_dinh_va_du_dung_bo_truong_cua_cong()
    {
        var handler = new FakeHandler(_ => JsonResponse(new
        {
            vnp_ResponseCode = "00",
            vnp_Message = "Confirm Success",
            vnp_TransactionNo = "999888",
            vnp_TransactionStatus = "05",
        }));
        var client = TaoClient(handler);

        await client.RefundAsync(TaoRequest(), TaoGiaoDich());

        var sent = JsonDocument.Parse(await handler.ReadBodyAsync()).RootElement;
        string Truong(string ten) => sent.GetProperty(ten).GetString()!;

        // vnp_RequestId sinh mới mỗi lượt (không lặp trong ngày theo tài liệu cổng) — chỉ ghim hình
        // dạng: đúng 32 ký tự hex.
        var requestId = Truong("vnp_RequestId");
        Assert.Matches("^[0-9a-f]{32}$", requestId);

        Assert.Equal("2.1.0", Truong("vnp_Version"));
        Assert.Equal("refund", Truong("vnp_Command"));
        Assert.Equal(TmnCode, Truong("vnp_TmnCode"));
        Assert.Equal("02", Truong("vnp_TransactionType"));      // 02 = hoàn TOÀN PHẦN (US 8 không hoàn một phần)
        Assert.Equal("PM-8f3a2c1d", Truong("vnp_TxnRef"));      // mã giao dịch GỐC
        Assert.Equal("17500000", Truong("vnp_Amount"));         // số tiền gốc × 100 (đơn vị nhỏ nhất)
        Assert.Equal(MaGiaoDichThu, Truong("vnp_TransactionNo"));
        Assert.Equal("20261014130000", Truong("vnp_TransactionDate"));   // PaidAt sang GMT+7
        Assert.Equal("SmartBus", Truong("vnp_CreateBy"));
        Assert.Equal("20231115051320", Truong("vnp_CreateDate"));        // mốc đồng hồ giả sang GMT+7
        Assert.Equal("127.0.0.1", Truong("vnp_IpAddr"));
        Assert.Equal("Hoan tien RF-8f3a2c1d", Truong("vnp_OrderInfo"));

        // Tính lại HMAC-SHA512 từ chính thân request, nối '|' THEO ĐÚNG THỨ TỰ tài liệu cổng — đổi
        // thứ tự hay bỏ trường nào trong code là khớp không nổi và test đỏ ngay.
        var signingData = string.Join("|",
            requestId, "2.1.0", "refund", TmnCode, "02", "PM-8f3a2c1d", "17500000",
            MaGiaoDichThu, "20261014130000", "SmartBus", "20231115051320", "127.0.0.1",
            "Hoan tien RF-8f3a2c1d");

        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(HashSecret));
        var expected = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(signingData)));

        Assert.Equal(expected, Truong("vnp_SecureHash"));
    }

    [Fact]
    public async Task Goi_dung_endpoint_hoan_tien_cua_cong()
    {
        var handler = new FakeHandler(_ => JsonResponse(new
        {
            vnp_ResponseCode = "00",
            vnp_Message = "Confirm Success",
            vnp_TransactionNo = "999888",
            vnp_TransactionStatus = "05",
        }));
        var client = TaoClient(handler);

        await client.RefundAsync(TaoRequest(), TaoGiaoDich());

        Assert.Equal(
            "https://sandbox.vnpayment.vn/merchant_webapi/api/transaction",
            handler.LastRequest?.RequestUri?.ToString());
    }

    // ---------------------------------------------------------------------------------------
    // Kết luận theo câu trả lời của cổng
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task ResponseCode_00_tra_Succeeded_voi_ma_giao_dich_hoan()
    {
        var handler = new FakeHandler(_ => JsonResponse(new
        {
            vnp_ResponseCode = "00",
            vnp_Message = "Confirm Success",
            vnp_TransactionNo = "999888",
            vnp_TransactionStatus = "05",
        }));
        var client = TaoClient(handler);

        var ketQua = await client.RefundAsync(TaoRequest(), TaoGiaoDich());

        Assert.Equal(RefundLogResult.Succeeded, ketQua.Result);
        // vnp_TransactionNo của RESPONSE là mã giao dịch hoàn phía VNPay.
        Assert.Equal("999888", ketQua.GatewayTransactionId);
    }

    [Fact]
    public async Task ResponseCode_00_nhung_TransactionStatus_09_tra_Failed()
    {
        // TransactionStatus 09 = cổng TỪ CHỐI hoàn dù ResponseCode 00 — câu trả lời rõ ràng, không
        // được coi là thành công.
        var handler = new FakeHandler(_ => JsonResponse(new
        {
            vnp_ResponseCode = "00",
            vnp_Message = "Giao dịch hoàn tiền bị từ chối",
            vnp_TransactionNo = "999888",
            vnp_TransactionStatus = "09",
        }));
        var client = TaoClient(handler);

        var ketQua = await client.RefundAsync(TaoRequest(), TaoGiaoDich());

        Assert.Equal(RefundLogResult.Failed, ketQua.Result);
        Assert.Contains("TransactionStatus 09", ketQua.Message);
    }

    [Fact]
    public async Task ResponseCode_94_tra_Unresolved()
    {
        // Code 94 = lệnh hoàn cho giao dịch này ĐÃ TỒN TẠI trên cổng (lượt trước timeout nhưng vẫn tới
        // nơi) — kết quả cuối chưa biết: Unresolved cho bước đối soát tra, không đoán, không xếp lịch.
        var handler = new FakeHandler(_ => JsonResponse(new
        {
            vnp_ResponseCode = "94",
            vnp_Message = "Giao dịch đã gửi yêu cầu hoàn tiền trước đó",
        }));
        var client = TaoClient(handler);

        var ketQua = await client.RefundAsync(TaoRequest(), TaoGiaoDich());

        Assert.Equal(RefundLogResult.Unresolved, ketQua.Result);
        Assert.Contains("94", ketQua.Message);
    }

    [Fact]
    public async Task ResponseCode_khac_tra_Failed_voi_loi_cong()
    {
        var handler = new FakeHandler(_ => JsonResponse(new
        {
            vnp_ResponseCode = "91",
            vnp_Message = "Không tìm thấy giao dịch yêu cầu hoàn trả",
        }));
        var client = TaoClient(handler);

        var ketQua = await client.RefundAsync(TaoRequest(), TaoGiaoDich());

        Assert.Equal(RefundLogResult.Failed, ketQua.Result);
        Assert.Contains("91", ketQua.Message);
        Assert.Contains("Không tìm thấy", ketQua.Message);
    }

    [Fact]
    public async Task Body_rac_tra_Unresolved()
    {
        // HTTP 200 nhưng body không phải JSON (hoặc thiếu vnp_ResponseCode): không có câu trả lời
        // đọc được — Unresolved, để bước đối soát tra sao kê.
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>không phải JSON</html>", Encoding.UTF8, "text/html"),
        });
        var client = TaoClient(handler);

        var ketQua = await client.RefundAsync(TaoRequest(), TaoGiaoDich());

        Assert.Equal(RefundLogResult.Unresolved, ketQua.Result);
    }

    // ---------------------------------------------------------------------------------------
    // Thiếu dữ liệu gốc và lỗi vận chuyển
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Giao_dich_goc_thieu_TransactionNo_tra_Failed_khong_goi_HTTP()
    {
        var handler = new FakeHandler(_ => JsonResponse(new
        {
            vnp_ResponseCode = "00",
            vnp_Message = "Confirm Success",
        }));
        var client = TaoClient(handler);

        var ketQua = await client.RefundAsync(TaoRequest(), TaoGiaoDich(gatewayTransactionId: null));

        Assert.Equal(RefundLogResult.Failed, ketQua.Result);
        Assert.Contains("GatewayTransactionId", ketQua.Message);
        Assert.Equal(0, handler.SoLanGoi);
    }

    [Fact]
    public async Task Giao_dich_goc_thieu_PaidAt_tra_Failed_khong_goi_HTTP()
    {
        // vnp_TransactionDate bắt buộc — thiếu mốc thu tiền là không dựng được lệnh hoàn.
        var handler = new FakeHandler(_ => JsonResponse(new
        {
            vnp_ResponseCode = "00",
            vnp_Message = "Confirm Success",
        }));
        var client = TaoClient(handler);

        var giaoDich = TaoGiaoDich();
        giaoDich.PaidAt = null;
        var ketQua = await client.RefundAsync(TaoRequest(), giaoDich);

        Assert.Equal(RefundLogResult.Failed, ketQua.Result);
        Assert.Contains("PaidAt", ketQua.Message);
        Assert.Equal(0, handler.SoLanGoi);
    }

    [Fact]
    public async Task HTTP_chet_nem_len_nguoi_goi()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("mạng chết"));
        var client = TaoClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.RefundAsync(TaoRequest(), TaoGiaoDich()));
    }

    private static HttpResponseMessage JsonResponse(object body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
    };

    /// <summary>Đồng hồ giả — ghim mốc "bây giờ" để vnp_CreateDate định được chính xác.</summary>
    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int SoLanGoi { get; private set; }

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SoLanGoi++;
            LastRequest = request;
            return Task.FromResult(responder(request));
        }

        public async Task<string> ReadBodyAsync()
        {
            Assert.NotNull(LastRequest);
            return await LastRequest.Content!.ReadAsStringAsync();
        }
    }
}
