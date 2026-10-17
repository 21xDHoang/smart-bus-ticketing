using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SmartBus.Api.Dtos.Refunds;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Client hoàn tiền ZaloPay (US 8 — task *"API gọi cổng thanh toán để hoàn tiền"*, dòng 29 — Trần
/// Trung Hiếu). Nói chuyện với <c>POST {Endpoint}/v2/refund</c>, ký MAC bằng Key1 — cùng chiều khoá
/// như <see cref="ZaloPayGatewayService.CreateOrderAsync"/>.
///
/// Ba điểm dễ sai, ghim lại đây:
///
/// <list type="number">
/// <item><b>m_refund_id KHÔNG được đổi giữa các lượt thử lại</b> — nó là khoá chống hoàn trùng của
/// cổng: ZaloPay cho phép hoàn NHIỀU lần trên một giao dịch (tới tổng số tiền), nên mỗi lượt thử mà
/// sinh mã mới là một LỆNH HOÀN MỚI — hoàn hai lần. Mã dựng từ <see cref="Refund.Id"/> nên ổn định
/// vĩnh viễn cho một khoản; lượt gửi lại chỉ nhận <c>DUPLICATE_REFUND</c>, không bao giờ thành lệnh
/// thứ hai. Định dạng theo tài liệu cổng: <c>{yymmdd}_{appId}_{32 ký tự}</c>.</item>
/// <item><b>zp_trans_id là mã giao dịch ZaloPay của lượt THU</b> (<c>Payment.GatewayTransactionId</c>).</item>
/// <item><b>return_code 3 = "đang xử lý"</b> — trạng thái thứ ba mà MoMo/VNPay không có: cổng ĐÃ nhận
/// lệnh nhưng chưa chốt, nên trả <see cref="RefundLogResult.Unresolved"/> cho bước đối soát (dòng 43)
/// tra tiếp bằng API query_refund, không đoán. Cùng phép xử lý cho <c>DUPLICATE_REFUND</c> (sub -23):
/// lệnh hoàn đã tồn tại trên cổng nghĩa là lượt trước đã tới nơi — kết quả cuối chưa biết.</item>
/// </list>
///
/// Lỗi vận chuyển NÉM LÊN người gọi, cùng lối <see cref="MoMoRefundClient"/>.
/// </summary>
public class ZaloPayRefundClient : IRefundGatewayClient
{
    /// <summary>Mã thành công của ZaloPay — KHÁC MoMo (0). So với 0 là hỏng toàn bộ nhánh thành công.</summary>
    private const int SuccessReturnCode = 1;

    /// <summary>Mã "lệnh hoàn đang xử lý" — chưa có kết quả cuối, không được đoán.</summary>
    private const int ProcessingReturnCode = 3;

    /// <summary>sub_return_code -23 = m_refund_id đã tồn tại — lệnh hoàn trùng, kết quả cuối chưa biết.</summary>
    private const long DuplicateSubReturnCode = -23;

    /// <summary>Định dạng tiền tố ngày của m_refund_id theo tài liệu ZaloPay.</summary>
    private const string RefundIdDateFormat = "yyMMdd";

    /// <summary>Trần chờ phản hồi cổng — quá hạn thì ném lên để thành Unresolved, không đoán.</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Giờ Việt Nam (GMT+7) — tiền tố ngày của m_refund_id tính theo giờ này.</summary>
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    private readonly ZaloPayOptions _options;
    private readonly HttpClient _http;
    private readonly TimeProvider _clock;

    /// <summary>
    /// app_id dạng SỐ để đưa vào body — tài liệu ZaloPay khai trường này là <c>int32</c>, gửi chuỗi
    /// là cổng từ chối. Chuỗi MAC thì vẫn dùng nguyên văn cấu hình; phép kiểm dưới đây bảo đảm hai
    /// dạng ấy là cùng một giá trị (cùng lối ZaloPayGatewayService).
    /// </summary>
    private readonly long _appId;

    public ZaloPayRefundClient(
        IOptions<ZaloPayOptions> options,
        HttpClient httpClient,
        TimeProvider? timeProvider = null)
    {
        _options = options.Value;
        _http = httpClient;
        _clock = timeProvider ?? TimeProvider.System;

        if (_options.MissingPiece() is { } missing)
        {
            throw new InvalidOperationException(
                $"Chưa cấu hình cổng thanh toán ZaloPay: thiếu {missing}. Đặt qua dotnet user-secrets "
                + "hoặc biến môi trường (xem appsettings.Development.json.example) — KHÔNG commit "
                + "giá trị thật lên repo public (luật 2).");
        }

        if (!long.TryParse(_options.AppId, NumberStyles.None, CultureInfo.InvariantCulture, out _appId)
            || _appId.ToString(CultureInfo.InvariantCulture) != _options.AppId)
        {
            throw new InvalidOperationException(
                $"Cấu hình ZaloPay:AppId phải là số nguyên (tài liệu ZaloPay khai app_id là int32, ví "
                + $"dụ 2553) — nhận được '{_options.AppId}'.");
        }
    }

    /// <inheritdoc />
    public async Task<RefundGatewayCallOutcome> RefundAsync(
        RefundGatewayCallRequest request,
        Payment payment,
        CancellationToken cancellationToken = default)
    {
        // Lệnh hoàn phải trỏ ĐÚNG giao dịch đã thu: thiếu mã giao dịch ZaloPay thì không có gì để
        // hoàn — câu trả lời chắc chắn (thử lại cũng không đổi) nên là Failed.
        if (!long.TryParse(
                payment.GatewayTransactionId,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var zpTransId))
        {
            return new RefundGatewayCallOutcome
            {
                Result = RefundLogResult.Failed,
                Message = "Giao dịch gốc thiếu mã giao dịch ZaloPay (GatewayTransactionId) — không thể gọi cổng hoàn tiền.",
            };
        }

        var now = _clock.GetUtcNow();
        var timestamp = now.ToUnixTimeMilliseconds();
        var amount = (long)request.Amount;
        var description = $"Hoan tien {request.RefundCode}";

        // m_refund_id ỔN ĐỊNH cho một khoản hoàn (xem doc-comment đầu file): {yymmdd}_{appId}_{32
        // ký tự của RefundId} = 44 ký tự, dưới trần 45 của cổng.
        var refundId = $"{now.ToOffset(VietnamOffset).ToString(RefundIdDateFormat, CultureInfo.InvariantCulture)}"
            + $"_{_options.AppId}_{request.RefundId.ToString("N")}";

        // Chuỗi MAC theo đúng tài liệu: app_id|zp_trans_id|amount|description|timestamp — KHÔNG có
        // m_refund_id. Số định dạng culture bất biến, cùng lối ZaloPayGatewayService.
        var macInput = string.Join("|",
            _options.AppId,
            zpTransId.ToString(CultureInfo.InvariantCulture),
            amount.ToString(CultureInfo.InvariantCulture),
            description,
            timestamp.ToString(CultureInfo.InvariantCulture));

        var body = new Dictionary<string, object?>
        {
            // Số, không phải chuỗi: tài liệu ZaloPay khai app_id là int32.
            ["app_id"] = _appId,
            ["m_refund_id"] = refundId,
            ["zp_trans_id"] = zpTransId,
            ["amount"] = amount,
            ["timestamp"] = timestamp,
            ["description"] = description,
            ["mac"] = SignWithKey1(macInput),
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(RequestTimeout);

        using var response = await _http.PostAsJsonAsync(
            $"{_options.Endpoint.TrimEnd('/')}/v2/refund",
            body,
            JsonOptions,
            cts.Token);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<RefundResponseJson>(
            JsonOptions, cts.Token) ?? new RefundResponseJson();

        return json.ReturnCode switch
        {
            SuccessReturnCode => new RefundGatewayCallOutcome
            {
                Result = RefundLogResult.Succeeded,
                Message = json.ReturnMessage,
                // refund_id là mã giao dịch hoàn phía ZaloPay — thứ đối chiếu sao kê ở bước đối soát.
                GatewayTransactionId = json.RefundId > 0
                    ? json.RefundId.ToString(CultureInfo.InvariantCulture)
                    : null,
            },

            // Đang xử lý: cổng ĐÃ nhận lệnh, kết quả cuối chỉ biết qua query_refund — để bước đối
            // soát (dòng 43) tra, không đoán và không xếp lịch gọi lại (gọi lại là hoàn hai lần).
            ProcessingReturnCode => new RefundGatewayCallOutcome
            {
                Result = RefundLogResult.Unresolved,
                Message = $"ZaloPay đang xử lý lệnh hoàn: {json.ReturnMessage}",
            },

            2 when json.SubReturnCode == DuplicateSubReturnCode => new RefundGatewayCallOutcome
            {
                // Lệnh hoàn với m_refund_id này ĐÃ TỒN TẠI trên cổng — lượt trước đã tới nơi, kết quả
                // cuối chưa biết: cũng thuộc bước đối soát, không phải thất bại để xếp lịch thử lại.
                Result = RefundLogResult.Unresolved,
                Message = $"ZaloPay báo lệnh hoàn đã tồn tại trên cổng: {json.ReturnMessage}",
            },

            2 => new RefundGatewayCallOutcome
            {
                Result = RefundLogResult.Failed,
                Message = $"ZaloPay từ chối hoàn tiền: {json.ReturnMessage} ({json.SubReturnMessage})",
            },

            // Mã lạ (kể cả 0 — ví dụ mẫu của tài liệu cổng) là câu trả lời không hiểu được: không đoán.
            _ => new RefundGatewayCallOutcome
            {
                Result = RefundLogResult.Unresolved,
                Message = $"ZaloPay trả return_code không rõ ({json.ReturnCode}): {json.ReturnMessage}",
            },
        };
    }

    /// <summary>HMAC-SHA256 bằng Key1 (chiều ta gửi đi), hex chữ thường.</summary>
    private string SignWithKey1(string rawSignature)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.Key1));

        return Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(rawSignature)));
    }

    /// <summary>JSON camelCase + không phân biệt hoa thường, dùng cho cả chiều gửi lẫn chiều đọc.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Hình dạng response của POST /v2/refund — ZaloPay trả snake_case (khác MoMo camelCase) nên phải
    /// ghim tên trường bằng <see cref="JsonPropertyNameAttribute"/> (cùng lối ZaloPayGatewayService).
    /// </summary>
    private sealed class RefundResponseJson
    {
        [JsonPropertyName("return_code")]
        public int ReturnCode { get; set; }

        [JsonPropertyName("return_message")]
        public string? ReturnMessage { get; set; }

        [JsonPropertyName("sub_return_code")]
        public long SubReturnCode { get; set; }

        [JsonPropertyName("sub_return_message")]
        public string? SubReturnMessage { get; set; }

        [JsonPropertyName("refund_id")]
        public long RefundId { get; set; }
    }
}
