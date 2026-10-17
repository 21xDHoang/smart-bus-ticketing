using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SmartBus.Api.Dtos.Refunds;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Client hoàn tiền MoMo (US 8 — task *"API gọi cổng thanh toán để hoàn tiền"*, dòng 29 — Trần Trung
/// Hiếu). Nói chuyện với <c>POST {Endpoint}/v2/gateway/api/refund</c> của cổng, cùng chuẩn chữ ký
/// HMAC-SHA256 như chiều thu tiền (<see cref="MoMoGatewayService"/>), nhưng bộ trường khác:
/// <c>accessKey, amount, description, orderId, partnerCode, requestId, transId</c> — xếp theo bảng
/// chữ cái, hex chữ thường.
///
/// Ba quyết định chốt theo tài liệu cổng:
/// <list type="number">
/// <item><b>orderId của lệnh hoàn PHẢI KHÁC orderId của giao dịch mua</b> (<c>Payment.PaymentCode</c>)
/// → dùng <see cref="RefundGatewayCallRequest.RefundCode"/>. Còn <c>requestId = RefundId</c> — khoá
/// idempotency: lượt gửi lại sau timeout với cùng requestId được cổng trả về kết quả lượt trước,
/// không bao giờ hoàn hai lần.</item>
/// <item><b>Timeout tối thiểu 30 giây</b> — cổng bắt buộc ghi trong tài liệu; đặt ngắn hơn là tự cắt
/// mất câu trả lời rồi phải đoán.</item>
/// <item><b>transId</b> là mã giao dịch MoMo của lượt THU (<c>Payment.GatewayTransactionId</c>) —
/// lệnh hoàn phải trỏ đúng giao dịch đã thu tiền.</item>
/// </list>
///
/// Lỗi vận chuyển (HTTP chết, timeout) NÉM LÊN người gọi — <see cref="RefundGatewayCaller"/> dịch
/// sang <see cref="RefundLogResult.Unresolved"/>. Lỗi nghiệp vụ của cổng (resultCode khác 0) trả về
/// trong kết quả kèm lời cổng.
/// </summary>
public class MoMoRefundClient : IRefundGatewayClient
{
    /// <summary>MoMo yêu cầu timeout TỐI THIỂU 30 giây cho API hoàn tiền — ngắn hơn là tự cắt câu trả lời.</summary>
    private static readonly TimeSpan MoMoMandatoryTimeout = TimeSpan.FromSeconds(30);

    private readonly MoMoOptions _options;
    private readonly HttpClient _http;

    public MoMoRefundClient(IOptions<MoMoOptions> options, HttpClient httpClient)
    {
        _options = options.Value;
        _http = httpClient;

        // Thiếu cấu hình đối tác thì mọi chữ ký đều sai và cổng chỉ trả "Bad format request" —
        // chết sớm với thông báo nói đúng chỗ thiếu (cùng lối MoMoGatewayService).
        if (_options.MissingPiece() is { } missing)
        {
            throw new InvalidOperationException(
                $"Chưa cấu hình cổng thanh toán MoMo: thiếu {missing}. Đặt qua dotnet user-secrets " +
                "hoặc biến môi trường (xem appsettings.Development.json.example) — KHÔNG commit " +
                "giá trị thật lên repo public (luật 2).");
        }
    }

    /// <inheritdoc />
    public async Task<RefundGatewayCallOutcome> RefundAsync(
        RefundGatewayCallRequest request,
        Payment payment,
        CancellationToken cancellationToken = default)
    {
        // Lệnh hoàn phải trỏ ĐÚNG giao dịch đã thu: thiếu mã giao dịch MoMo thì không có gì để hoàn.
        // Đây là câu trả lời chắc chắn (thử lại cũng không đổi) nên là Failed, không phải Unresolved.
        if (!long.TryParse(
                payment.GatewayTransactionId,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var transId))
        {
            return new RefundGatewayCallOutcome
            {
                Result = RefundLogResult.Failed,
                Message = "Giao dịch gốc thiếu mã giao dịch MoMo (GatewayTransactionId) — không thể gọi cổng hoàn tiền.",
            };
        }

        var description = $"Hoan tien {request.RefundCode}";

        // Thứ tự trường đúng chuẩn MoMo — xếp theo bảng chữ cái, đừng sắp lại cho "đẹp".
        // Số định dạng culture bất biến, cùng lối MoMoGatewayService.
        var rawSignature = string.Join("&",
            $"accessKey={_options.AccessKey}",
            $"amount={request.Amount.ToString(CultureInfo.InvariantCulture)}",
            $"description={description}",
            $"orderId={request.RefundCode}",
            $"partnerCode={_options.PartnerCode}",
            $"requestId={request.RefundId}",
            $"transId={transId.ToString(CultureInfo.InvariantCulture)}");

        var body = new
        {
            partnerCode = _options.PartnerCode,
            orderId = request.RefundCode,
            requestId = request.RefundId.ToString(),
            amount = (long)request.Amount,
            transId,
            lang = "vi",
            description,
            signature = Sign(rawSignature),
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(MoMoMandatoryTimeout);

        using var response = await _http.PostAsJsonAsync(
            $"{_options.Endpoint.TrimEnd('/')}/v2/gateway/api/refund",
            body,
            JsonOptions,
            cts.Token);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<RefundResponseJson>(
            JsonOptions, cts.Token) ?? new RefundResponseJson();

        if (json.ResultCode == 0)
        {
            return new RefundGatewayCallOutcome
            {
                Result = RefundLogResult.Succeeded,
                Message = json.Message,
                // transId của RESPONSE là mã giao dịch hoàn phía MoMo — thứ đối chiếu sao kê ở bước
                // đối soát (dòng 43). Cổng có thể không gửi mã ở vài ca lạ → để null còn hơn bịa.
                GatewayTransactionId = json.TransId > 0
                    ? json.TransId.ToString(CultureInfo.InvariantCulture)
                    : null,
            };
        }

        return new RefundGatewayCallOutcome
        {
            Result = RefundLogResult.Failed,
            Message = $"MoMo từ chối hoàn tiền (resultCode {json.ResultCode}): {json.Message}",
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

    /// <summary>JSON camelCase + không phân biệt hoa thường — MoMo trả camelCase, ta đọc bằng PascalCase.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Hình dạng response của POST /v2/gateway/api/refund — chỉ lấy các trường dùng tới.</summary>
    private sealed class RefundResponseJson
    {
        public int ResultCode { get; set; }

        public string? Message { get; set; }

        /// <summary>Mã giao dịch hoàn phía MoMo — chỉ có nghĩa khi ResultCode = 0.</summary>
        public long TransId { get; set; }
    }
}
