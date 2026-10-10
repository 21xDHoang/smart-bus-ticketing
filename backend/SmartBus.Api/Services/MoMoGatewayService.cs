using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SmartBus.Api.Dtos.Payments;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IMoMoGatewayService"/> — nói chuyện trực tiếp với cổng MoMo (US 6).
/// Task *"Tích hợp SDK MoMo: tạo giao dịch, nhận callback"* — Trần Trung Hiếu.
///
/// Chuẩn chữ ký MoMo (v2 gateway): HMAC-SHA256 trên chuỗi <c>key=value</c> xếp theo BẢNG CHỮ CÁI,
/// khoá là SecretKey, kết quả hex chữ thường. Thứ tự sai là lỗi kinh điển của tích hợp MoMo —
/// cổng trả <c>resultCode = 20 "Bad format request"</c> mà không nói sai ở đâu. Ba bộ trường:
///
/// • Tạo giao dịch: accessKey, amount, extraData, ipnUrl, orderId, orderInfo, partnerCode,
///   redirectUrl, requestId, requestType — accessKey chỉ nằm trong CHUỖI CHỮ KÝ, không nằm
///   trong body JSON.
/// • IPN callback: accessKey, amount, extraData, message, orderId, orderInfo, orderType,
///   partnerCode, payType, requestId, responseTime, resultCode, transId.
/// • Hỏi trạng thái: accessKey, orderId, partnerCode, requestId.
///
/// Ranh giới: KHÔNG biết bảng Payments (migration của Vàng Thị Dăm) — service nghiệp vụ thanh
/// toán sẽ gọi vào đây khi bảng có. Lỗi vận chuyển (HTTP chết, cổng không trả lời) cố ý để
/// NÉM LÊN người gọi: tạo giao dịch thì endpoint trả 502, job đối soát thì bỏ qua lượt chạy —
/// còn lỗi NGHIỆP VỤ của cổng (resultCode khác 0) trả về trong result để người gọi đọc Message.
/// </summary>
public class MoMoGatewayService : IMoMoGatewayService
{
    private const string CaptureWallet = "captureWallet";

    private readonly MoMoOptions _options;
    private readonly HttpClient _http;

    public MoMoGatewayService(IOptions<MoMoOptions> options, HttpClient httpClient)
    {
        _options = options.Value;
        _http = httpClient;

        // Thiếu cấu hình đối tác thì mọi chữ ký đều sai và cổng chỉ trả "Bad format request" —
        // chết sớm với thông báo nói đúng chỗ thiếu, cùng lối ChuoiKetNoiCsdl.KiemTra.
        var missing = _options.MissingPiece();
        if (missing is not null)
        {
            throw new InvalidOperationException(
                $"Chưa cấu hình cổng thanh toán MoMo: thiếu {missing}. Đặt qua dotnet user-secrets " +
                $"hoặc biến môi trường (xem appsettings.Development.json.example) — KHÔNG commit " +
                $"giá trị thật lên repo public (luật 2).");
        }
    }

