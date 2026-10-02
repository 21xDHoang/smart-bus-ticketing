using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.Trips;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Tìm chuyến cho hành khách (GET /api/trips/search) — story 1, Phùng Duy Hoàng.
/// Hợp đồng đầy đủ ở mục "GET /trips/search" của docs/api-contract.md.
///
/// ⚠️ KHÔNG gắn [Authorize]: đây là API CÔNG KHAI duy nhất của bề mặt /trips — story 1 là luồng
/// tra cứu của hành khách trước khi đăng nhập, đăng nhập là bước của màn hình đặt vé. Phân quyền
/// của dự án là opt-in (không có fallback policy), nên vắng [Authorize] nghĩa là ai cũng gọi được.
///
/// Đứng ở controller riêng cùng bề mặt api/trips với TripLookupController (màn hình điều hành,
/// cùng người làm) và TripsController (chi tiết chuyến — Vàng Thị Dăm): mỗi bề mặt một controller,
/// cùng lối cặp StopsController / RouteStopsController đã có từ Sprint 1. Đoạn literal "search"
/// không thể khớp {id:guid} của hai controller kia nên không tranh chấp.
/// </summary>
[ApiController]
[Route("api/trips")]
public class TripSearchController : ControllerBase
{
    private readonly ITripSearchService _tripSearchService;

    public TripSearchController(ITripSearchService tripSearchService)
        => _tripSearchService = tripSearchService;

    /// <summary>
    /// Chuyến Scheduled của một tuyến, kèm giá vé phổ thông và số ghế còn trống.
    /// Thiếu routeId hoặc sai định dạng GUID → 400; tuyến không tồn tại → 404;
    /// to sớm hơn from → 400.
    /// </summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] SearchTripsRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _tripSearchService.SearchAsync(request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };

    // Cùng một hàm ở RouteTripsController, RoutesController, FaresController, TripLookupController
    // và TripAssignmentController. Cố ý chép lại thay vì tách thành lớp dùng chung: tách ra thì
    // phải sửa cả controller của người khác, mà luật nhóm không cho sửa file của người khác.
    // Các bản giống nhau là cái giá rẻ hơn.
    private IActionResult ValidationError() => BadRequest(new
    {
        message = "Dữ liệu đầu vào không hợp lệ",
        errors = ModelState.Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray()),
    });
}
