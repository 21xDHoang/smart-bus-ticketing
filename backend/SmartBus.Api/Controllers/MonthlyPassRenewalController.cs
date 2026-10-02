using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SmartBus.Api.Dtos.MonthlyPasses;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Gia hạn vé tháng (POST /api/monthly-passes/{id}/renew) — story 16 "Đăng ký vé tháng",
/// Phùng Duy Hoàng. Hợp đồng đầy đủ ở mục "Vé tháng — /monthly-passes" của docs/api-contract.md.
///
/// [Authorize] trần (không policy): hành khách tự gia hạn vé của mình — RBAC của dự án chỉ có
/// AdminOnly/ManagerOrAbove, không có policy Passenger. Quyền sở hữu kiểm ở tầng service: vé của
/// người khác trả 404 như vé không tồn tại.
///
/// Đứng ở controller riêng cùng bề mặt api/monthly-passes với controller đăng ký vé tháng
/// (POST /monthly-passes — Trần Trung Hiếu, chưa làm): mỗi bề mặt một controller, cùng lối
/// TripSearchController đứng chung bề mặt api/trips. Đoạn literal "renew" đứng sau tham số nên
/// không tranh chấp với POST /monthly-passes.
/// </summary>
[ApiController]
[Route("api/monthly-passes")]
[Authorize]
public class MonthlyPassRenewalController : ControllerBase
{
    private readonly IMonthlyPassRenewalService _monthlyPassRenewalService;

    public MonthlyPassRenewalController(IMonthlyPassRenewalService monthlyPassRenewalService)
        => _monthlyPassRenewalService = monthlyPassRenewalService;

    /// <summary>
    /// Gia hạn vé tháng {id} của chính người gọi; trả 200 kèm vé MỚI vừa ghi (không phải vé cũ).
    /// Body tùy chọn (<c>{}</c> hoặc không gửi): passTypeCode bỏ trống = giữ nguyên loại vé.
    /// Không có token → 401; vé không tồn tại / vé của người khác / loại vé sai → 404;
    /// chồng lấn vé khác cùng tuyến → 409; passTypeCode quá 20 ký tự → 400.
    /// </summary>
    [HttpPost("{id:guid}/renew")]
    public async Task<IActionResult> Renew(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RenewMonthlyPassRequest? request,
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

        var result = await _monthlyPassRenewalService.RenewAsync(id, userId, request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>
    /// Người gọi lấy từ claim NameIdentifier — cùng hàm của AdminUserController và DriversController.
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
    // TripSearchController và TripAssignmentController. Cố ý chép lại thay vì tách thành lớp dùng
    // chung: tách ra thì phải sửa cả controller của người khác, mà luật nhóm không cho sửa file
    // của người khác. Các bản giống nhau là cái giá rẻ hơn.
    private IActionResult ValidationError() => BadRequest(new
    {
        message = "Dữ liệu đầu vào không hợp lệ",
        errors = ModelState.Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray()),
    });
}
