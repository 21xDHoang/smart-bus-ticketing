using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.TicketRequests;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Gửi yêu cầu đổi chuyến — <c>POST /api/ticket-changes</c> (US 5, task *"API gửi yêu cầu đổi
/// chuyến + tính chênh lệch giá"* — Nguyễn Duy Kiên, dòng 4 Sprint 4). Hợp đồng đầy đủ:
/// docs/api-contract.md, mục "Gửi yêu cầu đổi chuyến".
///
/// Cặp với <see cref="TicketChangeReviewController"/>: cùng bề mặt <c>api/ticket-changes</c>, khác
/// động từ. POST ở gốc không tranh chấp với route <c>{id:guid}</c> của controller kia — cùng lối
/// FeedbackSubmissionController đứng chung bề mặt với FeedbackLookupController.
///
/// <c>[Authorize]</c> trần (không policy): hành khách tự gửi yêu cầu cho vé của mình. RBAC của dự
/// án chỉ có AdminOnly/ManagerOrAbove, không có policy Passenger; quyền sở hữu vé được kiểm trong
/// truy vấn của service (vé người khác → 404, không phải 403).
///
/// 🔴 Response là hình dạng dòng CHUNG của US 5, kèm <c>priceDifference</c> CÓ DẤU — dương = khách
/// trả thêm, âm = nhà xe hoàn lại. Chênh lệch KHÔNG có cột trong CSDL: nó là
/// <c>NewPrice - Ticket.Price</c>, xem docs/32-csdl-huy-doi-ve.md §6.
/// </summary>
[ApiController]
[Route("api/ticket-changes")]
[Authorize]
public class TicketChangeSubmissionController : ControllerBase
{
    private readonly ITicketChangeSubmissionService _submissionService;

    public TicketChangeSubmissionController(ITicketChangeSubmissionService submissionService)
        => _submissionService = submissionService;

    /// <summary>
    /// Gửi một yêu cầu đổi chuyến; trả 201 kèm dòng yêu cầu vừa ghi (status luôn Pending).
    ///
    /// Không có token → 401; thiếu field bắt buộc / <c>reason</c> quá 2000 ký tự → 400; vé không
    /// tồn tại hoặc không thuộc người gọi, chuyến/ghế mới không tồn tại (kể cả ghế của xe khác) →
    /// 404; vé không còn Paid, vé đang có yêu cầu chờ duyệt, chuyến mới đã huỷ/đã khởi hành, ghế
    /// mới đã có người đặt, tuyến chưa có giá → 409.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(TicketRequestResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Submit(
        [FromBody] CreateTicketChangeRequest request,
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

        var result = await _submissionService.SubmitAsync(request, userId, cancellationToken);

        // 201 bằng StatusCode thay vì CreatedAtAction: không có endpoint GET một yêu cầu đơn lẻ để
        // trỏ Location tới (màn theo dõi đọc danh sách của chính khách). Cùng lối
        // FeedbackSubmissionController và AuthController.Register.
        return result.Success
            ? StatusCode(StatusCodes.Status201Created, result.Data)
            : Failure(result);
    }

    /// <summary>
    /// Người gửi lấy từ claim NameIdentifier — KHÔNG nhận từ body. Nhận từ body là mở đường cho
    /// người này gửi yêu cầu dưới tên người khác.
    /// </summary>
    private bool TryGetCurrentUserId(out Guid userId)
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private IActionResult MissingCurrentUser()
        => Unauthorized(new { message = "Không xác định được người dùng đang đăng nhập." });

    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };

    /// <summary>Lỗi model binding — cùng lối <see cref="TicketChangeReviewController"/>.</summary>
    private IActionResult ValidationError() => BadRequest(new
    {
        message = "Dữ liệu đầu vào không hợp lệ",
        errors = ModelState.Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray()),
    });
}
