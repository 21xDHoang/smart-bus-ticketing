using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SmartBus.Api.Dtos.Payments;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IZaloPayGatewayService"/> — nói chuyện trực tiếp với cổng ZaloPay (US 6).
/// Task *"Tích hợp ZaloPay và thẻ ngân hàng"* — Nguyễn Duy Kiên.
///
/// Chuẩn chữ ký ZaloPay (Gateway API v2): HMAC-SHA256, các trường nối bằng <c>|</c>, hex chữ thường.
/// Khác MoMo/VNPay ở chỗ cổng dùng HAI khoá cho HAI CHIỀU — <c>Key1</c> ký request ta gửi đi,
/// <c>Key2</c> xác thực callback cổng gửi về. Bốn chuỗi ký:
///
/// • Tạo đơn:       app_id|app_trans_id|app_user|amount|app_time|embed_data|item
/// • Hỏi trạng thái: app_id|app_trans_id|Key1   (phần tử thứ ba LÀ CHÍNH KHOÁ — xem cảnh báo dưới)
/// • Callback:      chính chuỗi <c>data</c> thô, KHÔNG nối thêm gì, ký bằng Key2
///
/// ⚠️ Chỗ tài liệu ZaloPay mâu thuẫn: trang spec hiện hành ghi chuỗi MAC của /v2/query là
/// <c>app_id|app_trans_id|key1</c> (phần tử thứ ba là chính khoá), còn bản PDF "ZaloPay APIs
/// Integration Document" đời cũ lại đặt <c>app_time</c> ở vị trí đó. Ở đây theo bản hiện hành, và
/// <see cref="BuildQueryMacInput"/> tách riêng nên đổi sang biến thể kia chỉ là sửa một dòng. Muốn
/// chắc phải chạy sandbox với khoá đối tác thật — nhóm chưa có, nên test hiện tại là test chống
/// hồi quy theo đặc tả chứ không phải bằng chứng cổng thật đã chấp nhận.
///
/// ⚠️ <c>app_trans_id</c> KHÁC orderId của MoMo ở chỗ cổng bắt buộc mã phải bắt đầu bằng
/// <c>yymmdd</c> giờ Việt Nam (GMT+7) và dài tối đa 40 ký tự — xem <see cref="BuildAppTransId"/>.
/// Vì vậy mã gửi cổng KHÔNG phải paymentCode trần, và callback phải bỏ tiền tố mới ra paymentCode
/// (<see cref="StripAppTransIdPrefix"/>) — chuyện này do tầng adapter lo, endpoint không thấy.
///
/// Bộ mã kết quả cũng khác MoMo: <c>return_code = 1</c> là THÀNH CÔNG (không phải 0), <c>2</c> thất
/// bại, <c>3</c> đang xử lý.
///
/// Ranh giới: KHÔNG biết bảng Payments (migration của Vàng Thị Dăm) — service nghiệp vụ thanh toán
/// sẽ gọi vào đây khi bảng có. Lỗi vận chuyển (HTTP chết) cố ý để NÉM LÊN người gọi; lỗi NGHIỆP VỤ
/// của cổng trả về trong result để người gọi đọc Message.
/// </summary>
public class ZaloPayGatewayService : IZaloPayGatewayService
{
    /// <summary>Mã thành công của ZaloPay — KHÁC MoMo (0). So với 0 là hỏng toàn bộ nhánh thành công.</summary>
    private const int SuccessReturnCode = 1;

    /// <summary>Mã "đơn đang xử lý" — trạng thái thứ ba MoMo không có, không được coi là hỏng.</summary>
    private const int ProcessingReturnCode = 3;

    /// <summary>Định dạng tiền tố của app_trans_id theo tài liệu ZaloPay.</summary>
    private const string AppTransIdDateFormat = "yyMMdd";

    /// <summary>Trần độ dài app_trans_id theo tài liệu ZaloPay.</summary>
    private const int AppTransIdMaxLength = 40;

    /// <summary>
    /// Giờ Việt Nam (GMT+7) — tiền tố app_trans_id tính theo giờ này chứ không theo UTC: gửi lệch
    /// ngày là cổng từ chối. Việt Nam không dùng DST từ 1975 nên lệch cố định, khỏi tra TimeZoneInfo.
    /// </summary>
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    private readonly ZaloPayOptions _options;
    private readonly HttpClient _http;

