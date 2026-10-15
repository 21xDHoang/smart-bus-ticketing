using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.TicketRequests;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Duyệt / từ chối yêu cầu đổi vé — US 5, task *"API duyệt / từ chối yêu cầu huỷ - đổi vé"*
/// (Phùng Duy Hoàng, dòng 5 Sprint 4). Hợp đồng đầy đủ: docs/api-contract.md, mục
/// "Duyệt / từ chối yêu cầu ĐỔI".
///
/// Cặp với <see cref="CancellationRequestReviewController"/>: cùng quyền, cùng body, cùng hình dạng
/// response, khác đúng tài nguyên. Hai file chứ không một file hai route vì mỗi bảng có một chủ
/// riêng ở nửa gửi yêu cầu (Hiếu dòng 3 / Kiên dòng 4) — nửa duyệt đi theo cùng cách chia đó.
///
/// 🔴 **Duyệt đổi vé là việc nặng nhất của US 5**: nó kiểm lại ghế mới ngay lúc duyệt (ghế không
/// được giữ lúc khách gửi), huỷ vé cũ, và **phát hành một vé MỚI** — không sửa vé cũ, vì vé cũ đã
/// in QR và đã nằm trong hoá đơn của <c>Payments</c>. Chi tiết + lý do: docs/api-contract.md và
/// <c>docs/32-csdl-huy-doi-ve.md</c> §3.1.
/// </summary>
[ApiController]
[Route("api/ticket-changes")]
[Authorize(Policy = RbacPolicies.ManagerOrAbove)]
public class TicketChangeReviewController : ControllerBase
{
    private readonly ITicketRequestReviewService _reviewService;

    public TicketChangeReviewController(ITicketRequestReviewService reviewService)
        => _reviewService = reviewService;

    /// <summary>
    /// Duyệt hoặc từ chối một yêu cầu đổi vé.
    /// Không có token → 401; không phải Admin/Manager → 403; <c>status</c> thiếu hoặc sai giá trị
    /// (kể cả <c>"Pending"</c>) → 400 kèm <c>errors.status</c>; <c>rejectReason</c> quá 2000 ký tự →
    /// 400; không có yêu cầu với <c>id</c> → 404; yêu cầu đã được người khác xử lý → 409; vé gốc
    /// không còn <c>Paid</c> → 409; chuyến xin đổi sang đã huỷ / đã chạy → 409; ghế mới đã có người
    /// đặt trong lúc chờ duyệt → 409.
    ///
    /// Duyệt thì trả về kèm <c>newTicketId</c> của vé vừa phát hành; từ chối thì <c>newTicketId</c>
    /// vẫn null và không có ghế nào bị kiểm.
    /// </summary>
    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Review(
        Guid id,
        [FromBody] UpdateTicketRequestStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        if (!TryGetCurrentUserId(out var reviewerUserId))
        {
            return MissingCurrentUser();
        }

        var result = await _reviewService.ReviewTicketChangeAsync(
            id, request, reviewerUserId, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>Người bấm duyệt lấy từ token — xem bản song sinh ở controller yêu cầu huỷ.</summary>
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

    /// <summary>Lỗi model binding — chép nguyên bản của <c>FeedbackAdminController</c> (xem lý do ở đó).</summary>
    private IActionResult ValidationError() => BadRequest(new
    {
        message = "Dữ liệu đầu vào không hợp lệ",
        errors = ModelState.Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray()),
    });
}
