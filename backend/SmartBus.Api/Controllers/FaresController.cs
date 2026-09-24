using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.Fares;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Cấu hình bảng giá vé theo tuyến và theo đối tượng ưu đãi — US 12, Phùng Duy Hoàng.
/// Hợp đồng đầy đủ ở mục "Bảng giá vé" của docs/api-contract.md.
///
/// Giá vé gắn chặt với tuyến nên đường dẫn lồng dưới tuyến
/// (<c>/api/routes/{routeId}/fares</c>) thay vì <c>/api/fares?routeId=…</c> — cùng lối với
/// <c>/api/routes/{id}/stops</c> đã ghi ở mục D1.
///
/// Phân quyền gắn ở cả lớp: story 12 nói "Là quản lý, tôi muốn thiết lập giá vé", nên
/// Admin và Quản lý dùng được, các vai trò khác nhận 403.
/// </summary>
[ApiController]
[Route("api/routes/{routeId:guid}/fares")]
[Authorize(Policy = RbacPolicies.ManagerOrAbove)]
public class FaresController : ControllerBase
{
    private readonly IFareService _fareService;

    public FaresController(IFareService fareService) => _fareService = fareService;

    /// <summary>Bảng giá của tuyến. Tuyến chưa cấu hình giá trả về mảng rỗng.</summary>
    [HttpGet]
    public async Task<IActionResult> ListByRoute(Guid routeId, CancellationToken cancellationToken)
    {
        var result = await _fareService.ListByRouteAsync(routeId, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>Một dòng giá trong bảng giá của tuyến.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid routeId, Guid id, CancellationToken cancellationToken)
    {
        var result = await _fareService.GetByIdAsync(routeId, id, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>Thêm một dòng giá cho tuyến. Trùng đối tượng đã có giá là 409.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        Guid routeId,
        [FromBody] CreateFareRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _fareService.CreateAsync(routeId, request, cancellationToken);

        return result.Success
            ? CreatedAtAction(nameof(GetById), new { routeId, id = result.Data!.Id }, result.Data)
            : Failure(result);
    }

    /// <summary>Sửa giá của một dòng đã có. Không đổi được đối tượng — xoá rồi tạo lại.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid routeId,
        Guid id,
        [FromBody] UpdateFareRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _fareService.UpdateAsync(routeId, id, request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>Xoá hẳn một dòng giá khỏi bảng giá của tuyến.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid routeId, Guid id, CancellationToken cancellationToken)
    {
        var result = await _fareService.DeleteAsync(routeId, id, cancellationToken);

        return result.Success ? NoContent() : Failure(result);
    }

    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };

    // Cùng một hàm ở AdminUserController và AuthController. Cố ý chép lại thay vì tách thành
    // lớp dùng chung: tách ra thì phải sửa cả hai controller của người khác, mà luật nhóm
    // không cho sửa file của người khác. Ba bản giống nhau là cái giá rẻ hơn.
    private IActionResult ValidationError() => BadRequest(new
    {
        message = "Dữ liệu đầu vào không hợp lệ",
        errors = ModelState.Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray()),
    });
}
