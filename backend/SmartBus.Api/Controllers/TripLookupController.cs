using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.Trips;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Tra cứu danh sách chuyến theo ngày + lọc theo tuyến — US 13 "Lập lịch trình", Phùng Duy Hoàng.
/// Hợp đồng đầy đủ ở mục "Chuyến xe — /trips" của docs/api-contract.md.
///
/// Tên lớp đặt là <c>TripLookupController</c> chứ không phải <c>TripsController</c>: bề mặt
/// <c>/api/trips</c> đã có <c>GET /api/trips/{id}</c> (chi tiết chuyến — Vàng Thị Dăm) đứng ở
/// <c>TripsController</c> riêng. Cùng lối cặp <c>StopsController</c> (Hiếu) /
/// <c>RouteStopsController</c> (Kiên) và cặp <c>TripsController</c> / <c>RouteTripsController</c>
/// (Hiếu): mỗi bề mặt một controller, hai route template khác nhau
/// (<c>api/trips</c> so với <c>api/trips/{id:guid}</c>) nên không tranh chấp.
///
/// Phân quyền gắn ở cả lớp: story 13 phát biểu "Là quản lý, tôi muốn thiết lập thời gian biểu và
/// tần suất chạy xe cho từng tuyến theo ngày", nên Admin và Quản lý dùng được, các vai trò khác
/// nhận 403.
/// </summary>
[ApiController]
[Route("api/trips")]
[Authorize(Policy = RbacPolicies.ManagerOrAbove)]
public class TripLookupController : ControllerBase
{
    private readonly ITripLookupService _tripLookupService;

    public TripLookupController(ITripLookupService tripLookupService) => _tripLookupService = tripLookupService;

    /// <summary>
    /// Danh sách chuyến theo ngày (from/to) + lọc theo tuyến và trạng thái + phân trang.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid? routeId,
        [FromQuery] ListTripsRequest request,
        CancellationToken cancellationToken)
    {
        // ModelState ở đây bắt cả routeId sai định dạng GUID (binder của Guid? trả lỗi chuyển
        // kiểu) lẫn page/pageSize ngoài khoảng khai trong ListTripsRequest.
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _tripLookupService.ListAsync(routeId, request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };

    // Cùng một hàm ở RoutesController, FaresController, RouteTripsController và AdminUserController.
    // Cố ý chép lại thay vì tách thành lớp dùng chung: tách ra thì phải sửa cả controller của
    // người khác, mà luật nhóm không cho sửa file của người khác. Các bản giống nhau là cái giá rẻ hơn.
    private IActionResult ValidationError() => BadRequest(new
    {
        message = "Dữ liệu đầu vào không hợp lệ",
        errors = ModelState.Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray()),
    });
}
