using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.Reports;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Xuất báo cáo tỷ lệ lấp đầy ra file — GET /api/reports/fill-rate/export. US 20 "Tỷ lệ lấp
/// đầy", Sprint 4 dòng 78 — Phùng Duy Hoàng. Hợp đồng đầy đủ ở mục "Xuất báo cáo tỷ lệ lấp đầy"
/// của docs/api-contract.md.
///
/// Báo cáo JSON <c>GET /api/reports/fill-rate</c> là việc của Kiên (dòng 66); endpoint này không
/// phụ thuộc vào nó — service tự đọc CSDL theo đúng định nghĩa đã chốt.
///
/// Phân quyền: Admin + Quản lý, cùng nhóm báo cáo doanh thu. GET thuần đọc nên không ghi nhật ký
/// kiểm toán.
/// </summary>
[ApiController]
[Route("api/reports/fill-rate")]
[Authorize(Policy = RbacPolicies.ManagerOrAbove)]
public class FillRateExportController : ControllerBase
{
    private readonly IFillRateExportService _fillRateExportService;

    public FillRateExportController(IFillRateExportService fillRateExportService)
        => _fillRateExportService = fillRateExportService;

    /// <summary>
    /// Trả file .xlsx (mặc định) hoặc .pdf, mỗi chuyến một dòng: mã chuyến, tuyến, giờ khởi hành
    /// (giờ Việt Nam), xe, sức chứa, số khách, tỷ lệ lấp đầy — kể cả chuyến 0%.
    ///
    /// Không token → 401; vai trò khác → 403; khoảng ngày ngược/quá 366 ngày, khung giờ lạ,
    /// format lạ, hoặc vượt trần dòng → 400; tuyến không tồn tại → 404. Lỗi vẫn là JSON, chỉ
    /// thành công mới là file.
    /// </summary>
    [HttpGet("export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Export(
        [FromQuery] FillRateExportRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _fillRateExportService.ExportAsync(
            request,
            User.FindFirstValue(ClaimTypes.Name) ?? User.Identity?.Name,
            cancellationToken);

        // Content-Type do service quyết định — controller không đoán lại lần thứ hai.
        return result.Success
            ? File(result.Data!.Content, result.Data.ContentType, result.Data.FileName)
            : Failure(result);
    }

    // Cùng một hàm ở RevenueExportController, AuditLogExportController… — chép lại cố ý: tách
    // thành lớp dùng chung thì phải sửa controller của người khác, mà luật nhóm không cho.
    private IActionResult ValidationError() => BadRequest(new
    {
        message = "Dữ liệu đầu vào không hợp lệ",
        errors = ModelState.Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray()),
    });

    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };
}
