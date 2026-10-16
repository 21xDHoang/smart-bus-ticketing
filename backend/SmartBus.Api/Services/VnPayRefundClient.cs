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
/// Client hoàn tiền VNPay (US 8 — task *"API gọi cổng thanh toán để hoàn tiền"*, dòng 29 — Trần Trung
/// Hiếu). Nói chuyện với <c>POST {RefundEndpoint}</c> (<c>vnp_Command = refund</c>).
///
/// Đây là API server-to-server đầu tiên của VNPay trong dự án — khác hẳn chiều thu tiền (dựng URL ký
/// sẵn cho khách bấm). Ba điểm dễ sai, ghim lại đây:
///
/// <list type="number">
/// <item><b>Chuỗi ký KHÔNG giống chiều thu tiền:</b> không xếp theo bảng chữ cái, không urlencode —
/// nối bằng '|' theo ĐÚNG thứ tự tài liệu cổng:
/// <c>vnp_RequestId|vnp_Version|vnp_Command|vnp_TmnCode|vnp_TransactionType|vnp_TxnRef|vnp_Amount|
/// vnp_TransactionNo|vnp_TransactionDate|vnp_CreateBy|vnp_CreateDate|vnp_IpAddr|vnp_OrderInfo</c>
/// rồi HMAC-SHA512, hex chữ HOA. Lẫn chuỗi ký của hai chiều là cổng trả code 97 (sai chữ ký).</item>
/// <item><b>vnp_Amount là số tiền GỐC × 100</b> (đơn vị nhỏ nhất) — ghi số tiền hoàn thì lệch 100 lần.
/// <c>vnp_TransactionType = 02</c> (hoàn TOÀN PHẦN): US 8 không có hoàn một phần.</item>
/// <item><b>vnp_TransactionDate là mốc thu tiền GỐC</b> (<c>Payment.PaidAt</c>) đổi sang GMT+7,
/// không phải giờ gọi hoàn — cổng dùng nó để định vị giao dịch cần hoàn.</item>
/// </list>
///
/// Kết luận theo câu trả lời của cổng: <c>vnp_ResponseCode 00</c> là "yêu cầu refund được thực hiện
/// thành công" (trừ khi <c>vnp_TransactionStatus 09</c> — cổng từ chối hoàn). Code 94 ("đã gửi yêu
/// cầu hoàn trước đó, đang xử lý") là ca có thật khi lượt trước bị timeout mà vẫn tới được cổng —
/// kết quả cuối chưa rõ nên trả <see cref="RefundLogResult.Unresolved"/> cho bước đối soát (dòng 43)
/// tra, không đoán. Mọi code khác là từ chối chắc chắn → <see cref="RefundLogResult.Failed"/>.
///
/// Response là cổng trả lời qua kênh HTTPS server-to-server đã xác thực TmnCode + chữ ký phía ta,
/// nên cố ý KHÔNG kiểm lại <c>vnp_SecureHash</c> của response — khác callback thanh toán (dữ liệu từ
/// trình duyệt của người lạ, bắt buộc phải kiểm). Lỗi vận chuyển NÉM LÊN người gọi, cùng lối
/// <see cref="MoMoRefundClient"/>.
/// </summary>
public class VnPayRefundClient : IRefundGatewayClient
{
    /// <summary>Phiên bản API của cổng — VNPay 2.1.0, chốt cứng theo tài liệu tích hợp.</summary>
    private const string ApiVersion = "2.1.0";

    /// <summary>Lệnh hoàn tiền — hằng số của VNPay, không phải tham số nghiệp vụ.</summary>
    private const string RefundCommand = "refund";

    /// <summary>02 = hoàn TOÀN PHẦN (03 là hoàn một phần — US 8 không có hoàn một phần).</summary>
    private const string FullRefundType = "02";

    /// <summary>Người khởi tạo lệnh hoàn — lượt gọi đến từ hệ thống, không có người bấm cụ thể.</summary>
    private const string CreateBy = "SmartBus";

    /// <summary>IP gửi cổng — cổng chỉ kiểm hình dạng, backend không biết IP thật của mình.</summary>
    private const string ServerIp = "127.0.0.1";

    /// <summary>Mã "yêu cầu refund được thực hiện thành công" của cổng.</summary>
    private const string SuccessResponseCode = "00";

    /// <summary>Mã "đã gửi yêu cầu hoàn tiền trước đó, đang xử lý" — kết quả cuối chưa rõ.</summary>
    private const string DuplicateResponseCode = "94";

