using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.SeatLayouts;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// CRUD sơ đồ ghế theo loại xe — story 2, Phùng Duy Hoàng.
/// Hợp đồng đầy đủ ở mục "Sơ đồ ghế — /seat-layouts" của docs/api-contract.md.
///
/// Phân quyền gắn ở cả lớp: sơ đồ ghế là thuộc tính của xe nên cùng mức với /buses — Admin và
/// Quản lý dùng được, các vai trò khác nhận 403.
///
/// Khác các CRUD thường ở hai điểm, cả hai đều nằm trong hợp đồng:
/// - POST/PUT còn SINH GHẾ thật cho các xe cùng loại đang chưa có ghế (mục "Lưu sơ đồ là ghế có
///   ngay (tự sinh)"), nên response có thêm <c>busesSeated</c> — số xe vừa được sinh ghế.
/// - PUT đổi hình học của sơ đồ mà ghế đã bị giữ chỗ/bán vé thì trả 409, không âm thầm xoá ghế.
/// </summary>
[ApiController]
[Route("api/seat-layouts")]
[Authorize(Policy = RbacPolicies.ManagerOrAbove)]
public class SeatLayoutsController : ControllerBase
{
    private readonly ISeatLayoutService _seatLayoutService;

    public SeatLayoutsController(ISeatLayoutService seatLayoutService) => _seatLayoutService = seatLayoutService;

    /// <summary>Danh sách sơ đồ — tìm theo loại xe, không phân trang.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] ListSeatLayoutsRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _seatLayoutService.ListAsync(request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>Chi tiết một sơ đồ.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _seatLayoutService.GetByIdAsync(id, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>
    /// Thêm sơ đồ cho một loại xe rồi sinh ghế cho mọi xe cùng loại còn thiếu ghế.
    /// Trùng loại xe là 400 kèm errors.busType.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateSeatLayoutRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _seatLayoutService.CreateAsync(request, cancellationToken);

        return result.Success
            ? CreatedAtAction(nameof(GetById), new { id = result.Data!.Id }, result.Data)
            : Failure(result);
    }

    /// <summary>
    /// Sửa sơ đồ — hình học hoặc tập ghế VIP đổi thì đồng bộ luôn ghế của các xe cùng loại
    /// (xoá ghế cũ rồi sinh lại theo lưới mới). Ghế đang bị giữ chỗ hoặc đã bán mà đổi là 409.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateSeatLayoutRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _seatLayoutService.UpdateAsync(id, request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>Xoá sơ đồ. Còn ghế nào trỏ về sơ đồ là 409 — ghế là dữ liệu nghiệp vụ, không xoá dây chuyền.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _seatLayoutService.DeleteAsync(id, cancellationToken);

        return result.Success ? NoContent() : Failure(result);
    }

    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };

    // Cùng một hàm ở AdminUserController, AuthController, FaresController, RoutesController và
    // BusesController. Cố ý chép lại thay vì tách thành lớp dùng chung: tách ra thì phải sửa cả
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
