using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Kiểm tra trạng thái giữ chỗ theo mã phiên (GET /api/seat-holds/{sessionCode}) — story 3,
/// Trần Trung Hiếu. Hợp đồng đầy đủ ở mục "Giữ chỗ — /seat-holds" của docs/api-contract.md.
///
/// [Authorize] trần (không policy): hành khách tự xem phiên giữ chỗ của mình — RBAC của dự án chỉ
/// có AdminOnly/ManagerOrAbove, không có policy Passenger. Phạm vi "của tôi" nằm ở truy vấn theo
/// UserId trong service: phiên của người khác trả 404, không có tham số nào để dò phiên người khác.
///
/// Đứng ở controller riêng cho bề mặt api/seat-holds: POST /seat-holds (giữ ghế — Kiên), release
/// (Hoàng) và extend (task kế tiếp của Hiếu) mỗi task một controller — cùng lối TripSearchController
/// đứng chung bề mặt api/trips.
/// </summary>
[ApiController]
[Route("api/seat-holds")]
[Authorize]
public class SeatHoldLookupController : ControllerBase
{
    private readonly ISeatHoldLookupService _seatHoldLookupService;

    public SeatHoldLookupController(ISeatHoldLookupService seatHoldLookupService)
        => _seatHoldLookupService = seatHoldLookupService;

    /// <summary>
    /// Phiên giữ chỗ của chính người gọi theo mã phiên.
    /// Không có token → 401; phiên không tồn tại hoặc của người khác → 404.
    /// </summary>
    [HttpGet("{sessionCode}")]
    public async Task<IActionResult> GetBySessionCode(string sessionCode, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return MissingCurrentUser();
        }

        var result = await _seatHoldLookupService.GetBySessionCodeAsync(userId, sessionCode, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>
    /// Người gọi lấy từ claim NameIdentifier — cùng hàm của MonthlyPassLookupController,
    /// AdminUserController và DriversController. Cố ý chép lại thay vì tách lớp dùng chung:
    /// tách ra thì phải sửa cả controller của người khác, mà luật nhóm không cho sửa file
    /// của người khác. Các bản giống nhau là cái giá rẻ hơn.
    /// </summary>
    private bool TryGetCurrentUserId(out Guid userId)
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private IActionResult MissingCurrentUser()
        => Unauthorized(new { message = "Không xác định được người dùng đang đăng nhập." });

    // Cùng một hàm ở TripSearchController, RouteTripsController, RoutesController, FaresController,
    // TripLookupController và TripAssignmentController — chép lại cố ý, cùng lý do trên.
    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };
}
