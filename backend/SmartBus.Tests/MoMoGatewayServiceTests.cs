using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SmartBus.Api.Dtos.Payments;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test cho <see cref="MoMoGatewayService"/> — client cổng thanh toán MoMo (US 6, task
/// *"Tích hợp SDK MoMo: tạo giao dịch, nhận callback"* — Trần Trung Hiếu).
///
/// Không cần CSDL: mọi cuộc gọi cổng đi qua <see cref="FakeHandler"/> — bắt nguyên request gửi
/// đi, trả response giả định. Hai ca chữ ký dùng vector tính SẴN bằng Python (độc lập với code
/// C#) để ghim đúng chuỗi chuẩn MoMo: thứ tự trường xếp theo bảng chữ cái, hex chữ thường.
/// Vector cũng là chốt chống "sửa cho đẹp rồi hỏng chữ ký" — đổi thứ tự trường là test đỏ ngay.
/// Xác nhận cuối cùng với cổng thật phải làm trên sandbox khi nhóm có khoá đối tác.
/// </summary>
public class MoMoGatewayServiceTests
{
    // Bộ dữ liệu đối tác giả cho test — cùng bộ giá trị dùng để tính vector bằng Python.
    private const string PartnerCode = "MOMO0T1D20220107";
    private const string AccessKey = "F8BBA842ECF85";
    private const string SecretKey = "K951B6PE1waDMi640xX08PD3vg6EkVlz";
    private const string OrderId = "63e61ad2318d66f22ecad8f7";
    private const string RequestId = "84f36d78-a97e-4bd2-afb4-5f89cbaac15e";

    /// <summary>
    /// Chữ ký của request tạo giao dịch với bộ dữ liệu trên, tính bằng Python (hmac-sha256):
    /// chuỗi "accessKey=…&amount=175000&extraData=&ipnUrl=…&orderId=…&orderInfo=pay with MoMo
    /// &partnerCode=…&redirectUrl=…&requestId=…&requestType=captureWallet".
    /// </summary>
    private const string CreateSignatureVector = "09084889d3f8da23272a8822815a622c2c2233344134967e9c0eca39d4569b5c";

    /// <summary>
    /// Chữ ký IPN hợp lệ với bộ dữ liệu trên + message=Success, orderType=momo_wallet,
    /// payType=webApp, responseTime=1700000000000, resultCode=0, transId=2757953842 — cũng Python.
    /// </summary>
    private const string CallbackSignatureVector = "7ad94c925b148569e2195b1b9cc0d802d071f4d222a10a50abcfdd88b2574e93";

    private static MoMoGatewayService ServiceWith(FakeHandler handler) => new(
        Options.Create(new MoMoOptions
        {
            PartnerCode = PartnerCode,
            AccessKey = AccessKey,
            SecretKey = SecretKey,
            Endpoint = "https://test-payment.momo.vn",
        }),
        new HttpClient(handler));

    private static MoMoCreatePaymentRequest CreateRequest() => new()
    {
        OrderId = OrderId,
        Amount = 175000,
        OrderInfo = "pay with MoMo",
        RedirectUrl = "http://localhost:3000/resultMomo",
        IpnUrl = "http://localhost:5000/api/checkout/ipn/momo",
        RequestId = RequestId,
    };

    private static MoMoCallback ValidCallback() => new()
    {
        PartnerCode = PartnerCode,
        OrderId = OrderId,
        RequestId = RequestId,
        Amount = 175000,
        OrderInfo = "pay with MoMo",
        OrderType = "momo_wallet",
        TransId = 2757953842,
        ResultCode = 0,
        Message = "Success",
        PayType = "webApp",
        ResponseTime = 1700000000000,
        Signature = CallbackSignatureVector,
    };

    // ---------------------------------------------------------------------------------------
    // Tạo giao dịch
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Tao_giao_dich_ky_dung_chuoi_chuan_MoMo()
    {
        var handler = new FakeHandler(_ => JsonResponse(new { resultCode = 0, message = "Success" }));
        var service = ServiceWith(handler);

        await service.CreatePaymentAsync(CreateRequest());

        var body = await ReadBodyAsync(handler);
        var sent = JsonDocument.Parse(body).RootElement;

        // Ghim bằng vector tính ngoài code C#: sai thứ tự trường / sai khoá / sai hoa thường là đỏ ngay.
        Assert.Equal(CreateSignatureVector, sent.GetProperty("signature").GetString());

        // Thân request đúng chuẩn MoMo: accessKey CHỈ nằm trong chuỗi chữ ký, không nằm trong body.
        Assert.Equal(PartnerCode, sent.GetProperty("partnerCode").GetString());
        Assert.Equal(RequestId, sent.GetProperty("requestId").GetString());
        Assert.Equal(175000, sent.GetProperty("amount").GetInt64());
        Assert.Equal(OrderId, sent.GetProperty("orderId").GetString());
        Assert.Equal("captureWallet", sent.GetProperty("requestType").GetString());
        Assert.False(sent.TryGetProperty("accessKey", out _));
        Assert.False(sent.TryGetProperty("secretKey", out _));
    }

    [Fact]
    public async Task Tao_giao_dich_thanh_cong_tra_pay_url()
    {
        var handler = new FakeHandler(_ => JsonResponse(new
        {
            resultCode = 0,
            message = "Success",
            payUrl = "https://test-payment.momo.vn/v2/gateway/pay?t=abc",
            deeplink = "momo://app",
            qrCodeUrl = "https://test-payment.momo.vn/qr/abc",
        }));
        var service = ServiceWith(handler);

        var result = await service.CreatePaymentAsync(CreateRequest());

        Assert.True(result.Success);
        Assert.Equal("https://test-payment.momo.vn/v2/gateway/pay?t=abc", result.PayUrl);
        Assert.Equal("momo://app", result.Deeplink);
        Assert.Equal("https://test-payment.momo.vn/qr/abc", result.QrCodeUrl);
        Assert.Equal(0, result.ResultCode);
    }

    [Fact]
    public async Task Tao_giao_dich_bi_cong_tu_choi_tra_Message_cua_cong()
    {
        var handler = new FakeHandler(_ => JsonResponse(new
        {
            resultCode = 1006,
            message = "Giao dịch bị từ chối do vượt hạn mức",
        }));
        var service = ServiceWith(handler);

        var result = await service.CreatePaymentAsync(CreateRequest());

        Assert.False(result.Success);
        Assert.Equal(1006, result.ResultCode);
        Assert.Equal("Giao dịch bị từ chối do vượt hạn mức", result.Message);
        Assert.Empty(result.PayUrl);
    }

    [Fact]
    public async Task Tao_giao_dich_khong_truyen_requestId_thi_tu_sinh_guid()
    {
        var handler = new FakeHandler(_ => JsonResponse(new { resultCode = 0, message = "Success" }));
        var service = ServiceWith(handler);

        var request = CreateRequest();
        request.RequestId = null;

        await service.CreatePaymentAsync(request);

        var sent = JsonDocument.Parse(await ReadBodyAsync(handler)).RootElement;
        Assert.True(Guid.TryParse(sent.GetProperty("requestId").GetString(), out _));
    }

    // ---------------------------------------------------------------------------------------
    // Kiểm chữ ký IPN
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Callback_chu_ky_dung_tra_true()
    {
        var service = ServiceWith(new FakeHandler(_ => JsonResponse(new { })));

        Assert.True(service.IsValidCallback(ValidCallback()));
    }

    [Fact]
    public void Callback_sua_amount_thi_chu_ky_sai()
    {
        var service = ServiceWith(new FakeHandler(_ => JsonResponse(new { })));

        // Kẻ tấn công sửa số tiền (hoặc bất kỳ trường nào trong chuỗi ký) thì chữ ký không khớp nữa.
        var tampered = ValidCallback();
        tampered.Amount = 175001;

        Assert.False(service.IsValidCallback(tampered));
    }

    [Fact]
    public void Callback_ky_bang_khoa_khac_tra_false()
    {
        // Cấu hình khoá khác với khoá đã ký vector — không được coi là hợp lệ.
        var otherKeyService = new MoMoGatewayService(
            Options.Create(new MoMoOptions
            {
                PartnerCode = PartnerCode,
                AccessKey = AccessKey,
                SecretKey = "KHOACUAKEKHAC-KHONG-PHAI-KHOA-DA-KY",
                Endpoint = "https://test-payment.momo.vn",
            }),
            new HttpClient(new FakeHandler(_ => JsonResponse(new { }))));

        Assert.False(otherKeyService.IsValidCallback(ValidCallback()));
    }

    [Fact]
    public void Callback_chu_ky_khong_phai_hex_tra_false()
    {
        var service = ServiceWith(new FakeHandler(_ => JsonResponse(new { })));

        var malformed = ValidCallback();
        malformed.Signature = "day-khong-phai-hex";

        Assert.False(service.IsValidCallback(malformed));
    }

    // ---------------------------------------------------------------------------------------
    // Hỏi trạng thái giao dịch — nguồn sự thật của đối soát
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Query_tra_trang_thai_va_trans_id_cua_cong()
    {
        var handler = new FakeHandler(_ => JsonResponse(new
        {
            resultCode = 0,
            message = "Success",
            transId = 2757953842,
            amount = 175000,
        }));
        var service = ServiceWith(handler);

        var result = await service.QueryTransactionAsync(OrderId);

        Assert.True(result.Success);
        Assert.Equal(2757953842, result.TransId);
        Assert.Equal(175000, result.Amount);

        // Chuỗi ký của query là bộ ngắn nhất: accessKey&orderId&partnerCode&requestId (chữ cái).
        // requestId do service tự sinh nên đọc ngược từ body rồi tính lại bằng cùng thuật toán —
        // ghim đúng BỘ TRƯỜNG và thứ tự, không ghim giá trị.
        var sent = JsonDocument.Parse(await ReadBodyAsync(handler)).RootElement;
        var requestId = sent.GetProperty("requestId").GetString();

        var expected = HmacHex(
            $"accessKey={AccessKey}&orderId={OrderId}&partnerCode={PartnerCode}&requestId={requestId}");

        Assert.Equal(expected, sent.GetProperty("signature").GetString());
    }

    [Fact]
    public async Task Query_giao_dich_that_bai_tra_ResultCode_va_Message()
    {
        var handler = new FakeHandler(_ => JsonResponse(new { resultCode = 49, message = "Không tìm thấy giao dịch" }));
        var service = ServiceWith(handler);

        var result = await service.QueryTransactionAsync(OrderId);

        Assert.False(result.Success);
        Assert.Equal(49, result.ResultCode);
        Assert.Equal("Không tìm thấy giao dịch", result.Message);
        Assert.Equal(0, result.TransId);
    }

    // ---------------------------------------------------------------------------------------
    // Cấu hình
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Thieu_PartnerCode_thi_bao_dung_ten_khoa_thieu(string partnerCode)
    {
        var options = new MoMoOptions
        {
            PartnerCode = partnerCode,
            AccessKey = AccessKey,
            SecretKey = SecretKey,
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new MoMoGatewayService(Options.Create(options), new HttpClient()));

        Assert.Contains("MoMo:PartnerCode", ex.Message);
        Assert.Contains("user-secrets", ex.Message);
    }

    [Fact]
    public void Thieu_SecretKey_thi_bao_dung_ten_khoa_thieu()
    {
        var options = new MoMoOptions
        {
            PartnerCode = PartnerCode,
            AccessKey = AccessKey,
            SecretKey = string.Empty,
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new MoMoGatewayService(Options.Create(options), new HttpClient()));

        Assert.Contains("MoMo:SecretKey", ex.Message);
    }

    // ---------------------------------------------------------------------------------------
    // Helper
    // ---------------------------------------------------------------------------------------

    /// <summary>HMAC-SHA256 hex chữ thường — bản tính lại trong test để ghim bộ trường chữ ký.</summary>
    private static string HmacHex(string raw)
    {
        using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(SecretKey));

        return Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(raw)));
    }

    private static HttpResponseMessage JsonResponse(object body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
    };

    private static async Task<string> ReadBodyAsync(FakeHandler handler)
    {
        Assert.NotNull(handler.LastRequest);
        Assert.NotNull(handler.LastRequest.Content);

        return await handler.LastRequest.Content.ReadAsStringAsync();
    }

    /// <summary>Handler giả — bắt request gửi đi và trả response định trước.</summary>
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;

            return Task.FromResult(responder(request));
        }
    }
}
