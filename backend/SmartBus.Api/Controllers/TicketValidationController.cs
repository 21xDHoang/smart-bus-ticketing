using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.TicketValidations;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Soát vé — POST /api/tickets/validate (US 15 "Soát vé QR", Sprint 4, task *"API soát vé: nhận mã
/// QR, xác thực chữ ký, trả kết quả hợp lệ/không"* — Trần Trung Hiếu, dòng 15, cộng dòng 19 *"Phân
/// quyền: chỉ tài xế/phụ xe được soát vé"*). Hợp đồng đầy đủ ở mục "Soát vé QR" của
/// docs/api-contract.md.
///
/// Phân quyền: <c>[Authorize(Roles = RoleCodes.Driver + "," + RoleCodes.Manager)]</c> — dòng 19
/// chốt chỉ Tài xế/Quản lý được soát (docs/34 §6). Không thêm policy "DriverOrManager" vào
/// RbacMiddleware (file của Dăm — luật E1 cấm sửa file người khác); dùng thẳng thuộc tính roles như
/// TripLocationController đã làm cho vai trò đơn.
///
/// Đứng ở controller riêng trên bề mặt api/tickets: <c>validate</c> là đoạn đường dẫn cố định,
/// <c>{id:guid}</c> là tham số nên không tranh chấp với MyTicketsController — mỗi task một
/// controller, cùng lối api/seat-holds và api/vouchers.
///
/// ⚠️ Nhật ký kiểm toán của endpoint này: <c>AuditLogMiddleware</c> tự ghi Create nhưng suy Target
/// là "Validate" (route không có <c>{id}</c>, response không có trường <c>id</c> ở nóc) — giới hạn
/// đã ghi nhận ở <c>AuditLogMiddleware.ResolveTableName</c>, bảng phủ sóng
/// <c>AuditLogCoverageTests</c> ghim sự thật này. Nhật ký thật của lượt soát nằm ở bảng
/// <c>TicketValidations</c>, do <see cref="ITicketValidationRecorder"/> ghi.
/// </summary>
[ApiController]
[Route("api/tickets")]
[Authorize(Roles = RoleCodes.Driver + "," + RoleCodes.Manager)]
public class TicketValidationController : ControllerBase
{
    private readonly ITicketValidationService _validationService;

    public TicketValidationController(ITicketValidationService validationService)
        => _validationService = validationService;

    /// <summary>
    /// Soát một lượt quét mã QR.
    ///
    /// Không có token → 401; vai trò không phải Tài xế/Quản lý → 403; body sai khuôn (thiếu
    /// <c>code</c>) → 400 kèm <c>errors.&lt;field&gt;</c>; <c>tripId</c> trỏ chuyến không tồn tại →
    /// 404; recorder từ chối lượt ghi (mã rỗng/quá dài, <c>scannedAt</c> ngoài cửa sổ) → 400;
    /// còn lại — kể cả vé không hợp lệ — → 200 kèm <c>valid</c>/<c>reason</c> (docs/29 §3).
    /// </summary>
    [HttpPost("validate")]
    [ProducesResponseType(typeof(TicketValidationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Validate(
        [FromBody] ValidateTicketRequest request,
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

        var result = await _validationService.ValidateAsync(request, userId, cancellationToken);

        // Mọi kết quả soát đều là 200 với ServiceResult.Ok — "vé không hợp lệ" nằm trong Data, không
        // phải Error (cùng lối VoucherValidationController).
        return result.Success
            ? Ok(result.Data)
            : Failure(result);
    }

    // Cùng một hàm ở SeatHoldCreateController, SeatHoldLookupController, FeedbackSubmissionController,
    // RouteTripsController, RoutesController, FaresController, TripSearchController, MyTicketsController
    // và VoucherValidationController — chép lại cố ý (luật không sửa file của người khác).
    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };

    /// <summary>
    /// Body sai khuôn — cùng lối VoucherValidationController: Program.cs đã tắt filter validate tự
    /// động của ASP.NET Core, controller phải tự kiểm và tự dựng theo khuôn { message, errors },
    /// khoá lỗi đổi sang camelCase để khớp tên trường trong JSON.
    /// </summary>
    private IActionResult ValidationError() => BadRequest(new
    {
        message = "Dữ liệu đầu vào không hợp lệ",
        errors = ModelState.Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray()),
    });

    /// <summary>
    /// Người soát lấy từ claim NameIdentifier — cùng hàm của MyTicketsController. Người dùng đã
    /// đăng nhập thì claim phải có; thiếu là token hỏng, trả 401.
    /// </summary>
    private bool TryGetCurrentUserId(out Guid userId)
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private IActionResult MissingCurrentUser()
        => Unauthorized(new { message = "Không xác định được người dùng đang đăng nhập." });
}
