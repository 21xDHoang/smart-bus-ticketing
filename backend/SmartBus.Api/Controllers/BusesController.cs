using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.Buses;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// CRUD danh sách xe buýt — US 14, Trần Trung Hiếu.
/// Hợp đồng đầy đủ ở mục "Xe buýt — /buses" của docs/api-contract.md.
///
/// Phân quyền gắn ở cả lớp: story 14 nói "Là quản lý, tôi muốn gán xe buýt và tài xế/phụ xe
/// cho từng chuyến chạy cụ thể", nên Admin và Quản lý dùng được, các vai trò khác nhận 403 —
/// cùng lối RoutesController.
/// </summary>
[ApiController]
[Route("api/buses")]
[Authorize(Policy = RbacPolicies.ManagerOrAbove)]
public class BusesController : ControllerBase
{
    private readonly IBusService _busService;

    public BusesController(IBusService busService) => _busService = busService;

    /// <summary>Danh sách xe — hỗ trợ tìm kiếm, lọc trạng thái và phân trang.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] ListBusesRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _busService.ListAsync(request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>Chi tiết một xe.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _busService.GetByIdAsync(id, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>Thêm xe mới. Trùng biển số là lỗi 400 kèm errors.licensePlate.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateBusRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _busService.CreateAsync(request, cancellationToken);

        return result.Success
            ? CreatedAtAction(nameof(GetById), new { id = result.Data!.Id }, result.Data)
            : Failure(result);
    }

    /// <summary>Sửa xe. Trạng thái bỏ trống thì giữ nguyên.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateBusRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _busService.UpdateAsync(id, request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>
    /// Xoá mềm — chuyển xe về Inactive, dữ liệu vẫn nằm trong CSDL.
    /// Trả 200 kèm xe đã ngừng khai thác.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _busService.DeleteAsync(id, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };

    // Cùng một hàm ở AdminUserController, AuthController, FaresController và RoutesController.
    // Cố ý chép lại thay vì tách thành lớp dùng chung: tách ra thì phải sửa cả controller của
    // người khác, mà luật nhóm không cho sửa file của người khác. Các bản giống nhau là cái
    // giá rẻ hơn.
    private IActionResult ValidationError() => BadRequest(new
    {
        message = "Dữ liệu đầu vào không hợp lệ",
        errors = ModelState.Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray()),
    });
}
