using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.Drivers;
using SmartBus.Api.Dtos.Trips;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Hồ sơ tài xế: CRUD tài khoản tài xế + danh sách ca làm việc — US 14, Trần Trung Hiếu.
/// Hợp đồng đầy đủ ở mục "Hồ sơ tài xế — /drivers" của docs/api-contract.md.
///
/// Phân quyền gắn ở cả lớp: story 14 nói "Là quản lý, tôi muốn gán xe buýt và tài xế/phụ xe
/// cho từng chuyến chạy cụ thể", nên Admin và Quản lý dùng được, các vai trò khác nhận 403 —
/// cùng lối BusesController. Tài xế KHÔNG xem được hồ sơ của chính mình ở đây: màn hình cá
/// nhân của tài xế là chuyện của nhóm story theo dõi thời gian thực (Sprint 3).
/// </summary>
[ApiController]
[Route("api/drivers")]
[Authorize(Policy = RbacPolicies.ManagerOrAbove)]
public class DriversController : ControllerBase
{
    private readonly IDriverService _driverService;

    public DriversController(IDriverService driverService) => _driverService = driverService;

    /// <summary>Danh sách tài xế — hỗ trợ tìm kiếm, lọc trạng thái hoạt động và phân trang.</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] ListDriversRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _driverService.ListAsync(request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>Chi tiết hồ sơ một tài xế. Id không phải tài xế cũng trả 404.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _driverService.GetByIdAsync(id, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>Tạo hồ sơ tài xế mới. Trùng SĐT là lỗi 400 kèm errors.phoneNumber.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateDriverRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _driverService.CreateAsync(request, cancellationToken);

        return result.Success
            ? CreatedAtAction(nameof(GetById), new { id = result.Data!.Id }, result.Data)
            : Failure(result);
    }

    /// <summary>Sửa hồ sơ tài xế (họ tên, SĐT, email).</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateDriverRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _driverService.UpdateAsync(id, request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>
    /// Xoá mềm — khóa tài khoản tài xế, dữ liệu vẫn nằm trong CSDL.
    /// Không tự khóa được tài khoản của chính mình.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return MissingCurrentUser();
        }

        var result = await _driverService.DeleteAsync(id, currentUserId, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>
    /// Ca làm việc của tài xế — danh sách chuyến tài xế được phân công, lọc theo khoảng giờ
    /// khởi hành + trạng thái, xếp theo giờ chạy tăng dần.
    /// </summary>
    [HttpGet("{id:guid}/trips")]
    public async Task<IActionResult> ListTrips(
        Guid id,
        [FromQuery] ListTripsRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _driverService.ListTripsAsync(id, request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };

    /// <summary>
    /// Id người đang gọi API, lấy từ claim mà <see cref="JwtMiddleware"/> đã gắn.
    /// Policy ManagerOrAbove bảo đảm luôn có, nhưng vẫn kiểm tra thay vì tin tưởng mù —
    /// Guid.Empty lọt vào tầng nghiệp vụ sẽ thành lỗi rất khó lần ra.
    /// </summary>
    private bool TryGetCurrentUserId(out Guid userId)
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private IActionResult MissingCurrentUser()
        => Unauthorized(new { message = "Không xác định được người dùng đang đăng nhập." });

    // Cùng một hàm ở AdminUserController, AuthController, BusesController, FaresController và
    // RoutesController. Cố ý chép lại thay vì tách thành lớp dùng chung: tách ra thì phải sửa cả
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
