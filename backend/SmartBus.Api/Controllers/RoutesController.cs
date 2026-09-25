using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.Routes;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// CRUD tuyến đường — US 12, Trần Trung Hiếu.
/// Hợp đồng đầy đủ ở mục "Tuyến đường — /routes" của docs/api-contract.md.
///
/// Phân quyền gắn ở cả lớp: story 12 nói "Là quản lý, tôi muốn thêm/sửa/xóa thông tin
/// tuyến đường", nên Admin và Quản lý dùng được, các vai trò khác nhận 403.
/// </summary>
[ApiController]
[Route("api/routes")]
[Authorize(Policy = RbacPolicies.ManagerOrAbove)]
public class RoutesController : ControllerBase
{
    private readonly IRouteService _routeService;

    public RoutesController(IRouteService routeService) => _routeService = routeService;

    /// <summary>Danh sách tuyến — hỗ trợ tìm kiếm, lọc trạng thái và phân trang.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] ListRoutesRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _routeService.ListAsync(request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>Chi tiết một tuyến.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _routeService.GetByIdAsync(id, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>Thêm tuyến mới. Trùng mã tuyến là lỗi 400 kèm errors.code.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateRouteRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _routeService.CreateAsync(request, cancellationToken);

        return result.Success
            ? CreatedAtAction(nameof(GetById), new { id = result.Data!.Id }, result.Data)
            : Failure(result);
    }

    /// <summary>Sửa tuyến. Trạng thái bỏ trống thì giữ nguyên.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateRouteRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _routeService.UpdateAsync(id, request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>
    /// Xoá mềm — chuyển tuyến về Inactive, dữ liệu vẫn nằm trong CSDL.
    /// Trả 200 kèm tuyến đã ngừng khai thác.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _routeService.DeleteAsync(id, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
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
