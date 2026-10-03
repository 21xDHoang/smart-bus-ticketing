using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.Feedbacks;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Phản ánh của chính người gọi — GET /api/feedbacks/me và /api/feedbacks/me/{id:guid}. Task *"API
/// danh sách phản ánh của hành khách + theo dõi trạng thái xử lý"* (US 24, Sprint 2, Nguyễn Duy
/// Kiên). Hợp đồng đầy đủ ở mục "Phản ánh — /feedbacks", phần "Phản ánh của tôi" của
/// docs/api-contract.md.
///
/// [Authorize] trần (không policy): RBAC của dự án chỉ có AdminOnly/ManagerOrAbove, không có policy
/// Passenger. Phạm vi "của tôi" nằm ở truy vấn theo UserId trong service — không có tham số nào để
/// dò phản ánh của người khác, và ca đó trả 404 chứ không phải 403. Cùng lối
/// MonthlyPassLookupController.
///
/// Đứng ở controller riêng cùng bề mặt api/feedbacks với FeedbackAdminController (api/admin/feedbacks
/// là tiền tố khác hẳn nên không đụng nhau): mỗi bề mặt một controller, cùng lối TripSearchController
/// đứng chung bề mặt api/trips. Đoạn literal "me" là chuỗi tĩnh nên không tranh chấp với tham số
/// {id} của route khác.
/// </summary>
[ApiController]
[Route("api/feedbacks")]
[Authorize]
public class FeedbackLookupController : ControllerBase
{
    private readonly IFeedbackLookupService _feedbackLookupService;

    public FeedbackLookupController(IFeedbackLookupService feedbackLookupService)
        => _feedbackLookupService = feedbackLookupService;

    /// <summary>
    /// Phản ánh của chính người gọi, mới nhất trước; trả 200 kèm MẢNG (có thể rỗng), không phân
    /// trang. Không có token → 401; status dài quá 20 ký tự → 400; status gõ sai → 200 kèm mảng
    /// rỗng (không phải lỗi).
    /// </summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetMine(
        [FromQuery] ListMyFeedbacksRequest request,
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

        var feedbacks = await _feedbackLookupService.ListMineAsync(userId, request, cancellationToken);

        return Ok(feedbacks);
    }

    /// <summary>
    /// Chi tiết một phản ánh của chính người gọi, kèm toàn bộ luồng phản hồi (cũ → mới).
    /// Phản ánh không tồn tại — hoặc thuộc hành khách khác — → 404 với cùng một câu.
    /// </summary>
    [HttpGet("me/{id:guid}")]
    public async Task<IActionResult> GetMineById(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return MissingCurrentUser();
        }

        var result = await _feedbackLookupService.GetMineAsync(userId, id, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>
    /// Người gọi lấy từ claim NameIdentifier — cùng hàm của AdminUserController, DriversController,
    /// MonthlyPassLookupController và MonthlyPassRenewalController. Cả hai endpoint đều cần: userId
    /// chính là phạm vi truy vấn, không phải chỉ để ghi tác giả.
    /// </summary>
    private bool TryGetCurrentUserId(out Guid userId)
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private IActionResult MissingCurrentUser()
        => Unauthorized(new { message = "Không xác định được người dùng đang đăng nhập." });

    /// <summary>
    /// Chỉ có nhánh NotFound là tới được đây: service không bao giờ trả Invalid/Conflict (bộ lọc
    /// status do DTO chặn ở controller) — hai nhánh còn lại giữ nguyên cho khớp khuôn Failure của dự
    /// án và để sau này thêm ca lỗi không phải sửa cấu trúc.
    /// </summary>
    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };

    // Cùng một hàm ở RouteTripsController, RoutesController, FaresController, TripLookupController,
    // TripSearchController, TripAssignmentController, MonthlyPassRenewalController và
    // FeedbackAdminController. Cố ý chép lại thay vì tách thành lớp dùng chung: tách ra thì phải sửa
    // cả controller của người khác, mà luật nhóm không cho sửa file của người khác.
    private IActionResult ValidationError() => BadRequest(new
    {
        message = "Dữ liệu đầu vào không hợp lệ",
        errors = ModelState.Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray()),
    });
}