    public async Task<MoMoCreatePaymentResult> CreatePaymentAsync(
        MoMoCreatePaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        var requestId = string.IsNullOrWhiteSpace(request.RequestId)
            ? Guid.NewGuid().ToString()
            : request.RequestId;

        // Thứ tự trường đúng chuẩn MoMo — xếp theo bảng chữ cái, đừng sắp lại cho "đẹp".
        // Số định dạng culture bất biến: "175000" phải là "175000" dù máy chạy văn hoá nào.
        var rawSignature = string.Join("&",
            $"accessKey={_options.AccessKey}",
            $"amount={request.Amount.ToString(CultureInfo.InvariantCulture)}",
            $"extraData={request.ExtraData}",
            $"ipnUrl={request.IpnUrl}",
            $"orderId={request.OrderId}",
            $"orderInfo={request.OrderInfo}",
            $"partnerCode={_options.PartnerCode}",
            $"redirectUrl={request.RedirectUrl}",
            $"requestId={requestId}",
            $"requestType={CaptureWallet}");

        var body = new
        {
            partnerCode = _options.PartnerCode,
            requestId,
            amount = request.Amount,
            orderId = request.OrderId,
            orderInfo = request.OrderInfo,
            redirectUrl = request.RedirectUrl,
            ipnUrl = request.IpnUrl,
            extraData = request.ExtraData,
            requestType = CaptureWallet,
            signature = Sign(rawSignature),
            lang = "vi",
        };

        using var response = await _http.PostAsJsonAsync(
            $"{_options.Endpoint.TrimEnd('/')}/v2/gateway/api/create",
            body,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<CreatePaymentResponseJson>(
            JsonOptions, cancellationToken) ?? new CreatePaymentResponseJson();

        return new MoMoCreatePaymentResult
        {
            Success = json.ResultCode == 0,
            PayUrl = json.PayUrl ?? string.Empty,
            Deeplink = json.Deeplink,
            QrCodeUrl = json.QrCodeUrl,
            ResultCode = json.ResultCode,
            Message = json.Message ?? string.Empty,
        };
    }

    public bool IsValidCallback(MoMoCallback callback)
    {
        // Thứ tự trường của IPN — bộ trường KHÁC bộ trường tạo giao dịch (có orderType, message,
        // payType, responseTime, resultCode, transId và KHÔNG có ipnUrl/redirectUrl/requestType).
        var rawSignature = string.Join("&",
            $"accessKey={_options.AccessKey}",
            $"amount={callback.Amount.ToString(CultureInfo.InvariantCulture)}",
            $"extraData={callback.ExtraData}",
            $"message={callback.Message}",
            $"orderId={callback.OrderId}",
            $"orderInfo={callback.OrderInfo}",
            $"orderType={callback.OrderType}",
            $"partnerCode={callback.PartnerCode}",
            $"payType={callback.PayType}",
            $"requestId={callback.RequestId}",
            $"responseTime={callback.ResponseTime.ToString(CultureInfo.InvariantCulture)}",
            $"resultCode={callback.ResultCode.ToString(CultureInfo.InvariantCulture)}",
            $"transId={callback.TransId.ToString(CultureInfo.InvariantCulture)}");

        return SignaturesMatch(Sign(rawSignature), callback.Signature);
    }

    public async Task<MoMoQueryTransactionResult> QueryTransactionAsync(
        string orderId,
        CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid().ToString();

        // Thứ tự trường của query — bộ ngắn nhất trong ba bộ.
        var rawSignature = string.Join("&",
            $"accessKey={_options.AccessKey}",
            $"orderId={orderId}",
            $"partnerCode={_options.PartnerCode}",
            $"requestId={requestId}");

        var body = new
        {
            partnerCode = _options.PartnerCode,
            requestId,
            orderId,
            signature = Sign(rawSignature),
            lang = "vi",
        };

        using var response = await _http.PostAsJsonAsync(
            $"{_options.Endpoint.TrimEnd('/')}/v2/gateway/api/query",
            body,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<QueryTransactionResponseJson>(
            JsonOptions, cancellationToken) ?? new QueryTransactionResponseJson();

        return new MoMoQueryTransactionResult
        {
            Success = json.ResultCode == 0,
            TransId = json.TransId,
            Amount = json.Amount,
            ResultCode = json.ResultCode,
            Message = json.Message ?? string.Empty,
        };
    }

    /// <summary>HMAC-SHA256 với SecretKey, trả hex chữ thường — định dạng chữ ký MoMo chấp nhận.</summary>
    private string Sign(string rawSignature)
    {
        var keyBytes = Encoding.UTF8.GetBytes(_options.SecretKey);
        var dataBytes = Encoding.UTF8.GetBytes(rawSignature);

        using var hmac = new HMACSHA256(keyBytes);

        return Convert.ToHexStringLower(hmac.ComputeHash(dataBytes));
    }

    /// <summary>
    /// So chữ ký bằng phép so sánh HẰNG THỜI GIAN — so chuỗi thường thì kẻ tấn công đo được độ
    /// dài tiền tố khớp qua thời gian phản hồi. Hex lỗi (không phải hex) coi như không khớp.
    /// </summary>
    private static bool SignaturesMatch(string expected, string actual)
    {
        try
        {
            var expectedBytes = Convert.FromHexString(expected);
            var actualBytes = Convert.FromHexString(actual);

            return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>JSON camelCase + không phân biệt hoa thường — MoMo trả camelCase, ta đọc bằng PascalCase.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Hình dạng response của POST /v2/gateway/api/create — chỉ lấy các trường dùng tới.</summary>
    private sealed class CreatePaymentResponseJson
    {
        public int ResultCode { get; set; }

        public string? Message { get; set; }

        public string? PayUrl { get; set; }

        public string? Deeplink { get; set; }

        public string? QrCodeUrl { get; set; }
    }

    /// <summary>Hình dạng response của POST /v2/gateway/api/query — chỉ lấy các trường dùng tới.</summary>
    private sealed class QueryTransactionResponseJson
    {
        public int ResultCode { get; set; }

        public string? Message { get; set; }

        public long TransId { get; set; }

        public long Amount { get; set; }
    }
}
