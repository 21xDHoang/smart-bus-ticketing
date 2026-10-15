using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.TicketRequests;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Duyệt / từ chối yêu cầu huỷ vé — US 5, task *"API duyệt / từ chối yêu cầu huỷ - đổi vé"*
/// (Phùng Duy Hoàng, dòng 5 Sprint 4). Hợp đồng đầy đủ: docs/api-contract.md, mục
/// "Duyệt / từ chối yêu cầu HUỶ".
///
/// <b>Vì sao tên lớp có thêm chữ Review:</b> route <c>/api/cancellation-requests</c> còn nửa còn lại
/// là <c>POST</c> của Hiếu (dòng 3) — hai lớp khác nhau ĐƯỢC phép cùng một route miễn khác
/// action/verb, nhưng tên lớp thì phải duy nhất trong assembly. Đặt tên này để Hiếu giữ được
/// <c>CancellationRequestsController</c> cho nửa của mình mà không phải đi đổi tên vì file của
/// người khác. Cặp với <see cref="TicketChangeReviewController"/>.
///
/// <b>Vì sao không nằm dưới /admin/</b> như <c>FeedbackAdminController</c>: đây là cùng một tài
/// nguyên mà khách vừa tạo ở <c>POST /cancellation-requests</c> — quản lý sửa trạng thái của chính
/// tài nguyên đó. Quyền gác bằng <see cref="RbacPolicies.ManagerOrAbove"/>, không gác bằng tiền tố
/// đường dẫn: chuyển nó sang /admin/ là đẻ ra hai đường dẫn cho một tài nguyên.
/// </summary>
[ApiController]
[Route("api/cancellation-requests")]
[Authorize(Policy = RbacPolicies.ManagerOrAbove)]
public class CancellationRequestReviewController : ControllerBase
{
    private readonly ITicketRequestReviewService _reviewService;

    public CancellationRequestReviewController(ITicketRequestReviewService reviewService)
        => _reviewService = reviewService;

    /// <summary>
    /// Duyệt hoặc từ chối một yêu cầu huỷ vé.
    /// Không có token → 401; không phải Admin/Manager → 403; <c>status</c> thiếu hoặc sai giá trị
    /// (kể cả <c>"Pending"</c>) → 400 kèm <c>errors.status</c>; <c>rejectReason</c> quá 2000 ký tự →
    /// 400; không có yêu cầu với <c>id</c> → 404; yêu cầu đã được người khác xử lý → 409; vé không
    /// còn <c>Paid</c> (đã bị huỷ hoặc đã soát ở cổng) → 409.
    ///
    /// Duyệt thì huỷ luôn vé gốc trong cùng lượt lưu; từ chối thì không đụng dòng vé.
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

        var result = await _reviewService.ReviewCancellationAsync(
            id, request, reviewerUserId, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>
    /// Người bấm duyệt lấy từ token. Token thiếu claim này là token không do API mình phát hành
    /// (hoặc token tự ký thiếu claim) — trả 401 thay vì đoán bừa một Guid rỗng rồi ghi vào
    /// <c>ReviewedByUserId</c>.
    /// </summary>
    private bool TryGetCurrentUserId(out Guid userId)
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private IActionResult MissingCurrentUser()
        => Unauthorized(new { message = "Không xác định được người dùng đang đăng nhập." });

    /// <summary>
    /// Một chỗ duy nhất quyết định mã HTTP cho mọi <see cref="ServiceErrorKind"/> — lỗi trả về giữ
    /// đúng một định dạng <c>{ message, errors }</c> cho mọi endpoint của dự án.
    /// </summary>
    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };

    /// <summary>
    /// Lỗi của model binding / data annotation. Tên trường đổi sang camelCase cho khớp JSON mà
    /// client gửi lên. Chép nguyên từ <c>FeedbackAdminController</c>: tám controller trong dự án
    /// đang giữ một bản sao của hàm này, và gom chúng lại là việc phải sửa file của người khác.
    /// </summary>
    private IActionResult ValidationError() => BadRequest(new
    {
        message = "Dữ liệu đầu vào không hợp lệ",
        errors = ModelState.Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray()),
    });
}
