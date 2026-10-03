using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.Feedbacks;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Xử lý phản ánh phía quản trị — US 24 "Gửi khiếu nại hoặc đánh giá chất lượng chuyến đi", task
/// "API Admin phản hồi và đổi trạng thái phản ánh" (Sprint 2) — Phùng Duy Hoàng. Hợp đồng đầy đủ
/// ở mục "Phản ánh — /feedbacks" của docs/api-contract.md.
///
/// [Authorize(Policy = ManagerOrAbove)]: xử lý phản ánh là nghiệp vụ vận hành của nhà xe —
/// Admin và Manager đều làm được, vai trò khác nhận 403 JSON của RbacMiddleware.
///
/// Bốn endpoint nằm chung một controller vì cùng một bề mặt /admin/feedbacks: danh sách, chi tiết,
/// ghi phản hồi, đổi trạng thái — cùng lối gom nhóm của AdminUserController.
/// </summary>
[ApiController]
[Route("api/admin/feedbacks")]
[Authorize(Policy = RbacPolicies.ManagerOrAbove)]
public class FeedbackAdminController : ControllerBase
{
    private readonly IFeedbackAdminService _feedbackAdminService;

    public FeedbackAdminController(IFeedbackAdminService feedbackAdminService)
        => _feedbackAdminService = feedbackAdminService;

    /// <summary>
    /// Danh sách phản ánh — lọc theo trạng thái/loại, phân trang, mới nhất trước.
    /// Không có token → 401; không đủ quyền → 403; page &lt; 1 hoặc pageSize ngoài 1..100 → 400;
    /// status/type lạ → 200 kèm danh sách rỗng (không phải lỗi).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] ListFeedbacksRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _feedbackAdminService.ListAsync(request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>
    /// Chi tiết một phản ánh kèm toàn bộ luồng phản hồi (cũ → mới).
    /// Phản ánh không tồn tại (hoặc {id} sai định dạng GUID) → 404.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _feedbackAdminService.GetByIdAsync(id, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>
    /// Phản hồi hành khách — ghi thêm một dòng vào luồng, trả 200 kèm phản ánh đầy đủ.
    /// KHÔNG tự đổi trạng thái: muốn chuyển thì gọi PATCH (hợp đồng nói rõ lý do).
    /// content trống hoặc quá 2000 ký tự → 400 errors.content; phản ánh không tồn tại → 404.
    /// </summary>
    [HttpPost("{id:guid}/replies")]
    public async Task<IActionResult> Reply(
        Guid id,
        [FromBody] CreateFeedbackReplyRequest request,
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

        var result = await _feedbackAdminService.ReplyAsync(id, userId, request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>
    /// Đổi trạng thái xử lý — mọi chiều chuyển đều hợp lệ (không có máy trạng thái).
    /// Trả 200 kèm phản ánh đầy đủ; status thiếu hoặc ngoài ba giá trị → 400 errors.status;
    /// phản ánh không tồn tại → 404.
    /// </summary>
    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> UpdateStatus(
        Guid id,
        [FromBody] UpdateFeedbackStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _feedbackAdminService.UpdateStatusAsync(id, request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>
    /// Người gọi lấy từ claim NameIdentifier — cùng hàm của AdminUserController, DriversController
    /// và MonthlyPassRenewalController. Chỉ endpoint ghi phản hồi cần: nó lưu ai là tác giả.
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

    // Cùng một hàm ở RouteTripsController, RoutesController, FaresController, TripLookupController,
    // TripSearchController, TripAssignmentController và MonthlyPassRenewalController. Cố ý chép lại
    // thay vì tách thành lớp dùng chung: tách ra thì phải sửa cả controller của người khác, mà luật
    // nhóm không cho sửa file của người khác. Các bản giống nhau là cái giá rẻ hơn.
    private IActionResult ValidationError() => BadRequest(new
    {
        message = "Dữ liệu đầu vào không hợp lệ",
        errors = ModelState.Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray()),
    });
}
