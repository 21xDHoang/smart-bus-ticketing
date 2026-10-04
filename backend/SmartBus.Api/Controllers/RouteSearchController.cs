using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.Routes;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Tìm tuyến cho hành khách (GET /api/routes/search) — story 1, Trần Trung Hiếu.
/// Hợp đồng đầy đủ ở mục "Tra cứu tuyến — /routes/search" của docs/api-contract.md.
///
/// ⚠️ KHÔNG gắn [Authorize]: đây là API CÔNG KHAI của story 1 — tra cứu tuyến là việc trước khi
/// đăng nhập, đăng nhập là bước của màn hình đặt vé. Phân quyền của dự án là opt-in (không có
/// fallback policy), nên vắng [Authorize] nghĩa là ai cũng gọi được — cùng lối TripSearchController.
///
/// Đứng ở controller riêng cùng bề mặt api/routes với RoutesController (CRUD tuyến của quản lý —
/// Admin/Manager): mỗi bề mặt một controller, cùng lối cặp TripsController / TripSearchController.
/// Đoạn literal "search" không thể khớp {id:guid} của RoutesController nên không tranh chấp.
/// </summary>
[ApiController]
[Route("api/routes")]
public class RouteSearchController : ControllerBase
{
    private readonly IRouteSearchService _routeSearchService;

    public RouteSearchController(IRouteSearchService routeSearchService)
        => _routeSearchService = routeSearchService;

    /// <summary>
    /// Tuyến Active khớp điểm đi/điểm đến, kèm trạm và giá thấp nhất. Thiếu origin/destination
    /// hoặc hai điểm trùng nhau → 400; date sai định dạng → 400; không có tuyến nào khớp → [].
    /// </summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] RouteSearchRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _routeSearchService.SearchAsync(request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };

    // Cùng một hàm ở RoutesController, TripSearchController, TripLookupController và nhiều
    // controller khác. Cố ý chép lại thay vì tách thành lớp dùng chung: tách ra thì phải sửa cả
    // controller của người khác, mà luật nhóm không cho sửa file của người khác. Các bản giống
    // nhau là cái giá rẻ hơn.
    private IActionResult ValidationError() => BadRequest(new
    {
        message = "Dữ liệu đầu vào không hợp lệ",
        errors = ModelState.Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray()),
    });
}
