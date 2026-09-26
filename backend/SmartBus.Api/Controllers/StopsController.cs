using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.Stops;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// CRUD trạm dừng kèm toạ độ lat/lng và địa chỉ — US 12, Trần Trung Hiếu.
/// Hợp đồng đầy đủ ở mục "Trạm dừng — /stops" của docs/api-contract.md.
///
/// Phân quyền gắn ở cả lớp: story 12 nói "Là quản lý, tôi muốn thêm/sửa/xóa thông tin
/// danh sách trạm dừng", nên Admin và Quản lý dùng được, các vai trò khác nhận 403.
/// </summary>
[ApiController]
[Route("api/stops")]
[Authorize(Policy = RbacPolicies.ManagerOrAbove)]
public class StopsController : ControllerBase
{
    private readonly IStopService _stopService;

    public StopsController(IStopService stopService) => _stopService = stopService;

    /// <summary>Danh sách toàn bộ trạm dừng, xếp theo tên.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var result = await _stopService.ListAsync(cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>Chi tiết một trạm.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _stopService.GetByIdAsync(id, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>Thêm trạm mới kèm toạ độ và địa chỉ.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] StopRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _stopService.CreateAsync(request, cancellationToken);

        return result.Success
            ? CreatedAtAction(nameof(GetById), new { id = result.Data!.Id }, result.Data)
            : Failure(result);
    }

    /// <summary>Sửa trạm.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] StopRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _stopService.UpdateAsync(id, request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>
    /// Xoá hẳn trạm. Trạm đang nằm trên tuyến đường nào đó bị chặn — 409 Conflict.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _stopService.DeleteAsync(id, cancellationToken);

        return result.Success ? NoContent() : Failure(result);
    }

    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };

    // Cùng một hàm ở AdminUserController, AuthController và FaresController. Cố ý chép lại
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