    /// <summary>vnp_TransactionStatus 09 = cổng từ chối hoàn tiền (dù ResponseCode 00).</summary>
    private const string RejectedTransactionStatus = "09";

    /// <summary>Định dạng thời gian VNPay yêu cầu cho vnp_TransactionDate/vnp_CreateDate.</summary>
    private const string VnPayDateFormat = "yyyyMMddHHmmss";

    /// <summary>
    /// Giờ Việt Nam (GMT+7) — cổng ghi nhận các mốc theo giờ này. Lệch cố định vì Việt Nam không
    /// dùng DST từ 1975; tránh hẳn phụ thuộc mã vùng của hệ điều hành (cùng lối VnPayGatewayService).
    /// </summary>
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    /// <summary>Trần chờ phản hồi cổng — quá hạn thì ném lên để thành Unresolved, không đoán.</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private readonly VnPayRefundOptions _options;
    private readonly HttpClient _http;
    private readonly TimeProvider _clock;

    public VnPayRefundClient(
        IOptions<VnPayRefundOptions> options,
        HttpClient httpClient,
        TimeProvider? timeProvider = null)
    {
        _options = options.Value;
        _http = httpClient;
        _clock = timeProvider ?? TimeProvider.System;

        // Thiếu cấu hình đối tác thì mọi chữ ký đều sai và cổng chỉ trả code 97 — chết sớm với thông
        // báo nói đúng chỗ thiếu (cùng lối VnPayGatewayService).
        if (_options.MissingPiece() is { } missing)
        {
            throw new InvalidOperationException(
                $"Chưa cấu hình cổng thanh toán VNPay: thiếu {missing}. Đặt qua dotnet user-secrets "
                + "hoặc biến môi trường (xem appsettings.Development.json.example) — KHÔNG commit "
                + "giá trị thật lên repo public (luật 2).");
        }
    }

