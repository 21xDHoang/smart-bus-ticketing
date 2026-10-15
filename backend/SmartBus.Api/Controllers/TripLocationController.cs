using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.Trips;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Cập nhật vị trí hiện tại của chuyến — PUT /api/trips/{id}/location. US 10 "Thông báo trạm",
/// Sprint 4, task *"API cập nhật vị trí chuyến (CurrentLat/CurrentLng/CurrentStopId)"* — Trần
/// Trung Hiếu. Hợp đồng đầy đủ ở mục "PUT /trips/{id}/location" của docs/api-contract.md.
///
/// Đứng ở controller riêng trên bề mặt api/trips, cùng lối TripAssignmentController (Hoàng) và
/// TripsController (Dăm) — mỗi task một controller, không sửa file của người khác. Route
/// template khác nhau nên không tranh chấp.
///
/// ⚠️ Tên tham số đường dẫn phải đúng là <c>{id}</c>: <c>AuditLogMiddleware</c> suy tên bảng từ
/// đoạn đứng ngay trước tham số <c>{id}</c> để ghi Target "Trips:{id}" — đổi tên thành
/// <c>{tripId}</c> là nhật ký ghi sai đối tượng (cùng cảnh báo ở TripAssignmentController).
///
/// Phân quyền: <c>[Authorize(Roles = RoleCodes.Driver)]</c> — chỉ vai trò Tài xế. Không thêm
/// policy "DriverOrAbove" vào RbacMiddleware (file của Dăm — luật E1 cấm sửa file người khác);
/// RoleCodes đã khai sẵn để dùng thẳng làm tham số attribute (xem doc-comment của nó). Quyền
/// "đúng tài xế của chuyến này" được kiểm tiếp trong service theo DriverId.
/// </summary>
[ApiController]
[Route("api/trips")]
[Authorize(Roles = RoleCodes.Driver)]
public class TripLocationController : ControllerBase
{
    private readonly ITripLocationService _locationService;

    public TripLocationController(ITripLocationService locationService)
        => _locationService = locationService;

    /// <summary>
    /// Báo vị trí hiện tại của xe — một lần gọi là một ảnh chụp (toạ độ + trạm gần nhất).
    /// Idempotent: gọi lại y hệt chỉ làm mới <c>positionUpdatedAt</c>.
    ///
    /// Không có token → 401; vai trò không phải Tài xế → 403; chuyến không tồn tại hoặc không do
    /// tài xế này điều khiển → 404; chuyến đã hoàn thành/huỷ → 409; trạm không tồn tại → 404;
    /// trạm không thuộc tuyến → 400; toạ độ thiếu/sai khoảng → 400 theo trường.
    /// </summary>
    [HttpPut("{id:guid}/location")]
    [ProducesResponseType(typeof(TripLocationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateLocation(
        Guid id,
        [FromBody] UpdateTripLocationRequest request,
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

        var result = await _locationService.UpdateAsync(id, userId, request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>
    /// Người gọi lấy từ claim NameIdentifier — cùng hàm của MyTicketsController,
    /// SeatHoldLookupController, MonthlyPassLookupController, AdminUserController và
    /// DriversController. Cố ý chép lại thay vì tách lớp dùng chung: tách ra thì phải sửa
    /// controller của người khác, mà luật nhóm không cho. Các bản giống nhau là cái giá rẻ hơn.
    /// </summary>
    private bool TryGetCurrentUserId(out Guid userId)
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private IActionResult MissingCurrentUser()
        => Unauthorized(new { message = "Không xác định được người dùng đang đăng nhập." });

    // Cùng một hàm ở AuthController, AdminUserController, FaresController, RoutesController,
    // StopsController, RouteStopsController, RouteTripsController, TripsController và
    // TripAssignmentController. Cố ý chép lại thay vì tách thành lớp dùng chung: tách ra thì
    // phải sửa controller của người khác, mà luật nhóm không cho. Các bản giống nhau là cái
    // giá rẻ hơn.
    private IActionResult ValidationError() => BadRequest(new
    {
        message = "Dữ liệu đầu vào không hợp lệ",
        errors = ModelState.Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray()),
    });

    // Cùng một hàm ở MyTicketsController, SeatHoldLookupController, TripSearchController,
    // RoutesController, FaresController và VoucherValidationController — chép lại cố ý, cùng
    // lý do trên.
    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };
}
