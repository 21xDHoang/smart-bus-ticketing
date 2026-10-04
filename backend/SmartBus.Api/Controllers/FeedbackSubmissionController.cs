using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.Feedbacks;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Gửi phản ánh — POST /api/feedbacks. Task *"API gửi phản ánh: chọn chuyến, loại phản ánh, nội
/// dung, đính kèm ảnh"* (US 24, Sprint 2, Trần Trung Hiếu). Hợp đồng đầy đủ ở mục
/// "Phản ánh — /feedbacks", phần "Gửi phản ánh" của docs/api-contract.md.
///
/// [Authorize] trần (không policy): hành khách tự gửi phản ánh của mình — RBAC của dự án chỉ có
/// AdminOnly/ManagerOrAbove, không có policy Passenger. Phản ánh luôn gắn userId của người gọi,
/// lấy từ claim NameIdentifier như FeedbackLookupController — userId KHÔNG có trong body, không ai
/// gửi phản ánh hộ người khác được.
///
/// Đứng ở controller riêng cùng bề mặt api/feedbacks với FeedbackLookupController (GET .../me —
/// Nguyễn Duy Kiên) và FeedbackAdminController (api/admin/feedbacks là tiền tố khác hẳn nên không
/// đụng nhau): mỗi bề mặt một controller, cùng lối TripSearchController đứng chung bề mặt
/// api/trips. POST ở gốc không tranh chấp với đoạn literal "me" của controller kia.
/// </summary>
[ApiController]
[Route("api/feedbacks")]
[Authorize]
public class FeedbackSubmissionController : ControllerBase
{
    private readonly IFeedbackSubmissionService _feedbackSubmissionService;

    public FeedbackSubmissionController(IFeedbackSubmissionService feedbackSubmissionService)
        => _feedbackSubmissionService = feedbackSubmissionService;

    /// <summary>
    /// Gửi một phản ánh mới của chính người gọi; trả 201 kèm phản ánh vừa ghi (status luôn New).
    /// Không có token → 401; tripId trỏ chuyến không tồn tại → 404; loại phản ánh sai → 400
    /// errors.type; content/rating/attachmentUrl sai khuôn → 400.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(FeedbackSubmissionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Submit(
        [FromBody] CreateFeedbackRequest request,
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

        var result = await _feedbackSubmissionService.SubmitAsync(request, userId, cancellationToken);

        // Trả 201 bằng StatusCode thay vì CreatedAtAction: không có hành động GET nào trên controller
        // này để trỏ Location tới (GET /feedbacks/me/{id} nằm ở FeedbackLookupController, là file
        // của Nguyễn Duy Kiên) — cùng lối AuthController.Register.
        return result.Success
            ? StatusCode(StatusCodes.Status201Created, result.Data)
            : Failure(result);
    }

    /// <summary>
    /// Người gọi lấy từ claim NameIdentifier — cùng hàm của FeedbackLookupController,
    /// MonthlyPassRegistrationController và AdminUserController.
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

    // Cùng một hàm ở FeedbackLookupController, FeedbackAdminController, MonthlyPassRegistrationController,
    // RouteTripsController, RoutesController, FaresController, TripLookupController, TripSearchController
    // và TripAssignmentController. Cố ý chép lại thay vì tách thành lớp dùng chung: tách ra thì phải
    // sửa cả controller của người khác, mà luật nhóm không cho sửa file của người khác.
    private IActionResult ValidationError() => BadRequest(new
    {
        message = "Dữ liệu đầu vào không hợp lệ",
        errors = ModelState.Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray()),
    });
}
