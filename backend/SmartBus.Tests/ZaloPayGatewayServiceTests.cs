using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SmartBus.Api.Dtos.Payments;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test cho <see cref="ZaloPayGatewayService"/> — client cổng thanh toán ZaloPay (US 6, task
/// *"Tích hợp ZaloPay và thẻ ngân hàng"* — Nguyễn Duy Kiên).
///
/// Không cần CSDL: mọi cuộc gọi cổng đi qua <see cref="FakeHandler"/> — bắt nguyên request gửi đi,
/// trả response định trước. Chữ ký ghim bằng vector tính SẴN NGOÀI C# (Node crypto, cùng tinh thần
/// bộ test MoMo dùng Python): đổi thứ tự trường, đổi khoá, hay đổi cách ghép chuỗi là đỏ ngay.
///
/// ⚠️ Đây là test chống hồi quy theo ĐẶC TẢ, KHÔNG phải bằng chứng cổng ZaloPay thật đã chấp nhận
/// chữ ký — muốn chắc phải chạy sandbox với khoá đối tác do ZaloPay cấp, nhóm chưa có. Riêng chuỗi
/// MAC của /v2/query còn là chỗ tài liệu ZaloPay tự mâu thuẫn (xem chú thích ở service).
/// </summary>
public class ZaloPayGatewayServiceTests
{
    // Bộ dữ liệu đối tác GIẢ của test — không phải khoá thật (luật 2, repo public). Cùng bộ giá trị
    // đưa vào script Node tính vector.
    private const string AppId = "2553";
    private const string Key1 = "test-key1-khong-phai-khoa-that";
    private const string Key2 = "test-key2-khong-phai-khoa-that";

    private const string AppTransId = "261010_PM-8f3a2c1d";
    private const string AppUser = "smartbus";
    private const long Amount = 175_000;

    /// <summary>Mốc thời gian ghim sẵn: 2026-10-10 15:32 giờ Việt Nam (08:32 UTC).</summary>
    private static readonly DateTimeOffset AppTime = DateTimeOffset.Parse("2026-10-10T08:32:00Z");

    private const long AppTimeMillis = 1791621120000;

    /// <summary>
    /// Chữ ký request tạo đơn với bộ dữ liệu trên, tính bằng Node crypto (hmac-sha256, Key1):
    /// "2553|261010_PM-8f3a2c1d|smartbus|175000|1791621120000|{}|[]".
    /// </summary>
    private const string CreateMacVector =
        "d015da7c85efdc75f8b0bdefcf3b9d71971f3b1ebbab4cd20343708fdaebda41";

    /// <summary>
    /// Chữ ký request tạo đơn khi GHIM KÊNH — embed_data đã thành
    /// {"preferred_payment_method":["domestic_card"]} trước khi ký. Đây là ca dễ sai nhất: ký trên
    /// embed_data cũ rồi gửi embed_data mới là chữ ký lệch ngay.
    /// </summary>
    private const string CreateMacPinnedChannelVector =
        "15ff64d405f7a1d19e7b2a05b95f180c77a88004599abae9581f1c4096d1a5f8";

    /// <summary>
    /// Chữ ký request hỏi trạng thái, cũng Node crypto (Key1):
    /// "2553|261010_PM-8f3a2c1d|test-key1-khong-phai-khoa-that" — phần tử thứ ba LÀ CHÍNH KHOÁ.
    /// </summary>
    private const string QueryMacVector =
        "63b28190497c6c5165d2edb8faeb15193252e9704dcf807f1c92ad0515a5f575";

    /// <summary>
    /// Chuỗi <c>data</c> cổng gửi trong callback — ĐÚNG nguyên văn đã đưa vào script Node, vì chữ ký
    /// tính trên chính chuỗi này (thứ tự khoá và khoảng trắng không được dựng lại).
    /// </summary>
    private const string CallbackData =
        "{\"app_id\":2553,\"app_trans_id\":\"261010_PM-8f3a2c1d\",\"app_user\":\"smartbus\","
        + "\"amount\":175000,\"zp_trans_id\":2609270000012345,\"server_time\":1791621120000,"
        + "\"channel\":38}";

    /// <summary>MAC hợp lệ của <see cref="CallbackData"/> — HMAC-SHA256 bằng Key2 (Node crypto).</summary>
    private const string CallbackMacVector =
        "8a6f812c2e8c56212a8402f4ea7ab9626d3be3932908fe69f744113c6377f80c";

    /// <summary>Cùng chuỗi <c>data</c> nhưng ký bằng Key1 — sai khoá, phải bị từ chối.</summary>
    private const string CallbackMacSignedWithKey1 =
        "f18066df8723e88a7d934e64f4e0e5f1718a5047a43f53b96299b73aa8a6a966";

    private static ZaloPayGatewayService ServiceWith(FakeHandler handler) => new(
        Options.Create(new ZaloPayOptions
        {
            AppId = AppId,
            Key1 = Key1,
            Key2 = Key2,
            Endpoint = "https://sb-openapi.zalopay.vn",
        }),
        new HttpClient(handler));

    private static ZaloPayCreateOrderRequest CreateRequest() => new()
    {
        AppTransId = AppTransId,
        AppUser = AppUser,
        Amount = Amount,
        Description = "Thanh toan ve xe SmartBus",
        AppTime = AppTime,
    };

    // ---------------------------------------------------------------------------------------
    // Tạo đơn
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Tao_don_ky_dung_chuoi_chuan_ZaloPay()
    {
        var handler = new FakeHandler(_ => JsonResponse(new { return_code = 1, order_url = "https://sb-openapi.zalopay.vn/pay" }));
        var service = ServiceWith(handler);

        await service.CreateOrderAsync(CreateRequest());

        var sent = JsonDocument.Parse(await ReadBodyAsync(handler)).RootElement;

        // Chốt quan trọng nhất: chuỗi ký nối bằng '|' theo ĐÚNG thứ tự tài liệu, khoá là Key1.
        Assert.Equal(CreateMacVector, sent.GetProperty("mac").GetString());
        // app_id phải là SỐ trong JSON (tài liệu ZaloPay khai int32) — gửi chuỗi là cổng từ chối.
        Assert.Equal(JsonValueKind.Number, sent.GetProperty("app_id").ValueKind);
        Assert.Equal(2553, sent.GetProperty("app_id").GetInt64());
        Assert.Equal(AppTransId, sent.GetProperty("app_trans_id").GetString());
        Assert.Equal(AppUser, sent.GetProperty("app_user").GetString());
        Assert.Equal(Amount, sent.GetProperty("amount").GetInt64());
        Assert.Equal(AppTimeMillis, sent.GetProperty("app_time").GetInt64());
        Assert.Equal("{}", sent.GetProperty("embed_data").GetString());
        Assert.Equal("[]", sent.GetProperty("item").GetString());
    }

    [Fact]
    public async Task Tao_don_khong_gui_tham_so_tuy_chon_khi_de_trong()
    {
        var handler = new FakeHandler(_ => JsonResponse(new { return_code = 1 }));
        var service = ServiceWith(handler);

        await service.CreateOrderAsync(CreateRequest());

        var sent = JsonDocument.Parse(await ReadBodyAsync(handler)).RootElement;

        // Tham số tuỳ chọn không có thì KHÔNG có mặt trong body — gửi chuỗi rỗng là cổng hiểu khác hẳn.
        Assert.False(sent.TryGetProperty("bank_code", out _));
        Assert.False(sent.TryGetProperty("callback_url", out _));
    }

    [Fact]
    public async Task Tao_don_ghim_kenh_thi_ky_tren_embed_data_DA_CHEN()
    {
        var handler = new FakeHandler(_ => JsonResponse(new { return_code = 1 }));
        var service = ServiceWith(handler);

        var request = CreateRequest();
        request.PreferredPaymentMethod = "domestic_card";

        await service.CreateOrderAsync(request);

        var sent = JsonDocument.Parse(await ReadBodyAsync(handler)).RootElement;

        // Chữ ký phải phủ ĐÚNG chuỗi embed_data gửi đi (đã chèn kênh), không phải chuỗi "{}" ban đầu.
        Assert.Equal(CreateMacPinnedChannelVector, sent.GetProperty("mac").GetString());
        Assert.Equal(
            "{\"preferred_payment_method\":[\"domestic_card\"]}",
            sent.GetProperty("embed_data").GetString());
    }

    [Fact]
    public async Task Tao_don_cong_tu_choi_tra_ve_result_chu_khong_nem()
    {
        var handler = new FakeHandler(_ => JsonResponse(new
        {
            return_code = 2,
            return_message = "Giao dich that bai",
            sub_return_code = -68,
            sub_return_message = "Duplicate app_trans_id",
        }));
        var service = ServiceWith(handler);

        var result = await service.CreateOrderAsync(CreateRequest());

        // return_code = 1 mới là thành công (KHÁC MoMo: 0) — 2 là hỏng, và phải trả về chứ không ném.
        Assert.False(result.Success);
        Assert.Equal(2, result.ReturnCode);
        Assert.Equal("Giao dich that bai", result.Message);
        Assert.Equal(-68, result.SubReturnCode);
        Assert.Equal("Duplicate app_trans_id", result.SubReturnMessage);
        Assert.Equal(string.Empty, result.OrderUrl);
    }

    [Fact]
    public async Task Tao_don_cong_nhan_tra_order_url()
    {
        var handler = new FakeHandler(_ => JsonResponse(new
        {
            return_code = 1,
            return_message = "Success",
            order_url = "https://sb-openapi.zalopay.vn/v2/pay/abc",
            qr_code = "data:image/png;base64,AAA",
        }));
        var service = ServiceWith(handler);

        var result = await service.CreateOrderAsync(CreateRequest());

        Assert.True(result.Success);
        Assert.Equal("https://sb-openapi.zalopay.vn/v2/pay/abc", result.OrderUrl);
        Assert.Equal("data:image/png;base64,AAA", result.QrCode);
    }

    // ---------------------------------------------------------------------------------------
    // Xác thực callback
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Callback_chap_nhan_vector_tinh_ngoai_CSharp()
    {
        var service = ServiceWith(new FakeHandler(_ => JsonResponse(new { })));

        var callback = new ZaloPayCallback { Data = CallbackData, Mac = CallbackMacVector, Type = 1 };

        Assert.True(service.IsValidCallback(callback));
    }

    [Fact]
    public void Callback_sua_so_tien_thi_chu_ky_sai()
    {
        var service = ServiceWith(new FakeHandler(_ => JsonResponse(new { })));

        // Kẻ tấn công sửa số tiền trong data nhưng giữ nguyên mac của cổng.
        var tampered = CallbackData.Replace("\"amount\":175000", "\"amount\":1");

        Assert.False(service.IsValidCallback(
            new ZaloPayCallback { Data = tampered, Mac = CallbackMacVector, Type = 1 }));
    }

    [Fact]
    public void Callback_ky_bang_Key1_thi_khong_hop_le()
    {
        var service = ServiceWith(new FakeHandler(_ => JsonResponse(new { })));

        // Key1 là khoá chiều TA GỬI ĐI — dùng nó cho chiều cổng gửi về là sai, phải bị từ chối.
        Assert.False(service.IsValidCallback(
            new ZaloPayCallback { Data = CallbackData, Mac = CallbackMacSignedWithKey1, Type = 1 }));
    }

    [Fact]
    public void Callback_mac_khong_phai_hex_tra_false_khong_nem()
    {
        var service = ServiceWith(new FakeHandler(_ => JsonResponse(new { })));

        Assert.False(service.IsValidCallback(
            new ZaloPayCallback { Data = CallbackData, Mac = "khong-phai-hex", Type = 1 }));
    }

    [Fact]
    public void Callback_rong_tra_false_khong_nem()
    {
        var service = ServiceWith(new FakeHandler(_ => JsonResponse(new { })));

        Assert.False(service.IsValidCallback(new ZaloPayCallback()));
    }

    [Fact]
    public void ParseCallbackData_doc_duoc_ruot_va_bo_qua_truong_la()
    {
        var service = ServiceWith(new FakeHandler(_ => JsonResponse(new { })));

        // CallbackData có trường "channel" mà DTO không khai — JsonSerializer phải bỏ qua, không ném.
        var data = service.ParseCallbackData(CallbackData);

        Assert.NotNull(data);
        Assert.Equal(AppTransId, data.AppTransId);
        Assert.Equal(Amount, data.Amount);
        Assert.Equal(2609270000012345, data.ZpTransId);
        Assert.Equal("smartbus", data.AppUser);
    }

    [Fact]
    public void ParseCallbackData_json_hong_tra_null_khong_nem()
    {
        var service = ServiceWith(new FakeHandler(_ => JsonResponse(new { })));

        Assert.Null(service.ParseCallbackData("khong-phai-json"));
        Assert.Null(service.ParseCallbackData(string.Empty));
    }

    // ---------------------------------------------------------------------------------------
    // Hỏi trạng thái
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Hoi_trang_thai_ky_dung_chuoi_dac_ta()
    {
        var handler = new FakeHandler(_ => JsonResponse(new { return_code = 1, amount = 175000, zp_trans_id = 2609270000012345 }));
        var service = ServiceWith(handler);

        var result = await service.QueryOrderAsync(AppTransId);

        var sent = JsonDocument.Parse(await ReadBodyAsync(handler)).RootElement;

        // Ký bằng Key1, và phần tử thứ ba của chuỗi MAC là CHÍNH KHOÁ (chỗ tài liệu ZaloPay mâu thuẫn).
        Assert.Equal(QueryMacVector, sent.GetProperty("mac").GetString());
        Assert.Equal(AppTransId, sent.GetProperty("app_trans_id").GetString());

        Assert.True(result.Success);
        Assert.False(result.IsProcessing);
        Assert.Equal(175000, result.Amount);
        Assert.Equal(2609270000012345, result.ZpTransId);
    }

    [Fact]
    public async Task Hoi_trang_thai_dang_xu_ly_thi_IsProcessing_chu_khong_phai_that_bai()
    {
        var handler = new FakeHandler(_ => JsonResponse(new { return_code = 3, return_message = "dang xu ly" }));
        var service = ServiceWith(handler);

        var result = await service.QueryOrderAsync(AppTransId);

        // Trạng thái thứ ba MoMo không có: đơn còn đang xử lý. Coi là hỏng ở đây là giết oan đơn
        // khách còn đang trả tiền.
        Assert.False(result.Success);
        Assert.True(result.IsProcessing);
        Assert.Equal(3, result.ReturnCode);
    }

    [Fact]
    public async Task Hoi_trang_thai_that_bai_tra_Message_cua_cong()
    {
        var handler = new FakeHandler(_ => JsonResponse(new { return_code = 2, return_message = "khong tim thay don" }));
        var service = ServiceWith(handler);

        var result = await service.QueryOrderAsync(AppTransId);

        Assert.False(result.Success);
        Assert.False(result.IsProcessing);
        Assert.Equal("khong tim thay don", result.Message);
    }

    // ---------------------------------------------------------------------------------------
    // app_trans_id — quy tắc riêng của ZaloPay
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void BuildAppTransId_ghep_tien_to_yymmdd_theo_gio_Viet_Nam()
    {
        // 08:32 UTC = 15:32 giờ VN, cùng ngày. Mốc ghép tiền tố là giờ VN chứ không phải UTC.
        var appTransId = ZaloPayGatewayService.BuildAppTransId("PM-8f3a2c1d", AppTime);

        Assert.Equal("261010_PM-8f3a2c1d", appTransId);
    }

    [Fact]
    public void BuildAppTransId_moc_UTC_lech_ngay_thi_theo_gio_Viet_Nam()
    {
        // 2026-10-09 18:00 UTC ĐÃ là 2026-10-10 01:00 giờ VN — lấy theo UTC là sai ngày, cổng từ chối.
        var appTransId = ZaloPayGatewayService.BuildAppTransId(
            "PM-1", DateTimeOffset.Parse("2026-10-09T18:00:00Z"));

        Assert.Equal("261010_PM-1", appTransId);
    }

    [Fact]
    public void BuildAppTransId_qua_tran_40_ky_tu_thi_nem()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            ZaloPayGatewayService.BuildAppTransId(new string('x', 40), AppTime));

        Assert.Contains("40", ex.Message);
    }

    [Fact]
    public void StripAppTransIdPrefix_bo_tien_to_ra_paymentCode()
    {
        Assert.Equal("PM-8f3a2c1d", ZaloPayGatewayService.StripAppTransIdPrefix("261010_PM-8f3a2c1d"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("PM-8f3a2c1d")]              // không có tiền tố
    [InlineData("261010PM-8f3a2c1d")]        // thiếu dấu '_' ở vị trí thứ 7
    [InlineData("26101_PM-1")]               // ngắn hơn dạng chuẩn
    public void StripAppTransIdPrefix_chuoi_la_tra_nguyen_van(string input)
    {
        // Thà để bước tra bảng Payments không thấy rồi trả 404, còn hơn cắt bừa rồi tra nhầm sang
        // giao dịch của người khác.
        Assert.Equal(input, ZaloPayGatewayService.StripAppTransIdPrefix(input));
    }

    // ---------------------------------------------------------------------------------------
    // Cấu hình
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("", Key1, Key2, "ZaloPay:AppId")]
    [InlineData(AppId, "", Key2, "ZaloPay:Key1")]
    [InlineData(AppId, Key1, "", "ZaloPay:Key2")]
    [InlineData(AppId, "   ", Key2, "ZaloPay:Key1")]
    public void Thieu_cau_hinh_thi_nem_ngay_khi_dung_service(
        string appId, string key1, string key2, string expectedMissing)
    {
        var options = new ZaloPayOptions { AppId = appId, Key1 = key1, Key2 = key2 };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new ZaloPayGatewayService(Options.Create(options), new HttpClient()));

        Assert.Contains(expectedMissing, ex.Message);
        Assert.Contains("user-secrets", ex.Message);
    }

    [Theory]
    [InlineData("khong-phai-so")]
    [InlineData("02553")] // số 0 thừa: body gửi cổng thành 2553 mà chuỗi ký vẫn "02553" → cổng từ chối
    [InlineData("-1")]
    [InlineData("2553 ")]
    public void AppId_khong_phai_so_nguyen_chuan_thi_nem(string appId)
    {
        var options = new ZaloPayOptions { AppId = appId, Key1 = Key1, Key2 = Key2 };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new ZaloPayGatewayService(Options.Create(options), new HttpClient()));

        Assert.Contains("ZaloPay:AppId", ex.Message);
    }

    // ---------------------------------------------------------------------------------------
    // Helper
    // ---------------------------------------------------------------------------------------

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
