using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.Trips;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Gán tài xế vào chuyến theo lô — US 14 "Phân công điều xe", Nguyễn Duy Kiên.
/// Hợp đồng đầy đủ ở mục "Lịch trình chạy xe — /routes/{routeId}/trips" của docs/api-contract.md.
///
/// Controller riêng thay vì thêm method vào <c>RouteTripsController</c>: quy ước không cho sửa file
/// của người khác (<c>RouteTripsController</c>/<c>RouteTripsService</c> là của Trần Trung Hiếu).
/// Cùng lối <c>FaresController</c> đã đi với <c>/api/routes/{routeId}/fares</c> và
/// <c>RouteStopsController</c> với <c>/api/routes/{routeId}/stops</c>.
///
/// Tên lớp có tiền tố <c>Trip</c> để không đụng <c>TripAssignmentController</c> — controller của
/// API đổi xe/đổi tài xế khi có sự cố (Phùng Duy Hoàng) đang nằm ở branch chưa merge.
///
/// Đường dẫn nằm dưới <c>/routes/{routeId}/trips</c> chứ không phải <c>/trips/driver-assignment</c>
/// vì hai lý do: đi song song với <c>POST .../trips/generate</c> (cũng là thao tác theo lô trên
/// nhiều chuyến của một tuyến), và nhờ có <c>{routeId}</c> trên đường dẫn nên
/// <c>AuditLogMiddleware</c> suy đúng tên bảng <c>Trips</c> cho dòng nhật ký.
///
/// Phân quyền gắn ở cả lớp: story 14 nói "Là quản lý, tôi muốn phân công chuyến cho tài xế", nên
/// Admin và Quản lý dùng được, các vai trò khác nhận 403.
/// </summary>
[ApiController]
[Route("api/routes/{routeId:guid}/trips")]
[Authorize(Policy = RbacPolicies.ManagerOrAbove)]
public class TripDriverAssignmentController : ControllerBase
{
    private readonly ITripDriverAssignmentService _driverAssignmentService;

    public TripDriverAssignmentController(ITripDriverAssignmentService driverAssignmentService)
        => _driverAssignmentService = driverAssignmentService;

    /// <summary>
    /// Gán MỘT tài xế cho NHIỀU chuyến của tuyến trong một thao tác.
    ///
    /// Nguyên tử: một chuyến không hợp lệ thì cả lô bị chặn. Trùng lịch tài xế KHÔNG chặn — trả
    /// trong <c>conflicts</c> của từng chuyến để màn hình cảnh báo mà vẫn cho lưu (luồng điều hành
    /// được phép cố ý chấp nhận trùng). Gọi lại y hệt → 200 kèm <c>assignedCount</c> bằng 0.
    /// </summary>
    [HttpPatch("driver-assignment")]
    public async Task<IActionResult> AssignDriver(
        Guid routeId,
        [FromBody] AssignDriverToTripsRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _driverAssignmentService.AssignAsync(routeId, request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };

    // Cùng một hàm ở AuthController, AdminUserController, FaresController, RoutesController,
    // StopsController, RouteStopsController, RouteTripsController và TripsController. Cố ý chép
    // lại thay vì tách thành lớp dùng chung: tách ra thì phải sửa controller của người khác, mà
    // luật nhóm không cho. Các bản giống nhau là cái giá rẻ hơn.
    private IActionResult ValidationError() => BadRequest(new
    {
        message = "Dữ liệu đầu vào không hợp lệ",
        errors = ModelState.Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray()),
    });
}