    /// <summary>
    /// app_id dạng SỐ để đưa vào body — tài liệu ZaloPay khai trường này là <c>int32</c>, gửi chuỗi
    /// là cổng từ chối. Chuỗi ký thì vẫn dùng nguyên văn cấu hình; phép kiểm dưới đây bảo đảm hai
    /// dạng ấy là cùng một giá trị nên chữ ký và body không bao giờ lệch nhau.
    /// </summary>
    private readonly long _appId;

    public ZaloPayGatewayService(IOptions<ZaloPayOptions> options, HttpClient httpClient)
    {
        _options = options.Value;
        _http = httpClient;

        // Thiếu cấu hình đối tác thì mọi chữ ký đều sai và cổng chỉ trả thông báo chung chung —
        // chết sớm với thông báo nói đúng chỗ thiếu (cùng lối MoMoGatewayService).
        if (_options.MissingPiece() is { } missing)
        {
            throw new InvalidOperationException(
                $"Chưa cấu hình cổng thanh toán ZaloPay: thiếu {missing}. Đặt qua dotnet user-secrets "
                + "hoặc biến môi trường (xem appsettings.Development.json.example) — KHÔNG commit "
                + "giá trị thật lên repo public (luật 2).");
        }

        // AppId phải là số nguyên KHÔNG có số 0 thừa: body gửi cổng là dạng số còn chuỗi ký dùng
        // nguyên văn cấu hình, "02553" sẽ thành 2553 trong body nhưng "02553" trong chữ ký — cổng
        // tính lại chữ ký theo app_id của nó (2553) và từ chối. Chặn ngay ở đây thay vì để cổng trả
        // một câu chung chung về chữ ký sai.
        if (!long.TryParse(_options.AppId, NumberStyles.None, CultureInfo.InvariantCulture, out _appId)
            || _appId.ToString(CultureInfo.InvariantCulture) != _options.AppId)
        {
            throw new InvalidOperationException(
                $"Cấu hình ZaloPay:AppId phải là số nguyên (tài liệu ZaloPay khai app_id là int32, ví "
                + $"dụ 2553) — nhận được '{_options.AppId}'.");
        }
    }

    /// <summary>
    /// Dựng app_trans_id từ paymentCode nội bộ: <c>{yymmdd}_{paymentCode}</c> theo giờ Việt Nam.
    ///
    /// Vì sao không dùng thẳng paymentCode: cổng BẮT BUỘC mã bắt đầu bằng <c>yymmdd</c> giờ GMT+7,
    /// mà paymentCode của hợp đồng (ví dụ "PM-8f3a2c1d") không có dạng đó. Ghép tiền tố vừa thoả
    /// ràng buộc vừa giữ nguyên tính duy nhất (paymentCode đã duy nhất theo A9).
    ///
    /// <paramref name="moment"/> truyền vào chứ không lấy giờ hệ thống bên trong: đây là quy tắc
    /// định dạng của cổng, phải test được bằng mốc thời gian ghim sẵn.
    /// </summary>
    public static string BuildAppTransId(string paymentCode, DateTimeOffset moment)
    {
        if (string.IsNullOrWhiteSpace(paymentCode))
        {
            throw new ArgumentException("paymentCode không được rỗng.", nameof(paymentCode));
        }

        var prefix = moment.ToOffset(VietnamOffset).ToString(AppTransIdDateFormat, CultureInfo.InvariantCulture);
        var appTransId = $"{prefix}_{paymentCode}";

        if (appTransId.Length > AppTransIdMaxLength)
        {
            throw new ArgumentException(
                $"app_trans_id dài {appTransId.Length} ký tự, quá trần {AppTransIdMaxLength} của ZaloPay "
                + $"(paymentCode '{paymentCode}' quá dài).",
                nameof(paymentCode));
        }

        return appTransId;
    }

    /// <summary>
    /// Bỏ tiền tố <c>yymmdd_</c> để về paymentCode nội bộ. Chỉ cắt khi chuỗi ĐÚNG dạng (ký tự thứ 7
    /// là '_') — chuỗi lạ thì trả nguyên văn, thà để bước tra bảng Payments không thấy rồi trả 404
    /// còn hơn cắt bừa rồi tra nhầm sang giao dịch của người khác.
    /// </summary>
    public static string StripAppTransIdPrefix(string appTransId)
    {
        if (string.IsNullOrEmpty(appTransId) || appTransId.Length <= 7 || appTransId[6] != '_')
        {
            return appTransId ?? string.Empty;
        }

        return appTransId[7..];
    }

