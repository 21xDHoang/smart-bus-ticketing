using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.Trips;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Lịch trình chạy xe theo tuyến — US 13 "Lập lịch trình", Trần Trung Hiếu.
/// Hợp đồng đầy đủ ở mục "Lịch trình chạy xe — /routes/{routeId}/trips" của docs/api-contract.md.
///
/// Tên lớp đặt là <c>RouteTripsController</c> chứ không phải <c>TripsController</c>: bề mặt API
/// còn có <c>GET /api/trips/{id}</c> (chi tiết chuyến — Vàng Thị Dăm) đứng ở controller
/// <c>TripsController</c> riêng. Cùng khuôn với cặp <c>StopsController</c> (CRUD trạm phẳng) và
/// <c>RouteStopsController</c> (gán trạm lồng dưới tuyến) đã có từ Sprint 1 — hai controller
/// khác tên, hai tầng đường dẫn khác nhau, không đụng nhau.
///
/// Lịch trình gắn chặt với tuyến nên đường dẫn lồng dưới tuyến
/// (<c>/api/routes/{routeId}/trips</c>) thay vì <c>/api/trips?routeId=…</c> — cùng lối với
/// <c>/api/routes/{routeId}/fares</c> và <c>/api/routes/{routeId}/stops</c> đã ghi ở mục D1.
///
/// Lịch trình định kỳ không tách bảng Schedule (quy ước A8.3): quản lý lập lịch bằng
/// POST .../trips/generate — tuyến + xe + mốc bắt đầu (ngày áp dụng + giờ khởi hành) + mốc
/// kết thúc + tần suất (phút) → hệ thống sinh N dòng Trips.
///
/// Phân quyền gắn ở cả lớp: story 13 nói "Là quản lý, tôi muốn thiết lập thời gian biểu",
/// nên Admin và Quản lý dùng được, các vai trò khác nhận 403.
/// </summary>
[ApiController]
[Route("api/routes/{routeId:guid}/trips")]
[Authorize(Policy = RbacPolicies.ManagerOrAbove)]
public class RouteTripsController : ControllerBase
{
    private readonly IRouteTripsService _tripService;

    public RouteTripsController(IRouteTripsService tripService) => _tripService = tripService;

    /// <summary>Danh sách chuyến của tuyến — lọc theo khoảng giờ khởi hành, trạng thái, phân trang.</summary>
    [HttpGet]
    public async Task<IActionResult> ListByRoute(
        Guid routeId,
        [FromQuery] ListTripsRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _tripService.ListAsync(routeId, request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>Chi tiết một chuyến của tuyến.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid routeId, Guid id, CancellationToken cancellationToken)
    {
        var result = await _tripService.GetByIdAsync(routeId, id, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>
    /// Thêm một chuyến lẻ cho tuyến. Chuyến mới luôn ở trạng thái Scheduled.
    /// Trùng khung giờ (cùng tuyến hoặc cùng xe) → 409; vượt trần 200 chuyến/ngày → 400.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        Guid routeId,
        [FromBody] CreateTripRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _tripService.CreateAsync(routeId, request, cancellationToken);

        return result.Success
            ? CreatedAtAction(nameof(GetById), new { routeId, id = result.Data!.Id }, result.Data)
            : Failure(result);
    }

    /// <summary>Sửa xe/giờ chạy. Trạng thái bỏ trống thì giữ nguyên.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid routeId,
        Guid id,
        [FromBody] UpdateTripRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _tripService.UpdateAsync(routeId, id, request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>
    /// Huỷ chuyến — chuyển về Cancelled, dữ liệu vẫn nằm trong CSDL (quy ước A4).
    /// Chuyến đã Completed không huỷ được. Trả 200 kèm chuyến đã huỷ.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid routeId, Guid id, CancellationToken cancellationToken)
    {
        var result = await _tripService.DeleteAsync(routeId, id, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>
    /// Sinh chuyến hàng loạt theo lịch trình định kỳ (quy ước A8.3):
    /// tuyến + xe + mốc bắt đầu + mốc kết thúc + tần suất (phút) → N dòng Trips.
    /// Trùng khung giờ với chuyến hiện có → 409, vượt trần 200 chuyến/ngày → 400;
    /// thao tác nguyên tử — lỗi thì không tạo chuyến nào. Trả 200 kèm danh sách chuyến vừa sinh.
    /// </summary>
    [HttpPost("generate")]
    public async Task<IActionResult> Generate(
        Guid routeId,
        [FromBody] GenerateTripsRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _tripService.GenerateAsync(routeId, request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };

    // Cùng một hàm ở RoutesController, FaresController và AdminUserController. Cố ý chép lại
    // thay vì tách thành lớp dùng chung: tách ra thì phải sửa cả controller của người khác,
    // mà luật nhóm không cho sửa file của người khác. Các bản giống nhau là cái giá rẻ hơn.
    private IActionResult ValidationError() => BadRequest(new
    {
        message = "Dữ liệu đầu vào không hợp lệ",
        errors = ModelState.Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray()),
    });
}
