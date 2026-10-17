using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.Payments;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Bề mặt thanh toán — POST /api/payments (tạo giao dịch + gọi cổng), GET /api/payments/{paymentCode}
/// (tra trạng thái), POST /api/payments/momo/callback và POST /api/payments/zalopay/callback (IPN
/// của cổng). US 6 "Cổng thanh toán", tasks *"Tạo giao dịch + gọi cổng"*, *"API kiểm tra trạng
/// thái giao dịch"* và *"đối soát tự động"* — Trần Trung Hiếu. Hợp đồng đầy đủ ở mục
/// "Thanh toán — /payments" của docs/api-contract.md.
///
/// Hai endpoint của khách yêu cầu đăng nhập và chỉ chạm giao dịch của chính người gọi (điều kiện
/// UserId trong truy vấn — giao dịch của người khác trả 404, cùng lối /seat-holds/{sessionCode}).
/// Hai endpoint callback thì CÔNG KHAI: người gọi là máy chủ cổng, tin request chỉ dựa trên chữ ký.
///
/// Ghi nhật ký: POST /payments và hai callback đều đi qua AuditLogMiddleware (POST → Create; tên
/// bảng suy từ đoạn tĩnh là "Payments"/"Callback" — giới hạn đã biết của suy tên, xem BangPhuSong
/// trong AuditLogCoverageTests). ⚠️ Callback VNPay (GET /api/payments/vnpay/callback) là task của
/// Nguyễn Duy Kiên — KHÔNG có ở đây; BankCard gọi về chính endpoint đó (cùng cổng VNPay).
/// </summary>
[ApiController]
[Route("api/payments")]
[Authorize]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly IPaymentGatewayResolver _resolver;
    private readonly IPaymentSettlementService _settlement;
    private readonly IPaymentTicketIssuer _issuer;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(
        IPaymentService paymentService,
        IPaymentGatewayResolver resolver,
        IPaymentSettlementService settlement,
        IPaymentTicketIssuer issuer,
        ILogger<PaymentsController> logger)
    {
        _paymentService = paymentService;
        _resolver = resolver;
        _settlement = settlement;
        _issuer = issuer;
        _logger = logger;
    }

    /// <summary>
    /// Tạo giao dịch thanh toán rồi gọi cổng lấy URL chuyển khách sang trả tiền.
    ///
    /// Mã phương thức chưa mở → 400 · chuyến không tồn tại → 404 · dữ liệu lệch (total, ghế) → 400
    /// theo trường · voucher không dùng được → 409 theo trường voucherCode · cổng từ chối / thiếu
    /// cấu hình / mạng chết → 502 kèm lời cổng.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(CreatePaymentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Create(
        [FromBody] CreatePaymentRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        if (!TryGetCurrentUserId(out var userId))
        {
            return MissingCurrentUser();
        }

        // IPN URL suy từ chính request — backend chạy ở đâu thì cổng gọi về đúng nơi đó, không cần
        // cấu hình (khác ReturnUrl: đó là origin của FE, nằm ở PaymentOptions).
        var ipnPath = request.MethodCode?.ToLowerInvariant() switch
        {
            "momo" => "momo",
            "zalopay" => "zalopay",
            "vnpay" or "bankcard" => "vnpay",   // BankCard là kênh thẻ của VNPay, gọi về endpoint VNPay
            _ => string.Empty,
        };

        var ipnUrl = ipnPath.Length == 0
            ? string.Empty
            : $"{Request.Scheme}://{Request.Host}{Request.PathBase}/api/payments/{ipnPath}/callback";

        try
        {
            var result = await _paymentService.CreateAsync(
                userId,
                request,
                ipnUrl,
                Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1",
                cancellationToken);

            return result.Success ? Ok(result.Data) : Failure(result);
        }
        catch (InvalidOperationException ex)
        {
            // Cổng từ chối lúc tạo, hoặc thiếu cấu hình khoá (adapter ném khi dựng) — lỗi phía cổng
            // / triển khai, khác hẳn "chưa mở" (400) hay lỗi dữ liệu. Bản ghi Pending nằm lại cho
            // job đối soát hỏi cổng và chốt Failed.
            return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = $"Không kết nối được cổng thanh toán: {ex.Message}" });
        }
    }

    /// <summary>
    /// Tra trạng thái giao dịch — cửa sổ đọc của màn chờ (poll mỗi 3 giây). Không tự hỏi cổng.
    /// Giao dịch của người khác và giao dịch không tồn tại cùng một câu 404.
    /// </summary>
    [HttpGet("{paymentCode}")]
    [ProducesResponseType(typeof(PaymentStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatus(string paymentCode, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return MissingCurrentUser();
        }

        var result = await _paymentService.GetStatusAsync(userId, paymentCode, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>
    /// IPN của MoMo — CÔNG KHAI: người gọi là máy chủ MoMo, tin request chỉ dựa trên chữ ký.
    /// Chữ ký sai → 400 · lệch mã đơn/cổng/số tiền → 404 · còn lại → 204 trong 15 giây (cổng gửi
    /// lại nếu chậm, mà bản gửi lại là ca idempotency của cửa chốt).
    /// </summary>
    [AllowAnonymous]
    [HttpPost("momo/callback")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MoMoCallback(CancellationToken cancellationToken)
    {
        if (!_resolver.TryGet(PaymentProviderCodes.MoMo, out var gateway))
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Cổng thanh toán MoMo chưa được cấu hình." });
        }

        var body = await ReadRawBodyAsync(cancellationToken);
        var callback = gateway.VerifyCallback(new PaymentCallbackInput { Body = body });

        return await SettleCallbackAsync(
            PaymentProviderCodes.MoMo,
            callback,
            success: NoContent(),
            cancellationToken);
    }

    /// <summary>
    /// IPN của ZaloPay — CÔNG KHAI như MoMo. Khác ở câu trả lời cổng bắt buộc: JSON
    /// <c>{ return_code: 1, return_message: "Success" }</c> (không phải 204), và chữ ký hợp lệ
    /// NGHĨA LÀ đã thu tiền — callback ZaloPay không mang mã kết quả.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("zalopay/callback")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ZaloPayCallback(CancellationToken cancellationToken)
    {
        if (!_resolver.TryGet(PaymentProviderCodes.ZaloPay, out var gateway))
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Cổng thanh toán ZaloPay chưa được cấu hình." });
        }

        var body = await ReadRawBodyAsync(cancellationToken);
        var callback = gateway.VerifyCallback(new PaymentCallbackInput { Body = body });

        return await SettleCallbackAsync(
            PaymentProviderCodes.ZaloPay,
            callback,
            success: Ok(new { return_code = 1, return_message = "Success" }),
            cancellationToken);
    }

    /// <summary>
    /// Chốt kết quả callback qua ĐÚNG MỘT cửa (PaymentSettlementService) rồi phát hành vé khi cờ
    /// <c>ShouldIssueTickets</c> gợi ý — cờ giữ true ở bản gửi lại khi lần phát hành trước đứt giữa
    /// đường, và <see cref="IPaymentTicketIssuer"/> idempotent theo paymentCode nên gọi lại vô hại.
    /// Phát hành hỏng thì ghi log rồi VẪN trả lời cổng: tiền đã về, không có lý do gì để cổng gửi
    /// lại mãi (hợp đồng đã chốt).
    /// </summary>
    private async Task<IActionResult> SettleCallbackAsync(
        string providerCode,
        PaymentCallbackResult callback,
        IActionResult success,
        CancellationToken cancellationToken)
    {
        var settle = await _settlement.SettleAsync(providerCode, callback, cancellationToken);

        if (!settle.Success)
        {
            return settle.ErrorKind == ServiceErrorKind.NotFound
                ? NotFound(new { message = settle.Error })
                : BadRequest(new { message = settle.Error, errors = settle.Errors });
        }

        if (settle.Data!.ShouldIssueTickets)
        {
            try
            {
                await _issuer.IssueAsync(settle.Data.PaymentCode, cancellationToken);
            }
            catch (Exception ex)
            {
                // Hỏng ở đây KHÔNG được làm hỏng callback: ghi log rồi vẫn trả câu cổng chờ — bản gửi
                // lại sẽ cứu vé nhờ idempotency của issuer.
                _logger.LogError(ex,
                    "Callback {ProviderCode} giao dịch {PaymentCode}: phát hành vé lỗi — bỏ qua, chờ bản gửi lại.",
                    providerCode, settle.Data.PaymentCode);
            }
        }

        return success;
    }

    private async Task<string> ReadRawBodyAsync(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    /// <summary>
    /// Người gọi lấy từ claim NameIdentifier — cùng hàm của TripLocationController,
    /// MyTicketsController, SeatHoldLookupController, MonthlyPassLookupController, AdminUserController
    /// và DriversController. Cố ý chép lại thay vì tách lớp dùng chung: tách ra thì phải sửa
    /// controller của người khác, mà luật nhóm không cho. Các bản giống nhau là cái giá rẻ hơn.
    /// </summary>
    private bool TryGetCurrentUserId(out Guid userId)
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private IActionResult MissingCurrentUser()
        => Unauthorized(new { message = "Không xác định được người dùng đang đăng nhập." });

    // Cùng một hàm ở TripLocationController, AuthController, AdminUserController, FaresController,
    // RoutesController, StopsController, RouteStopsController, RouteTripsController, TripsController
    // và TripAssignmentController. Cố ý chép lại thay vì tách thành lớp dùng chung: tách ra thì
    // phải sửa controller của người khác, mà luật nhóm không cho. Các bản giống nhau là cái giá
    // rẻ hơn.
    private IActionResult ValidationError() => BadRequest(new
    {
        message = "Dữ liệu đầu vào không hợp lệ",
        errors = ModelState.Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray()),
    });

    // Cùng một hàm ở TripLocationController, MyTicketsController, SeatHoldLookupController,
    // TripSearchController, RoutesController, FaresController và VoucherValidationController —
    // chép lại cố ý, cùng lý do trên.
    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };
}
