using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.MonthlyPasses.Admin;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Quản trị vé tháng (GET /api/monthly-passes, GET /{id}, GET /statistics,
/// PATCH /{id}/status) — bổ sung ngoài story 16, hợp đồng ở mục
/// "Vé tháng — /monthly-passes" của docs/api-contract.md.
///
/// [Authorize(Policy = ManagerOrAbove)]: đây là nghiệp vụ vận hành của nhà xe — Admin và Quản lý
/// đều xem/huỷ vé được. Người không đủ quyền nhận 403 theo handler chung của RbacMiddleware.
///
/// Đứng ở controller riêng cùng bề mặt api/monthly-passes với ba controller vé tháng của hành
/// khách, cùng lối các controller khác đứng chung một bề mặt. Đoạn literal "statistics" thắng
/// tham số "{id:guid}" nên không tranh chấp route.
///
/// Vì sao service không inject qua DI: đăng ký DI nằm trong Program.cs — file của Phùng Duy
/// Hoàng, ngoài phạm vi được sửa. Controller nhận AppDbContext (đã có trong DI) rồi khởi tạo
/// MonthlyPassAdminService trực tiếp; DbContext là scoped nên vòng đời vẫn đúng.
/// </summary>
[ApiController]
[Route("api/monthly-passes")]
[Authorize(Policy = RbacPolicies.ManagerOrAbove)]
public class MonthlyPassAdminController : ControllerBase
{
    private readonly MonthlyPassAdminService _monthlyPassAdminService;

    public MonthlyPassAdminController(AppDbContext db)
        => _monthlyPassAdminService = new MonthlyPassAdminService(db);

    /// <summary>
    /// Danh sách vé tháng toàn hệ thống — lọc theo mã vé/họ tên/SĐT, trạng thái, tuyến, loại vé;
    /// phân trang. Trả 200 kèm { items, total, page, pageSize }.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] ListMonthlyPassesRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _monthlyPassAdminService.ListAsync(request, cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Số vé theo nhóm hiệu lực + doanh thu. Không tham số.
    /// </summary>
    [HttpGet("statistics")]
    public async Task<IActionResult> Statistics(CancellationToken cancellationToken)
        => Ok(await _monthlyPassAdminService.GetStatisticsAsync(cancellationToken));

    /// <summary>
    /// Chi tiết một vé bất kỳ; 404 khi id không tồn tại (không có nhánh "vé của người khác" —
    /// quản trị viên xem được mọi vé).
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var pass = await _monthlyPassAdminService.GetAsync(id, cancellationToken);

        return pass is null
            ? NotFound(new { message = "Không tìm thấy vé tháng" })
            : Ok(pass);
    }

    /// <summary>
    /// Huỷ (Active → Cancelled) hoặc mở lại (Cancelled → Active) một vé.
    /// 404 khi id không tồn tại; 409 khi không chuyển được trạng thái (đã hết hạn, trạng thái
    /// do job quản lý, hoặc đang đã ở trạng thái đích); 400 khi status sai.
    /// </summary>
    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(
        Guid id,
        [FromBody] UpdateMonthlyPassStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _monthlyPassAdminService.UpdateStatusAsync(id, request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };

    // Cùng một hàm ở MonthlyPassRegistrationController, MonthlyPassRenewalController,
    // TripAssignmentController… Cố ý chép lại thay vì tách lớp dùng chung — các bản giống nhau
    // là cái giá rẻ hơn sửa file của người khác.
    private IActionResult ValidationError() => BadRequest(new
    {
        message = "Dữ liệu đầu vào không hợp lệ",
        errors = ModelState.Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray()),
    });
}
