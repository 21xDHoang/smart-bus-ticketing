using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Tra cứu vé tháng đang hoạt động của chính người gọi (GET /api/monthly-passes/me) — story 16
/// "Đăng ký vé tháng", Phùng Duy Hoàng. Hợp đồng đầy đủ ở mục "Vé tháng — /monthly-passes"
/// của docs/api-contract.md.
///
/// [Authorize] trần (không policy): RBAC của dự án chỉ có AdminOnly/ManagerOrAbove, không có
/// policy Passenger. Phạm vi "của tôi" nằm ở truy vấn theo UserId trong service — không có tham
/// số nào để dò vé của người khác.
///
/// Đứng ở controller riêng cùng bề mặt api/monthly-passes với controller gia hạn
/// (MonthlyPassRenewalController) và controller đăng ký vé tháng (POST /monthly-passes —
/// Trần Trung Hiếu, chưa làm): mỗi bề mặt một controller, cùng lối TripSearchController đứng
/// chung bề mặt api/trips. Đoạn literal "me" là chuỗi tĩnh nên không tranh chấp với tham số
/// {id} của các route khác.
/// </summary>
[ApiController]
[Route("api/monthly-passes")]
[Authorize]
public class MonthlyPassLookupController : ControllerBase
{
    private readonly IMonthlyPassLookupService _monthlyPassLookupService;

    public MonthlyPassLookupController(IMonthlyPassLookupService monthlyPassLookupService)
        => _monthlyPassLookupService = monthlyPassLookupService;

    /// <summary>
    /// Vé tháng đang hoạt động của chính người gọi; trả 200 kèm MẢNG (có thể rỗng), không phân trang.
    /// Không có token → 401. Không còn nhánh lỗi nào khác: "chưa có vé nào" là mảng rỗng, không 404.
    /// </summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return MissingCurrentUser();
        }

        var passes = await _monthlyPassLookupService.GetActiveAsync(userId, cancellationToken);

        return Ok(passes);
    }

    /// <summary>
    /// Người gọi lấy từ claim NameIdentifier — cùng hàm của AdminUserController và DriversController.
    /// </summary>
    private bool TryGetCurrentUserId(out Guid userId)
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private IActionResult MissingCurrentUser()
        => Unauthorized(new { message = "Không xác định được người dùng đang đăng nhập." });
}
