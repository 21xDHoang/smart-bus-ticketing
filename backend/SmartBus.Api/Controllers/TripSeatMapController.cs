using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Sơ đồ ghế theo chuyến cho hành khách (GET /api/trips/{id}/seats) — story 2, Trần Trung Hiếu.
/// Hợp đồng đầy đủ ở mục "GET /trips/{id}/seats" của docs/api-contract.md.
///
/// ⚠️ KHÔNG gắn [Authorize]: cùng lối TripSearchController — hành khách xem sơ đồ ghế để chọn chỗ
/// TRƯỚC khi đăng nhập, đăng nhập là bước của API giữ ghế (US 3). Phân quyền của dự án là opt-in
/// (không có fallback policy), nên vắng [Authorize] nghĩa là ai cũng gọi được.
///
/// Đứng ở controller riêng cùng bề mặt api/trips: chi tiết chuyến GET /trips/{id} là của Vàng Thị
/// Dăm (TripsController), tìm chuyến là của Phùng Duy Hoàng (TripSearchController) — mỗi bề mặt một
/// controller. Đoạn literal "seats" nằm SAU {id:guid} nên không tranh chấp với GET /trips/{id:guid}.
/// </summary>
[ApiController]
[Route("api/trips")]
public class TripSeatMapController : ControllerBase
{
    private readonly ITripSeatMapService _tripSeatMapService;

    public TripSeatMapController(ITripSeatMapService tripSeatMapService)
        => _tripSeatMapService = tripSeatMapService;

    /// <summary>
    /// Sơ đồ ghế của chuyến kèm trạng thái từng ghế (Available / Held / Paid).
    /// Chuyến không tồn tại hoặc xe của chuyến đã bị xoá → 404.
    /// </summary>
    [HttpGet("{id:guid}/seats")]
    public async Task<IActionResult> GetSeatMap(Guid id, CancellationToken cancellationToken)
    {
        var result = await _tripSeatMapService.GetSeatMapAsync(id, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    // Cùng một hàm ở TripSearchController, RouteTripsController, RoutesController, FaresController,
    // TripLookupController và TripAssignmentController. Cố ý chép lại thay vì tách thành lớp dùng
    // chung: tách ra thì phải sửa cả controller của người khác, mà luật nhóm không cho sửa file
    // của người khác. Các bản giống nhau là cái giá rẻ hơn.
    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };
}
