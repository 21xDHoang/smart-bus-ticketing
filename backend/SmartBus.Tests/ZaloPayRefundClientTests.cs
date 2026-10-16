using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SmartBus.Api.Dtos.Refunds;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test cho <see cref="ZaloPayRefundClient"/> — client hoàn tiền ZaloPay (US 8, task *"API gọi cổng
/// thanh toán để hoàn tiền"* dòng 29 — Trần Trung Hiếu).
///
/// Không cần CSDL: mọi cuộc gọi cổng đi qua <see cref="FakeHandler"/>. Ca MAC dùng vector tính SẴN
/// bằng Python (độc lập với code C#) để ghim đúng chuỗi ký <c>app_id|zp_trans_id|amount|description|
/// timestamp</c> — đổi thứ tự trường là test đỏ ngay.
///
/// Bộ khoá test CÔNG BỐ của ZaloPay (app 2553 — in trong tài liệu cổng, đã nằm sẵn trong repo):
/// Key1 ký chiều TA GỬI ĐI, Key2 xác thực chiều cổng gửi về — client hoàn tiền chỉ dùng Key1.
/// </summary>
public class ZaloPayRefundClientTests
{
    private const string AppId = "2553";
    private const string Key1 = "PcY4iZIKFCIdgZvA6ueMcMHHUbRLYjPL";
    private const string Key2 = "kLtgPl8HHhfvMuDHPwKfgfsY4Ydm9eIz";

    /// <summary>Mã giao dịch ZaloPay của lượt THU (zp_trans_id gốc).</summary>
    private const string MaGiaoDichThu = "2609270000012345";

    /// <summary>Mốc "bây giờ" ghim sẵn — timestamp của lệnh hoàn: 1700000000000 (2023-11-15 05:13:20 GMT+7).</summary>
    private static readonly DateTimeOffset BayGio = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);

    /// <summary>
    /// MAC của lệnh hoàn với bộ dữ liệu bên dưới, tính bằng Python (hmac-sha256, Key1): chuỗi
    /// "2553|2609270000012345|175000|Hoan tien RF-8f3a2c1d|1700000000000".
    /// </summary>
    private const string RefundMacVector = "b4bd2b74240bf4bf22247983a2ec1f56de2c8246f61fd3d8af421c8b93065663";

    private static ZaloPayRefundClient TaoClient(FakeHandler handler) => new(
        Options.Create(new ZaloPayOptions
        {
            AppId = AppId,
            Key1 = Key1,
            Key2 = Key2,
            Endpoint = "https://sb-openapi.zalopay.vn",
        }),
        new HttpClient(handler),
        new FakeClock(BayGio));

    private static RefundGatewayCallRequest TaoRequest() => new()
    {
        RefundId = Guid.Parse("b3e4a7c1-8f3a-4c2d-9e1b-0a1b2c3d4e5f"),
        RefundCode = "RF-8f3a2c1d",
        MethodCode = "ZaloPay",
        Amount = 175_000m,
    };

    private static Payment TaoGiaoDich(string? gatewayTransactionId = MaGiaoDichThu) => new()
    {
        PaymentCode = "PM-8f3a2c1d",
        MethodCode = "ZaloPay",
        Amount = 175_000m,
        GatewayTransactionId = gatewayTransactionId,
        PaidAt = new DateTime(2026, 10, 14, 6, 0, 0, DateTimeKind.Utc),
    };

    // ---------------------------------------------------------------------------------------
    // MAC và hình dạng request
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Ky_dung_chuoi_quy_dinh_va_m_refund_id_on_dinh_theo_khoan_hoan()
    {
        var handler = new FakeHandler(_ => JsonResponse(new
        {
            return_code = 1,
            return_message = "Giao dịch thành công",
            sub_return_code = 1,
            sub_return_message = "Success",
            refund_id = 1905861234567890,
        }));
        var client = TaoClient(handler);

        await client.RefundAsync(TaoRequest(), TaoGiaoDich());

        var sent = JsonDocument.Parse(await handler.ReadBodyAsync()).RootElement;

        // Ghim bằng vector tính ngoài code C#: sai thứ tự trường / sai khoá / sai hoa thường là đỏ ngay.
        Assert.Equal(RefundMacVector, sent.GetProperty("mac").GetString());

        // app_id là SỐ (int32 theo tài liệu), không phải chuỗi.
        Assert.Equal(2553, sent.GetProperty("app_id").GetInt32());
        Assert.Equal(2609270000012345, sent.GetProperty("zp_trans_id").GetInt64());
        Assert.Equal(175000, sent.GetProperty("amount").GetInt64());
        Assert.Equal(1700000000000, sent.GetProperty("timestamp").GetInt64());
        Assert.Equal("Hoan tien RF-8f3a2c1d", sent.GetProperty("description").GetString());

        // m_refund_id ỔN ĐỊNH theo RefundId (đúng định dạng {yymmdd}_{appId}_{32 ký tự}, 44/45 ký tự):
        // mỗi lượt thử lại mà sinh mã mới là một LỆNH HOÀN MỚI — hoàn hai lần. Khoá chống trùng của
        // ZaloPay đứng trên chính mã này.
        var refundId = sent.GetProperty("m_refund_id").GetString();
        Assert.Equal("231115_2553_b3e4a7c18f3a4c2d9e1b0a1b2c3d4e5f", refundId);
        Assert.Equal(44, refundId!.Length);
    }

    [Fact]
    public async Task Goi_dung_endpoint_refund_cua_cong()
    {
        var handler = new FakeHandler(_ => JsonResponse(new
        {
            return_code = 1,
            return_message = "Giao dịch thành công",
            refund_id = 1905861234567890,
        }));
        var client = TaoClient(handler);

        await client.RefundAsync(TaoRequest(), TaoGiaoDich());

        Assert.Equal("https://sb-openapi.zalopay.vn/v2/refund", handler.LastRequest?.RequestUri?.ToString());
    }

    // ---------------------------------------------------------------------------------------
    // Kết luận theo câu trả lời của cổng
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task ReturnCode_1_tra_Succeeded_voi_refund_id()
    {
        var handler = new FakeHandler(_ => JsonResponse(new
        {
            return_code = 1,
            return_message = "Giao dịch thành công",
            sub_return_code = 1,
            sub_return_message = "Success",
            refund_id = 1905861234567890,
        }));
        var client = TaoClient(handler);

        var ketQua = await client.RefundAsync(TaoRequest(), TaoGiaoDich());

        Assert.Equal(RefundLogResult.Succeeded, ketQua.Result);
        // refund_id là mã giao dịch hoàn phía ZaloPay — thứ đối chiếu sao kê.
        Assert.Equal("1905861234567890", ketQua.GatewayTransactionId);
    }

    [Fact]
    public async Task ReturnCode_2_tra_Failed_voi_loi_cong()
    {
        var handler = new FakeHandler(_ => JsonResponse(new
        {
            return_code = 2,
            return_message = "Giao dịch hoàn tiền thất bại",
            sub_return_code = -14,
            sub_return_message = "REFUND_AMOUNT_INVALID",
        }));
        var client = TaoClient(handler);

        var ketQua = await client.RefundAsync(TaoRequest(), TaoGiaoDich());

        Assert.Equal(RefundLogResult.Failed, ketQua.Result);
        Assert.Contains("REFUND_AMOUNT_INVALID", ketQua.Message);
    }

    [Fact]
    public async Task ReturnCode_2_voi_sub_23_tra_Unresolved()
    {
        // DUPLICATE_REFUND: lệnh hoàn với m_refund_id này ĐÃ TỒN TẠI trên cổng — lượt trước đã tới
        // nơi, kết quả cuối chưa biết. Unresolved cho bước đối soát tra, KHÔNG phải Failed để xếp
        // lịch thử lại (thử lại là đúng đường hoàn hai lần).
        var handler = new FakeHandler(_ => JsonResponse(new
        {
            return_code = 2,
            return_message = "Refund request already exists",
            sub_return_code = -23,
            sub_return_message = "DUPLICATE_REFUND",
        }));
        var client = TaoClient(handler);

        var ketQua = await client.RefundAsync(TaoRequest(), TaoGiaoDich());

        Assert.Equal(RefundLogResult.Unresolved, ketQua.Result);
        Assert.Contains("đã tồn tại", ketQua.Message);
    }

    [Fact]
    public async Task ReturnCode_3_tra_Unresolved()
    {
        // "Đang xử lý" — trạng thái thứ ba mà MoMo/VNPay không có: cổng ĐÃ nhận lệnh, kết quả cuối chỉ
        // biết qua query_refund. Không đoán, không xếp lịch gọi lại.
        var handler = new FakeHandler(_ => JsonResponse(new
        {
            return_code = 3,
            return_message = "Refund đang xử lý",
            sub_return_code = 0,
            sub_return_message = "",
        }));
        var client = TaoClient(handler);

        var ketQua = await client.RefundAsync(TaoRequest(), TaoGiaoDich());

        Assert.Equal(RefundLogResult.Unresolved, ketQua.Result);
        Assert.Contains("đang xử lý", ketQua.Message);
    }

    [Fact]
    public async Task ReturnCode_la_tra_Unresolved()
    {
        // Mã lạ (kể cả 0 — ví dụ mẫu của tài liệu cổng) là câu trả lời không hiểu được: không đoán.
        var handler = new FakeHandler(_ => JsonResponse(new
        {
            return_code = 0,
            return_message = "string",
        }));
        var client = TaoClient(handler);

        var ketQua = await client.RefundAsync(TaoRequest(), TaoGiaoDich());

        Assert.Equal(RefundLogResult.Unresolved, ketQua.Result);
    }

    // ---------------------------------------------------------------------------------------
    // Thiếu dữ liệu gốc và lỗi vận chuyển
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Giao_dich_goc_thieu_zp_trans_id_tra_Failed_khong_goi_HTTP()
    {
        var handler = new FakeHandler(_ => JsonResponse(new
        {
            return_code = 1,
            return_message = "Giao dịch thành công",
        }));
        var client = TaoClient(handler);

        var ketQua = await client.RefundAsync(TaoRequest(), TaoGiaoDich(gatewayTransactionId: null));

        Assert.Equal(RefundLogResult.Failed, ketQua.Result);
        Assert.Contains("GatewayTransactionId", ketQua.Message);
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

    /// <summary>Đồng hồ giả — ghim mốc "bây giờ" để timestamp và tiền tố ngày định được chính xác.</summary>
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
