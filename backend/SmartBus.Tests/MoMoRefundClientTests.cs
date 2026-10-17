using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SmartBus.Api.Dtos.Refunds;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test cho <see cref="MoMoRefundClient"/> — client hoàn tiền MoMo (US 8, task *"API gọi cổng thanh
/// toán để hoàn tiền"* dòng 29 — Trần Trung Hiếu).
///
/// Không cần CSDL: mọi cuộc gọi cổng đi qua <see cref="FakeHandler"/>. Ca chữ ký dùng vector tính
/// SẴN bằng Python (độc lập với code C#) để ghim đúng chuỗi chuẩn MoMo của API hoàn tiền — bộ trường
/// KHÁC chiều thu tiền (có description/transId, không có ipnUrl/redirectUrl/requestType). Vector cũng
/// là chốt chống "sửa cho đẹp rồi hỏng chữ ký": đổi thứ tự trường là test đỏ ngay. Xác nhận cuối
/// cùng với cổng thật phải làm trên sandbox khi nhóm có khoá đối tác.
/// </summary>
public class MoMoRefundClientTests
{
    // Bộ dữ liệu đối tác giả cho test — cùng bộ giá trị đã dùng ở MoMoGatewayServiceTests (khoá giả,
    // KHÔNG phải khoá đối tác thật — luật 2).
    private const string PartnerCode = "MOMO0T1D20220107";
    private const string AccessKey = "F8BBA842ECF85";
    private const string SecretKey = "K951B6PE1waDMi640xX08PD3vg6EkVlz";

    /// <summary>Mã giao dịch MoMo của lượt THU — Payment.GatewayTransactionId của giao dịch gốc.</summary>
    private const string MaGiaoDichThu = "2757953842";

    /// <summary>
    /// Chữ ký của lệnh hoàn với bộ dữ liệu bên dưới, tính bằng Python (hmac-sha256): chuỗi
    /// "accessKey=F8BBA842ECF85&amp;amount=175000&amp;description=Hoan tien RF-8f3a2c1d&amp;orderId=RF-8f3a2c1d
    /// &amp;partnerCode=MOMO0T1D20220107&amp;requestId=b3e4a7c1-8f3a-4c2d-9e1b-0a1b2c3d4e5f
    /// &amp;transId=2757953842".
    /// </summary>
    private const string RefundSignatureVector = "7faa5eb705926625397f48cbf73150b8877eb99451ce8fff336d30cebfdb7fe4";

    private static MoMoRefundClient TaoClient(FakeHandler handler) => new(
        Options.Create(new MoMoOptions
        {
            PartnerCode = PartnerCode,
            AccessKey = AccessKey,
            SecretKey = SecretKey,
            Endpoint = "https://test-payment.momo.vn",
        }),
        new HttpClient(handler));

    private static RefundGatewayCallRequest TaoRequest(decimal amount = 175_000m) => new()
    {
        RefundId = Guid.Parse("b3e4a7c1-8f3a-4c2d-9e1b-0a1b2c3d4e5f"),
        RefundCode = "RF-8f3a2c1d",
        MethodCode = "MoMo",
        Amount = amount,
    };

    private static Payment TaoGiaoDich(string? gatewayTransactionId = MaGiaoDichThu) => new()
    {
        PaymentCode = "PM-8f3a2c1d",
        MethodCode = "MoMo",
        Amount = 175_000m,
        GatewayTransactionId = gatewayTransactionId,
        PaidAt = new DateTime(2026, 10, 14, 6, 0, 0, DateTimeKind.Utc),
    };

    // ---------------------------------------------------------------------------------------
    // Chữ ký và hình dạng request
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Ky_dung_chuoi_chuan_MoMo_va_khong_lo_accessKey_ra_than_body()
    {
        var handler = new FakeHandler(_ => JsonResponse(new { resultCode = 0, message = "Success." }));
        var client = TaoClient(handler);

        await client.RefundAsync(TaoRequest(), TaoGiaoDich());

        var sent = JsonDocument.Parse(await handler.ReadBodyAsync()).RootElement;

        // Ghim bằng vector tính ngoài code C#: sai thứ tự trường / sai khoá / sai hoa thường là đỏ ngay.
        Assert.Equal(RefundSignatureVector, sent.GetProperty("signature").GetString());

        // orderId của lệnh hoàn PHẢI KHÁC orderId của giao dịch mua (PaymentCode) — tài liệu cổng.
        Assert.Equal("RF-8f3a2c1d", sent.GetProperty("orderId").GetString());

        // requestId = RefundId: khoá idempotency — gửi lại sau timeout không tạo lệnh hoàn thứ hai.
        Assert.Equal("b3e4a7c1-8f3a-4c2d-9e1b-0a1b2c3d4e5f", sent.GetProperty("requestId").GetString());

        Assert.Equal(PartnerCode, sent.GetProperty("partnerCode").GetString());
        Assert.Equal(175000, sent.GetProperty("amount").GetInt64());
        Assert.Equal(2757953842, sent.GetProperty("transId").GetInt64());
        Assert.Equal("vi", sent.GetProperty("lang").GetString());
        Assert.Equal("Hoan tien RF-8f3a2c1d", sent.GetProperty("description").GetString());

        // accessKey CHỈ nằm trong chuỗi chữ ký, không nằm trong body (cùng chuẩn chiều thu tiền).
        Assert.False(sent.TryGetProperty("accessKey", out _));
        Assert.False(sent.TryGetProperty("secretKey", out _));
    }

    [Fact]
    public async Task Goi_dung_endpoint_refund_cua_cong()
    {
        var handler = new FakeHandler(_ => JsonResponse(new { resultCode = 0, message = "Success." }));
        var client = TaoClient(handler);

        await client.RefundAsync(TaoRequest(), TaoGiaoDich());

        Assert.Equal("https://test-payment.momo.vn/v2/gateway/api/refund", handler.LastRequest?.RequestUri?.ToString());
    }

    // ---------------------------------------------------------------------------------------
    // Kết luận theo câu trả lời của cổng
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task ResultCode_0_tra_Succeeded_voi_ma_giao_dich_hoan()
    {
        var handler = new FakeHandler(_ => JsonResponse(new
        {
            resultCode = 0,
            message = "Success.",
            transId = 999888777,
        }));
        var client = TaoClient(handler);

        var ketQua = await client.RefundAsync(TaoRequest(), TaoGiaoDich());

        Assert.Equal(RefundLogResult.Succeeded, ketQua.Result);
        // transId của RESPONSE là mã giao dịch hoàn phía MoMo — thứ đối chiếu sao kê.
        Assert.Equal("999888777", ketQua.GatewayTransactionId);
    }

    [Fact]
    public async Task ResultCode_khac_0_tra_Failed_voi_loi_cong()
    {
        var handler = new FakeHandler(_ => JsonResponse(new
        {
            resultCode = 1001,
            message = "Giao dịch không tồn tại",
        }));
        var client = TaoClient(handler);

        var ketQua = await client.RefundAsync(TaoRequest(), TaoGiaoDich());

        Assert.Equal(RefundLogResult.Failed, ketQua.Result);
        Assert.Contains("1001", ketQua.Message);
        Assert.Contains("Giao dịch không tồn tại", ketQua.Message);
    }

    // ---------------------------------------------------------------------------------------
    // Thiếu dữ liệu gốc và lỗi vận chuyển
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Giao_dich_goc_thieu_GatewayTransactionId_tra_Failed_khong_goi_HTTP()
    {
        var handler = new FakeHandler(_ => JsonResponse(new { resultCode = 0, message = "Success." }));
        var client = TaoClient(handler);

        var ketQua = await client.RefundAsync(TaoRequest(), TaoGiaoDich(gatewayTransactionId: null));

        // Thiếu mã giao dịch thu thì lệnh hoàn không trỏ được vào đâu — câu trả lời chắc chắn (thử
        // lại cũng không đổi) nên Failed, và không có cuộc gọi cổng nào.
        Assert.Equal(RefundLogResult.Failed, ketQua.Result);
        Assert.Contains("GatewayTransactionId", ketQua.Message);
        Assert.Equal(0, handler.SoLanGoi);
    }

    [Fact]
    public async Task HTTP_chet_nem_len_nguoi_goi()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("mạng chết"));
        var client = TaoClient(handler);

        // Lỗi vận chuyển NÉM LÊN — RefundGatewayCaller dịch thành Unresolved, không ai được đoán.
        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.RefundAsync(TaoRequest(), TaoGiaoDich()));
    }

    [Fact]
    public async Task Timeout_nem_TaskCanceled_len_nguoi_goi()
    {
        // Giả lập timeout bằng cách ném thẳng TaskCanceledException — không chờ 30 giây thật trong
        // test (CancelAfter 30s là hằng số của cổng, không thể rút ngắn).
        var handler = new FakeHandler(_ => throw new TaskCanceledException("hết thời gian chờ"));
        var client = TaoClient(handler);

        await Assert.ThrowsAsync<TaskCanceledException>(
            () => client.RefundAsync(TaoRequest(), TaoGiaoDich()));
    }

    private static HttpResponseMessage JsonResponse(object body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json"),
    };

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
