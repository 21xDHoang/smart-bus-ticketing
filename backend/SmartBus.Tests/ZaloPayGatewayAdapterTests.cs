using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SmartBus.Api.Dtos.Payments;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test cho <see cref="ZaloPayGatewayAdapter"/> — adapter thống nhất cổng thanh toán (US 6, task
/// *"Tích hợp ZaloPay và thẻ ngân hàng"* — Nguyễn Duy Kiên).
///
/// Dùng client ZaloPay THẬT trên <see cref="FakeHandler"/> (bắt nguyên request gửi đi, không chạm
/// mạng) để phép dịch của adapter được kiểm trên hình dạng request thật. Ba chỗ đáng ghim nhất:
///
/// • adapter GHÉP tiền tố <c>yymmdd</c> lúc gửi và BỎ tiền tố lúc nhận — endpoint phải luôn thấy
///   đúng paymentCode nội bộ như với MoMo/VNPay;
/// • adapter ghim kênh ví qua <c>embed_data</c>, và chữ ký phải phủ ĐÚNG chuỗi embed_data đã chèn;
/// • vỏ callback ba trường có <c>type</c> là SỐ và <c>data</c> là CHUỖI JSON — khai sai kiểu là mọi
///   callback thật đều bị từ chối.
/// </summary>
public class ZaloPayGatewayAdapterTests
{
    // Bộ khoá đối tác GIẢ của test — cùng bộ với ZaloPayGatewayServiceTests (xem chú thích ở đó).
    private const string AppId = "2553";
    private const string Key1 = "test-key1-khong-phai-khoa-that";
    private const string Key2 = "test-key2-khong-phai-khoa-that";

    private const string PaymentCode = "PM-8f3a2c1d";
    private const long Amount = 175_000;

    /// <summary>Chuỗi <c>data</c> cổng gửi — đúng nguyên văn đưa vào script Node tính chữ ký.</summary>
    private const string CallbackData =
        "{\"app_id\":2553,\"app_trans_id\":\"261010_PM-8f3a2c1d\",\"app_user\":\"smartbus\","
        + "\"amount\":175000,\"zp_trans_id\":2609270000012345,\"server_time\":1791621120000,"
        + "\"channel\":38}";

    private const string CallbackMac =
        "8a6f812c2e8c56212a8402f4ea7ab9626d3be3932908fe69f744113c6377f80c";

    /// <summary>Cùng chuỗi <c>data</c> nhưng ký bằng Key1 (khoá chiều TA GỬI ĐI) — phải bị từ chối.</summary>
    private const string CallbackMacSignedWithKey1 =
        "f18066df8723e88a7d934e64f4e0e5f1718a5047a43f53b96299b73aa8a6a966";

    private static ZaloPayGatewayAdapter AdapterWith(FakeHandler handler) => new(new ZaloPayGatewayService(
        Options.Create(new ZaloPayOptions
        {
            AppId = AppId,
            Key1 = Key1,
            Key2 = Key2,
            Endpoint = "https://sb-openapi.zalopay.vn",
        }),
        new HttpClient(handler)));

    private static PaymentInitiationRequest InitiationRequest() => new()
    {
        PaymentCode = PaymentCode,
        Amount = Amount,
        OrderInfo = "Thanh toan ve xe SmartBus",
        ReturnUrl = "http://localhost:5173/payment-waiting",
        IpnUrl = "https://api.smartbus.test/api/payments/zalopay/callback",
        IpAddress = "203.113.131.1",
    };

    /// <summary>
    /// Dựng vỏ callback đúng như cổng gửi: <c>type</c> là SỐ, <c>data</c> là chuỗi JSON đã escape.
    /// Serialize bằng JsonSerializer để phần escape chuỗi là thật, không phải tự nối tay.
    /// </summary>
    private static string Envelope(string data, string mac, int type = 1)
        => JsonSerializer.Serialize(new { data, mac, type });

    // ---------------------------------------------------------------------------------------
    // Khởi tạo
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Initiate_ghep_tien_to_yymmdd_va_bo_lai_khi_nhan()
    {
        var handler = new FakeHandler(_ => JsonResponse(new
        {
            return_code = 1,
            return_message = "Success",
            order_url = "https://sb-openapi.zalopay.vn/v2/pay/abc",
            qr_code = "data:image/png;base64,AAA",
        }));

        var result = await AdapterWith(handler).InitiateAsync(InitiationRequest());

        var sent = JsonDocument.Parse(await ReadBodyAsync(handler)).RootElement;
        var appTransId = sent.GetProperty("app_trans_id").GetString();

        // Tiền tố phải là NGÀY HÔM NAY theo giờ Việt Nam — mốc lấy từ chính app_time gửi đi để phép
        // so không phụ thuộc việc test chạy lúc nào.
        var appTime = DateTimeOffset.FromUnixTimeMilliseconds(sent.GetProperty("app_time").GetInt64());
        var expectedPrefix = appTime.ToOffset(TimeSpan.FromHours(7)).ToString("yyMMdd");
        Assert.Equal($"{expectedPrefix}_{PaymentCode}", appTransId);

        Assert.True(result.Success);
        Assert.Equal("https://sb-openapi.zalopay.vn/v2/pay/abc", result.RedirectUrl);
        Assert.Equal("data:image/png;base64,AAA", result.QrCodeUrl);
        Assert.Equal("Success", result.Message);

        // Cổng bắt buộc app_user (MoMo không có trường này).
        Assert.Equal("smartbus", sent.GetProperty("app_user").GetString());
        Assert.Equal("https://api.smartbus.test/api/payments/zalopay/callback", sent.GetProperty("callback_url").GetString());
        Assert.Equal(Amount, sent.GetProperty("amount").GetInt64());
    }

    [Fact]
    public async Task Initiate_ghim_kenh_vi_va_chu_ky_phu_dung_embed_data_da_chen()
    {
        var handler = new FakeHandler(_ => JsonResponse(new { return_code = 1, order_url = "https://x" }));

        await AdapterWith(handler).InitiateAsync(InitiationRequest());

        var sent = JsonDocument.Parse(await ReadBodyAsync(handler)).RootElement;

        var embedData = sent.GetProperty("embed_data").GetString();
        Assert.Equal("{\"preferred_payment_method\":[\"zalopay_wallet\"]}", embedData);

        // Tính lại chữ ký theo ĐÚNG chuỗi ký của ZaloPay trên các giá trị ĐÃ GỬI ĐI: đây là phép
        // kiểm bắt được lỗi ký trên embed_data cũ rồi gửi embed_data mới (chữ ký sẽ lệch).
        var expectedMacInput = string.Join(
            "|",
            AppId,
            sent.GetProperty("app_trans_id").GetString(),
            "smartbus",
            Amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            sent.GetProperty("app_time").GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture),
            embedData,
            "[]");

        Assert.Equal(HmacHex(expectedMacInput, Key1), sent.GetProperty("mac").GetString());
    }

    [Fact]
    public async Task Initiate_cong_tu_choi_thi_Uu_tien_cau_chi_tiet_cua_cong()
    {
        var handler = new FakeHandler(_ => JsonResponse(new
        {
            return_code = 2,
            return_message = "Giao dich that bai",
            sub_return_code = -68,
            sub_return_message = "Duplicate app_trans_id",
        }));

        var result = await AdapterWith(handler).InitiateAsync(InitiationRequest());

        Assert.False(result.Success);
        // return_message là câu chung; câu nói rõ vì sao nằm ở sub_return_message.
        Assert.Equal("Duplicate app_trans_id", result.Message);
        Assert.Equal(string.Empty, result.RedirectUrl);
    }

    [Fact]
    public async Task Initiate_bo_trong_IpnUrl_thi_khong_gui_callback_url()
    {
        var handler = new FakeHandler(_ => JsonResponse(new { return_code = 1 }));

        var request = InitiationRequest();
        request.IpnUrl = string.Empty;

        await AdapterWith(handler).InitiateAsync(request);

        var sent = JsonDocument.Parse(await ReadBodyAsync(handler)).RootElement;

        Assert.False(sent.TryGetProperty("callback_url", out _));
    }

    // ---------------------------------------------------------------------------------------
    // Callback
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Verify_callback_that_thi_hop_le_va_bo_tien_to_ra_paymentCode()
    {
        var result = AdapterWith(new FakeHandler(_ => JsonResponse(new { })))
            .VerifyCallback(new PaymentCallbackInput { Body = Envelope(CallbackData, CallbackMac) });

        Assert.True(result.IsValid);
        Assert.True(result.Succeeded);
        // Endpoint phải thấy đúng paymentCode nội bộ — KHÔNG phải "261010_PM-8f3a2c1d".
        Assert.Equal(PaymentCode, result.PaymentCode);
        Assert.Equal(175_000m, result.Amount);
        Assert.Equal("2609270000012345", result.GatewayTransactionId);
        // Cố ý để trống: callback ZaloPay không mang mã kết quả nào để mà chép vào.
        Assert.Equal(string.Empty, result.ProviderResponseCode);
    }

    [Fact]
    public void Verify_type_la_SO_van_doc_duoc_vo_callback()
    {
        // Ghim lỗi đã từng mắc: khai Type là string thì JsonSerializer ném khi gặp "type":1 và MỌI
        // callback thật đều bị từ chối — hỏng cả đường tiền mà test chỉ nhìn chữ ký sẽ không thấy.
        var result = AdapterWith(new FakeHandler(_ => JsonResponse(new { })))
            .VerifyCallback(new PaymentCallbackInput { Body = Envelope(CallbackData, CallbackMac, type: 1) });

        Assert.True(result.IsValid);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Verify_ky_bang_Key1_thi_khong_hop_le()
    {
        var result = AdapterWith(new FakeHandler(_ => JsonResponse(new { })))
            .VerifyCallback(new PaymentCallbackInput
            {
                Body = Envelope(CallbackData, CallbackMacSignedWithKey1),
            });

        Assert.False(result.IsValid);
        Assert.False(result.Succeeded);
        Assert.Equal(string.Empty, result.PaymentCode);
    }

    [Fact]
    public void Verify_sua_so_tien_thi_chu_ky_sai()
    {
        var tampered = CallbackData.Replace("\"amount\":175000", "\"amount\":1");

        var result = AdapterWith(new FakeHandler(_ => JsonResponse(new { })))
            .VerifyCallback(new PaymentCallbackInput { Body = Envelope(tampered, CallbackMac) });

        Assert.False(result.IsValid);
        Assert.False(result.Succeeded);
    }

    /// <summary>
    /// Chữ ký ĐÚNG nhưng ruột không phải JSON — cổng thật không bao giờ gửi thế, nhưng nếu có thì
    /// tuyệt đối không được coi là đã thu tiền khi ta chưa đọc được gì để đối chiếu.
    /// </summary>
    [Fact]
    public void Verify_chu_ky_dung_nhung_ruot_hong_thi_khong_bao_thanh_cong()
    {
        var result = AdapterWith(new FakeHandler(_ => JsonResponse(new { })))
            .VerifyCallback(new PaymentCallbackInput
            {
                Body = Envelope("khong-phai-json", HmacHex("khong-phai-json", Key2)),
            });

        Assert.True(result.IsValid);
        Assert.False(result.Succeeded);
        Assert.Equal(string.Empty, result.PaymentCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("khong-phai-json")]
    [InlineData("{\"data\":\"x\"}")] // thiếu hẳn mac — chữ ký rỗng phải bị từ chối, không được ném
    public void Verify_than_rong_hoac_rac_tra_false_khong_nem(string? body)
    {
        var result = AdapterWith(new FakeHandler(_ => JsonResponse(new { })))
            .VerifyCallback(new PaymentCallbackInput { Body = body });

        Assert.False(result.IsValid);
        Assert.False(result.Succeeded);
        Assert.Equal(string.Empty, result.PaymentCode);
    }

    // ---------------------------------------------------------------------------------------
    // Helper
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// HMAC-SHA256 hex chữ thường. Dùng cho ca "chữ ký đúng nhưng ruột hỏng" — ở đó phép thử không
    /// nhắm vào chuẩn chữ ký (đã có vector tính ngoài C# ghim ở ZaloPayGatewayServiceTests) mà nhắm
    /// vào nhánh xử lý, nên tự ký lấy một chữ ký hợp lệ là đủ và đúng chỗ.
    /// </summary>
    private static string HmacHex(string raw, string key)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));

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