    /// <inheritdoc />
    public async Task<RefundGatewayCallOutcome> RefundAsync(
        RefundGatewayCallRequest request,
        Payment payment,
        CancellationToken cancellationToken = default)
    {
        // Lệnh hoàn phải định vị được giao dịch gốc: thiếu TransactionNo hoặc mốc thu tiền thì không
        // dựng được request — câu trả lời chắc chắn (thử lại cũng không đổi) nên là Failed.
        if (string.IsNullOrWhiteSpace(payment.GatewayTransactionId))
        {
            return new RefundGatewayCallOutcome
            {
                Result = RefundLogResult.Failed,
                Message = "Giao dịch gốc thiếu mã giao dịch VNPay (GatewayTransactionId) — không thể gọi cổng hoàn tiền.",
            };
        }

        if (payment.PaidAt is not { } paidAt)
        {
            return new RefundGatewayCallOutcome
            {
                Result = RefundLogResult.Failed,
                Message = "Giao dịch gốc thiếu mốc thu tiền (PaidAt) — không dựng được vnp_TransactionDate.",
            };
        }

        // requestId sinh MỚI mỗi lượt gọi: tài liệu cổng bắt không được lặp lại trong ngày, còn chống
        // hoàn hai lần nằm ở chính cổng (code 94 khi cùng TxnRef đã có lệnh hoàn trước đó).
        var requestId = Guid.NewGuid().ToString("N");

        // Tham số gửi cổng — đúng bộ của tài liệu VNPay 2.1.0, chuỗi ký nối '|' THEO ĐÚNG THỨ TỰ
        // này (xem doc-comment đầu file): đổi thứ tự là cổng trả 97.
        var signingData = string.Join("|",
            requestId,
            ApiVersion,
            RefundCommand,
            _options.TmnCode,
            FullRefundType,
            payment.PaymentCode,
            // Số tiền GỐC × 100 (đơn vị nhỏ nhất của VNPay) — RefundGatewayCaller đã chặn phần lẻ.
            (payment.Amount * 100).ToString("0", CultureInfo.InvariantCulture),
            payment.GatewayTransactionId,
            FormatVnPayTime(paidAt),
            CreateBy,
            FormatVnPayTime(_clock.GetUtcNow()),
            ServerIp,
            $"Hoan tien {request.RefundCode}");

        var body = new
        {
            vnp_RequestId = requestId,
            vnp_Version = ApiVersion,
            vnp_Command = RefundCommand,
            vnp_TmnCode = _options.TmnCode,
            vnp_TransactionType = FullRefundType,
            vnp_TxnRef = payment.PaymentCode,
            vnp_Amount = (payment.Amount * 100).ToString("0", CultureInfo.InvariantCulture),
            vnp_OrderInfo = $"Hoan tien {request.RefundCode}",
            vnp_TransactionNo = payment.GatewayTransactionId,
            vnp_TransactionDate = FormatVnPayTime(paidAt),
            vnp_CreateBy = CreateBy,
            vnp_CreateDate = FormatVnPayTime(_clock.GetUtcNow()),
            vnp_IpAddr = ServerIp,
            vnp_SecureHash = Sign(signingData),
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(RequestTimeout);

        using var response = await _http.PostAsJsonAsync(
            _options.RefundEndpoint,
            body,
            JsonOptions,
            cts.Token);
        response.EnsureSuccessStatusCode();

        using var json = await ReadResponseJsonAsync(response, cts.Token);

        // Cổng trả HTTP 200 nhưng body không đọc được (hoặc thiếu code): coi như không có câu trả
        // lời — KHÔNG đoán, để bước đối soát tra sao kê.
        if (json is null)
        {
            return new RefundGatewayCallOutcome
            {
                Result = RefundLogResult.Unresolved,
                Message = "VNPay trả về body không đọc được (thiếu vnp_ResponseCode) — kết quả chưa rõ.",
            };
        }

        var root = json.RootElement;
        var responseCode = GetStringOrEmpty(root, "vnp_ResponseCode");
        var message = GetStringOrEmpty(root, "vnp_Message");

        // Code 94: một lệnh hoàn cho giao dịch này ĐÃ TỒN TẠI trên cổng (lượt trước timeout nhưng vẫn
        // tới nơi) — kết quả cuối không biết được từ câu này, chỉ bước đối soát (dòng 43) tra được.
        if (responseCode == DuplicateResponseCode)
        {
            return new RefundGatewayCallOutcome
            {
                Result = RefundLogResult.Unresolved,
                Message = $"VNPay báo lệnh hoàn tiền đã tồn tại và đang xử lý (code {responseCode}): {message}",
            };
        }

        if (responseCode == SuccessResponseCode)
        {
            // TransactionStatus 09 = cổng TỪ CHỐI hoàn dù ResponseCode 00 — câu trả lời rõ ràng.
            var transactionStatus = GetStringOrEmpty(root, "vnp_TransactionStatus");
            if (transactionStatus == RejectedTransactionStatus)
            {
                return new RefundGatewayCallOutcome
                {
                    Result = RefundLogResult.Failed,
                    Message = $"VNPay từ chối hoàn tiền (TransactionStatus 09): {message}",
                };
            }

            // vnp_TransactionNo của RESPONSE là mã giao dịch hoàn phía VNPay — thứ đối chiếu sao kê.
            return new RefundGatewayCallOutcome
            {
                Result = RefundLogResult.Succeeded,
                Message = message,
                GatewayTransactionId = GetStringOrEmpty(root, "vnp_TransactionNo"),
            };
        }

        return new RefundGatewayCallOutcome
        {
            Result = RefundLogResult.Failed,
            Message = $"VNPay từ chối hoàn tiền (code {responseCode}): {message}",
        };
    }

    /// <summary>HMAC-SHA512 trên chuỗi ký, hex chữ HOA — quy ước chữ ký của VNPay.</summary>
    private string Sign(string signingData)
    {
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(_options.HashSecret));

        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(signingData)));
    }

    /// <summary>Đổi một mốc thời gian về giờ GMT+7 đúng định dạng cổng yêu cầu.</summary>
    private static string FormatVnPayTime(DateTimeOffset moment)
    {
        return moment.ToOffset(VietnamOffset).ToString(VnPayDateFormat, CultureInfo.InvariantCulture);
    }

    private static string GetStringOrEmpty(JsonElement json, string property) =>
        json.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    /// <summary>
    /// Đọc body response thành JsonDocument. Trả null khi body không phải JSON hợp lệ hoặc thiếu
    /// vnp_ResponseCode — dữ liệu cổng trả về, không phải lỗi lập trình nên không ném. Người gọi
    /// dispose document trả về.
    /// </summary>
    private static async Task<JsonDocument?> ReadResponseJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        try
        {
            var document = JsonDocument.Parse(content);

            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("vnp_ResponseCode", out _))
            {
                document.Dispose();
                return null;
            }

            return document;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>JSON camelCase — request gửi dạng object ẩn danh, response đọc bằng GetProperty.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
