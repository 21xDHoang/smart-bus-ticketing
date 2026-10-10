using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Nhả ghế khi khách huỷ thao tác giữ chỗ theo mã phiên (POST /api/seat-holds/{sessionCode}/release)
/// — story 3, Phùng Duy Hoàng. Hợp đồng đầy đủ ở mục "Giữ chỗ — /seat-holds" của docs/api-contract.md.
///
/// [Authorize] trần (không policy): hành khách tự nhả phiên giữ chỗ của mình — RBAC của dự án
/// chỉ có AdminOnly/ManagerOrAbove, không có policy Passenger. Phạm vi "của tôi" nằm ở truy vấn
/// theo UserId trong service: phiên của người khác trả 404, không có tham số nào để dò phiên người khác.
///
/// Đứng ở controller riêng cho bề mặt api/seat-holds: POST /seat-holds (giữ ghế — Kiên), kiểm tra
/// trạng thái và gia hạn (Hiếu), release (task này) mỗi task một controller — cùng lối
/// TripSearchController đứng chung bề mặt api/trips.
/// </summary>
[ApiController]
[Route("api/seat-holds")]
[Authorize]
public class SeatHoldReleaseController : ControllerBase
{
    private readonly ISeatHoldReleaseService _seatHoldReleaseService;

    public SeatHoldReleaseController(ISeatHoldReleaseService seatHoldReleaseService)
        => _seatHoldReleaseService = seatHoldReleaseService;

    /// <summary>
    /// Nhả toàn bộ lượt giữ của phiên — màn hình gọi khi khách bấm "Huỷ" trong modal đếm ngược,
    /// hoặc khi đồng hồ về 0 mà modal còn mở. Không có body. Không có token → 401; phiên không
    /// tồn tại hoặc của người khác → 404; phiên đã chốt thành vé → 409; phiên đã kết thúc
    /// (Expired / Released) → 200 với đúng trạng thái hiện tại, gọi lại vẫn 200.
    /// </summary>
    [HttpPost("{sessionCode}/release")]
    public async Task<IActionResult> Release(string sessionCode, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return MissingCurrentUser();
        }

        var result = await _seatHoldReleaseService.ReleaseAsync(userId, sessionCode, cancellationToken);

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
    // TripLookupController, TripAssignmentController và SeatHoldExtendController — chép lại cố ý,
    // cùng lý do trên.
    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };
}
