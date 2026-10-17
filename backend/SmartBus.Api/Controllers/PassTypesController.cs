using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.PassTypes;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Danh mục loại vé tháng (GET/POST/PUT/DELETE /api/pass-types) — bổ sung ngoài story 16, hợp
/// đồng ở mục "Loại vé tháng — /pass-types" của docs/api-contract.md.
///
/// [Authorize(Policy = ManagerOrAbove)]: giá gói là quyết định kinh doanh — cùng ranh giới với
/// FaresController (cấu hình giá vé lượt).
///
/// Vì sao service không inject qua DI: đăng ký DI nằm trong Program.cs — file của Phùng Duy
/// Hoàng, ngoài phạm vi được sửa. Controller nhận AppDbContext (đã có trong DI) rồi khởi tạo
/// PassTypeService trực tiếp; DbContext là scoped nên vòng đời vẫn đúng.
/// </summary>
[ApiController]
[Route("api/pass-types")]
[Authorize(Policy = RbacPolicies.ManagerOrAbove)]
public class PassTypesController : ControllerBase
{
    private readonly PassTypeService _passTypeService;

    public PassTypesController(AppDbContext db)
        => _passTypeService = new PassTypeService(db);

    /// <summary>Toàn bộ danh mục, sắp theo thời hạn rồi mã. Trả mảng trần.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
        => Ok(await _passTypeService.ListAsync(cancellationToken));

    /// <summary>
    /// Thêm loại vé; trả 201 kèm loại vé vừa tạo. Trùng mã → 409; vi phạm ràng buộc → 400.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreatePassTypeRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _passTypeService.CreateAsync(request, cancellationToken);

        return result.Success
            ? StatusCode(StatusCodes.Status201Created, result.Data)
            : Failure(result);
    }

    /// <summary>
    /// Sửa tên/thời hạn/giá; trả 200 kèm loại vé sau khi lưu. Mã KHÔNG sửa được.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdatePassTypeRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _passTypeService.UpdateAsync(id, request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>
    /// Xoá loại vé chưa từng được dùng; trả 200 kèm loại vé đã xoá. Đang có vé tháng tham chiếu
    /// → 409. Không tồn tại → 404.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _passTypeService.DeleteAsync(id, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };

    // Cùng một hàm ở các controller khác — cố ý chép lại thay vì sửa file của người khác.
    private IActionResult ValidationError() => BadRequest(new
    {
        message = "Dữ liệu đầu vào không hợp lệ",
        errors = ModelState.Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray()),
    });
}