    public async Task<ZaloPayCreateOrderResult> CreateOrderAsync(
        ZaloPayCreateOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        var appTime = request.AppTime ?? DateTimeOffset.UtcNow;
        var item = string.IsNullOrWhiteSpace(request.Item) ? "[]" : request.Item;
        var embedData = BuildEmbedData(request);

        var body = new Dictionary<string, object?>
        {
            // Số, không phải chuỗi: tài liệu ZaloPay khai app_id là int32.
            ["app_id"] = _appId,
            ["app_trans_id"] = request.AppTransId,
            ["app_user"] = request.AppUser,
            ["app_time"] = appTime.ToUnixTimeMilliseconds(),
            ["amount"] = request.Amount,
            ["description"] = request.Description,
            ["item"] = item,
            ["embed_data"] = embedData,
            // Tham số tuỳ chọn không có thì KHÔNG đưa vào body — gửi chuỗi rỗng là cổng hiểu khác hẳn.
            ["mac"] = SignWithKey1(BuildCreateOrderMacInput(request, appTime, embedData, item)),
        };

        if (!string.IsNullOrWhiteSpace(request.BankCode))
        {
            body["bank_code"] = request.BankCode;
        }

        if (!string.IsNullOrWhiteSpace(request.CallbackUrl))
        {
            body["callback_url"] = request.CallbackUrl;
        }

        using var response = await _http.PostAsJsonAsync(
            $"{_options.Endpoint.TrimEnd('/')}/v2/create",
            body,
            JsonOptions,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<CreateOrderResponseJson>(
            JsonOptions, cancellationToken) ?? new CreateOrderResponseJson();

        return new ZaloPayCreateOrderResult
        {
            Success = json.ReturnCode == SuccessReturnCode,
            ReturnCode = json.ReturnCode,
            Message = json.ReturnMessage ?? string.Empty,
            SubReturnCode = json.SubReturnCode,
            SubReturnMessage = json.SubReturnMessage ?? string.Empty,
            OrderUrl = json.OrderUrl ?? string.Empty,
            QrCode = json.QrCode,
        };
    }

    public bool IsValidCallback(ZaloPayCallback callback)
    {
        // Ký trên CHÍNH chuỗi data thô, không nối thêm trường nào và không phân tích lại JSON: thứ
        // tự khoá với khoảng trắng trong chuỗi do cổng quyết định, dựng lại là chữ ký lệch ngay.
        return SignaturesMatch(SignWithKey2(callback.Data), callback.Mac);
    }

    public ZaloPayCallbackData? ParseCallbackData(string data)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ZaloPayCallbackData>(data, JsonOptions);
        }
        catch (JsonException)
        {
            // Không phải JSON — dữ liệu người ngoài gửi tới, không phải lỗi của ta.
            return null;
        }
    }

    public async Task<ZaloPayQueryResult> QueryOrderAsync(
        string appTransId,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["app_id"] = _appId,
            ["app_trans_id"] = appTransId,
            ["mac"] = SignWithKey1(BuildQueryMacInput(appTransId)),
        };

        using var response = await _http.PostAsJsonAsync(
            $"{_options.Endpoint.TrimEnd('/')}/v2/query",
            body,
            JsonOptions,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<QueryOrderResponseJson>(
            JsonOptions, cancellationToken) ?? new QueryOrderResponseJson();

        return new ZaloPayQueryResult
        {
            Success = json.ReturnCode == SuccessReturnCode,
            IsProcessing = json.ReturnCode == ProcessingReturnCode,
            ReturnCode = json.ReturnCode,
            Message = json.ReturnMessage ?? string.Empty,
            Amount = json.Amount,
            ZpTransId = json.ZpTransId,
        };
    }

    /// <summary>Chuỗi ký của /v2/create — thứ tự trường theo tài liệu, đừng sắp lại cho "đẹp".</summary>
    private string BuildCreateOrderMacInput(
        ZaloPayCreateOrderRequest request,
        DateTimeOffset appTime,
        string embedData,
        string item)
    {
        return string.Join(
            "|",
            _options.AppId,
            request.AppTransId,
            request.AppUser,
            // Số định dạng culture bất biến: "175000" phải là "175000" dù máy chạy văn hoá nào.
            request.Amount.ToString(CultureInfo.InvariantCulture),
            appTime.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            embedData,
            item);
    }

    /// <summary>
    /// Chuỗi ký của /v2/query. Tách riêng khỏi lời gọi có chủ đích: đây là chỗ tài liệu ZaloPay tự
    /// mâu thuẫn (bản hiện hành: phần tử thứ ba là chính khoá Key1; bản PDF cũ: app_time). Đổi theo
    /// biến thể kia thì sửa đúng hàm này.
    /// </summary>
    private string BuildQueryMacInput(string appTransId)
    {
        return string.Join("|", _options.AppId, appTransId, _options.Key1);
    }

    /// <summary>
    /// Dựng <c>embed_data</c> gửi cổng. ZaloPay KHÔNG có trường <c>method</c> — muốn ghim kênh thì
    /// đặt <c>preferred_payment_method</c> trong embed_data, nên phải chèn vào chuỗi JSON có sẵn
    /// (nối chuỗi tay là hỏng ngay khi embed_data đã có nội dung khác).
    /// </summary>
    private static string BuildEmbedData(ZaloPayCreateOrderRequest request)
    {
        var embed = string.IsNullOrWhiteSpace(request.EmbedData) ? "{}" : request.EmbedData;

        if (string.IsNullOrWhiteSpace(request.PreferredPaymentMethod))
        {
            return embed;
        }

        JsonObject node;
        try
        {
            node = JsonNode.Parse(embed) as JsonObject
                ?? throw new ArgumentException(
                    $"EmbedData phải là một object JSON để chèn preferred_payment_method, nhận được: {embed}",
                    nameof(request));
        }
        catch (JsonException ex)
        {
            // EmbedData hỏng là lỗi của người gọi (lập trình), không phải dữ liệu người ngoài — ném
            // lên thay vì âm thầm bỏ qua, để không gửi cổng một đơn thiếu kênh đã yêu cầu.
            throw new ArgumentException($"EmbedData không phải JSON hợp lệ: {embed}", nameof(request), ex);
        }

        node["preferred_payment_method"] = new JsonArray(request.PreferredPaymentMethod);

        return node.ToJsonString();
    }

    /// <summary>HMAC-SHA256 bằng Key1 (chiều ta gửi đi), hex chữ thường.</summary>
    private string SignWithKey1(string rawSignature) => Sign(rawSignature, _options.Key1);

    /// <summary>HMAC-SHA256 bằng Key2 (chiều cổng gửi về), hex chữ thường.</summary>
    private string SignWithKey2(string rawSignature) => Sign(rawSignature, _options.Key2);

    private static string Sign(string rawSignature, string key)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));

        return Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(rawSignature)));
    }

    /// <summary>
    /// So chữ ký bằng phép so sánh HẰNG THỜI GIAN — so chuỗi thường thì kẻ tấn công đo được độ dài
    /// tiền tố khớp qua thời gian phản hồi. Hex lỗi (không phải hex) coi như không khớp.
    /// </summary>
    private static bool SignaturesMatch(string expected, string actual)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expected),
                Convert.FromHexString(actual));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>JSON camelCase + không phân biệt hoa thường, dùng cho cả chiều gửi lẫn chiều đọc.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Hình dạng response của POST /v2/create — chỉ lấy các trường dùng tới. ZaloPay trả snake_case
    /// (khác MoMo camelCase) nên phải ghim tên trường bằng <see cref="JsonPropertyNameAttribute"/>;
    /// chính sách camelCase không tự nối được "ReturnCode" với "return_code".
    /// </summary>
    private sealed class CreateOrderResponseJson
    {
        [JsonPropertyName("return_code")]
        public int ReturnCode { get; set; }

        [JsonPropertyName("return_message")]
        public string? ReturnMessage { get; set; }

        [JsonPropertyName("sub_return_code")]
        public int SubReturnCode { get; set; }

        [JsonPropertyName("sub_return_message")]
        public string? SubReturnMessage { get; set; }

        [JsonPropertyName("order_url")]
        public string? OrderUrl { get; set; }

        [JsonPropertyName("qr_code")]
        public string? QrCode { get; set; }
    }

    /// <summary>Hình dạng response của POST /v2/query — chỉ lấy các trường dùng tới.</summary>
    private sealed class QueryOrderResponseJson
    {
        [JsonPropertyName("return_code")]
        public int ReturnCode { get; set; }

        [JsonPropertyName("return_message")]
        public string? ReturnMessage { get; set; }

        [JsonPropertyName("amount")]
        public long Amount { get; set; }

        [JsonPropertyName("zp_trans_id")]
        public long ZpTransId { get; set; }
    }
}
