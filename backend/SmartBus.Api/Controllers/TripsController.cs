using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Chuyến xe — US 13, Vàng Thị Dăm. Hiện chỉ có chi tiết chuyến.
/// Hợp đồng đầy đủ ở mục "Chuyến xe — /trips" của docs/api-contract.md.
///
/// Phân quyền gắn ở cả lớp: story 13 phát biểu "Là quản lý, tôi muốn thiết lập thời gian biểu và
/// tần suất chạy xe cho từng tuyến theo ngày", nên Admin và Quản lý dùng được, các vai trò khác
/// nhận 403.
///
/// Chưa có <c>GET /trips</c> (danh sách theo ngày) và chưa có endpoint tạo chuyến: ba task còn lại
/// của story 13 thuộc Hiếu, Kiên và Hoàng (xem cột "Assign" ở sheet Sprint 2). Bản chi tiết chuyến
/// đứng một mình vẫn có nghĩa — chuyến được sinh ra bởi BackgroundService của Kiên, và màn hình
/// nào có id chuyến thì gọi được vào đây.
///
/// Cố ý KHÔNG có hàm <c>ValidationError()</c> như các controller khác: controller này chỉ có một
/// endpoint không nhận body, nên không có ModelState nào để dựng lỗi 400. Thêm endpoint thêm/sửa
/// chuyến của story 13 thì chép hàm đó từ RoutesController sang — xem chú thích ở đó về lý do mỗi
/// controller tự giữ một bản.
/// </summary>
[ApiController]
[Route("api/trips")]
[Authorize(Policy = RbacPolicies.ManagerOrAbove)]
public class TripsController : ControllerBase
{
    private readonly ITripService _tripService;

    public TripsController(ITripService tripService) => _tripService = tripService;

    /// <summary>
    /// Chi tiết một chuyến: giờ chạy, tuyến, loại xe + sức chứa, danh sách trạm dừng.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _tripService.GetByIdAsync(id, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };
}
