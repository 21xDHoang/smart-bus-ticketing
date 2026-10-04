using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.Stops;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Gợi ý trạm dừng cho hành khách (GET /api/stops/search). Hợp đồng đầy đủ ở mục
/// "Tra cứu trạm dừng — /stops/search" của docs/api-contract.md.
///
/// ⚠️ KHÔNG gắn [Authorize]: đây là API CÔNG KHAI — hành khách gõ từ khoá để CHỌN trạm TRƯỚC khi
/// đăng nhập, rồi mới gọi GET /routes/search bằng tên trạm vừa chọn. Phân quyền của dự án là
/// opt-in (không có fallback policy), nên vắng [Authorize] nghĩa là ai cũng gọi được — cùng lối
/// RouteSearchController và TripSearchController.
///
/// Đứng ở controller riêng cùng bề mặt api/stops với StopsController (CRUD trạm của quản lý —
/// <c>[Authorize(ManagerOrAbove)]</c>): mỗi bề mặt một controller, cùng lối cặp
/// TripsController / TripSearchController. Đoạn literal "search" không thể khớp {id:guid} của
/// StopsController nên không tranh chấp route.
///
/// Tên controller số nhiều (Stops…) theo lối StopsController, còn DTO và service số ít
/// (StopSearch…) theo lối RouteSearchRequest / RouteSearchService — mỗi tầng giữ quy ước của
/// tầng mình, không phải đánh máy nhầm.
/// </summary>
[ApiController]
[Route("api/stops")]
public class StopsSearchController : ControllerBase
{
    private readonly IStopSearchService _stopSearchService;

    public StopsSearchController(IStopSearchService stopSearchService)
        => _stopSearchService = stopSearchService;

    /// <summary>
    /// Trạm khớp từ khoá (tên hoặc địa chỉ, không phân biệt hoa thường và không phân biệt dấu),
    /// tối đa <c>limit</c> dòng — mặc định 10. Bỏ trống từ khoá = đầu danh sách theo tên.
    /// <c>limit</c> ngoài 1..50 → 400 kèm <c>errors.limit</c>; không trạm nào khớp → [].
    /// </summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] StopSearchRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _stopSearchService.SearchAsync(request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };

    // Cùng một hàm ở RoutesController, RouteSearchController, TripSearchController và nhiều
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
