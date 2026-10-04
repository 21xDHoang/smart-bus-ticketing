using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.MonthlyPasses;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Đăng ký vé tháng (POST /api/monthly-passes) — story 16 "Đăng ký vé tháng", Trần Trung Hiếu.
/// Hợp đồng đầy đủ ở mục "Vé tháng — /monthly-passes" của docs/api-contract.md.
///
/// [Authorize] trần (không policy): hành khách tự đăng ký vé của mình — RBAC của dự án chỉ có
/// AdminOnly/ManagerOrAbove, không có policy Passenger. Vé luôn gắn userId của người gọi, lấy từ
/// claim NameIdentifier như MonthlyPassRenewalController.
///
/// Đứng ở controller riêng cùng bề mặt api/monthly-passes với MonthlyPassRenewalController
/// (POST /monthly-passes/{id}/renew — Phùng Duy Hoàng) và MonthlyPassLookupController
/// (GET /monthly-passes/me — Phùng Duy Hoàng): mỗi bề mặt một controller, cùng lối TripSearchController
/// đứng chung bề mặt api/trips. POST ở gốc không tranh chấp với đoạn literal "renew"/"me" của hai
/// controller kia.
/// </summary>
[ApiController]
[Route("api/monthly-passes")]
[Authorize]
public class MonthlyPassRegistrationController : ControllerBase
{
    private readonly IMonthlyPassRegistrationService _monthlyPassRegistrationService;

    public MonthlyPassRegistrationController(IMonthlyPassRegistrationService monthlyPassRegistrationService)
        => _monthlyPassRegistrationService = monthlyPassRegistrationService;

    /// <summary>
    /// Đăng ký vé tháng mới của chính người gọi; trả 200 kèm vé vừa ghi. Không có token → 401;
    /// tuyến không tồn tại / loại vé sai → 404; chồng lấn vé khác cùng tuyến → 409;
    /// routeId thiếu/sai GUID hoặc passTypeCode thiếu/quá 20 ký tự → 400.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Register(
        [FromBody] RegisterMonthlyPassRequest request,
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

        var result = await _monthlyPassRegistrationService.RegisterAsync(request, userId, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>
    /// Người gọi lấy từ claim NameIdentifier — cùng hàm của MonthlyPassRenewalController,
    /// AdminUserController và DriversController.
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

    // Cùng một hàm ở MonthlyPassRenewalController, RouteTripsController, RoutesController,
    // FaresController, TripLookupController, TripSearchController và TripAssignmentController.
    // Cố ý chép lại thay vì tách thành lớp dùng chung: tách ra thì phải sửa cả controller của người
    // khác, mà luật nhóm không cho sửa file của người khác. Các bản giống nhau là cái giá rẻ hơn.
    private IActionResult ValidationError() => BadRequest(new
    {
        message = "Dữ liệu đầu vào không hợp lệ",
        errors = ModelState.Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray()),
    });
}
