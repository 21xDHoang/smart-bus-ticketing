using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Lịch sử thông báo trong ứng dụng của chính người gọi — GET /api/notifications/me. Task *"API lịch
/// sử thông báo đã nhận của hành khách"* (dòng 19, US 10, Sprint 5, Vàng Thị Dăm). Hợp đồng đầy đủ ở
/// mục "Thông báo trong ứng dụng — /notifications" của docs/api-contract.md.
///
/// [Authorize] trần (không policy): hành khách tự xem thông báo của mình — RBAC của dự án chỉ có
/// AdminOnly/ManagerOrAbove, không có policy Passenger. Phạm vi "của tôi" nằm ở truy vấn theo UserId
/// trong service: thông báo của người khác không có đường nào lọt ra, vì không có tham số nào để hỏi.
///
/// Đứng ở controller riêng cho bề mặt api/notifications: kênh ĐẨY là <c>NotificationHub</c> ở
/// /hubs/notifications (SignalR, không phải REST), còn các task Sprint 5 khác trên cùng bề mặt này
/// (API đăng ký/huỷ theo dõi — dòng 17; màn quản lý mẫu thông báo — dòng 24) sẽ là controller khác,
/// cùng lối "mỗi task một controller" của api/tickets và api/seat-holds.
///
/// Tên lớp theo cặp đã có: /api/tickets/me ↔ MyTicketsController + IMyTicketsService.
/// </summary>
[ApiController]
[Route("api/notifications")]
[Authorize]
public class MyNotificationsController : ControllerBase
{
    private readonly IMyNotificationsService _myNotificationsService;

    public MyNotificationsController(IMyNotificationsService myNotificationsService)
        => _myNotificationsService = myNotificationsService;

    /// <summary>
    /// Danh sách thông báo của chính người gọi — mọi loại, mảng trần, mới nhất trước, không phân
    /// trang (hợp đồng mục "GET /notifications/me").
    ///
    /// Không có token → 401; chưa nhận thông báo nào → 200 kèm mảng rỗng. Không có nhánh lỗi nào
    /// khác: endpoint không có tham số nào để gõ sai.
    /// </summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(IReadOnlyList<Dtos.Notifications.NotificationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return MissingCurrentUser();
        }

        var notifications = await _myNotificationsService.ListMineAsync(userId, cancellationToken);

        return Ok(notifications);
    }

    /// <summary>
    /// Người gọi lấy từ claim NameIdentifier — cùng hàm của MyTicketsController,
    /// SeatHoldLookupController, MonthlyPassLookupController và AdminUserController. Cố ý chép lại
    /// thay vì tách lớp dùng chung: tách ra thì phải sửa cả controller của người khác, mà luật nhóm
    /// không cho sửa file của người khác. Các bản giống nhau là cái giá rẻ hơn.
    /// </summary>
    private bool TryGetCurrentUserId(out Guid userId)
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private IActionResult MissingCurrentUser()
        => Unauthorized(new { message = "Không xác định được người dùng đang đăng nhập." });
}
