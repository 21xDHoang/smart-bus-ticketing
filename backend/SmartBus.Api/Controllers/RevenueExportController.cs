using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.Reports;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Xuất báo cáo doanh thu ra file — GET /api/reports/revenue/export. US 24 "Xuất dữ liệu",
/// Sprint 4 dòng 78 — Phùng Duy Hoàng. Hợp đồng đầy đủ ở mục "Xuất báo cáo doanh thu" của
/// docs/api-contract.md.
///
/// Controller riêng trên cùng bề mặt /reports/revenue với <see cref="RevenueReportController"/>
/// — đúng lệ "mỗi task một controller". Không sửa file của Hiếu.
///
/// Phân quyền: cùng báo cáo gốc (Admin + Quản lý). GET thuần đọc nên không ghi nhật ký kiểm toán.
/// </summary>
[ApiController]
[Route("api/reports/revenue")]
[Authorize(Policy = RbacPolicies.ManagerOrAbove)]
public class RevenueExportController : ControllerBase
{
    private readonly IRevenueExportService _revenueExportService;

    public RevenueExportController(IRevenueExportService revenueExportService)
        => _revenueExportService = revenueExportService;

    /// <summary>
    /// Trả file .xlsx (mặc định) hoặc .pdf chứa đúng các bucket của báo cáo JSON: kỳ, doanh thu,
    /// số giao dịch — kể cả bucket 0 đồng. Tham số dùng chung với báo cáo gốc, thêm <c>format</c>.
    ///
    /// Không token → 401; vai trò khác → 403; granularity lạ / from sau to / quá 366 ngày /
    /// format lạ → 400; tuyến không tồn tại → 404. Lỗi vẫn là JSON, chỉ thành công mới là file.
    /// </summary>
    [HttpGet("export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Export(
        [FromQuery] RevenueExportRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _revenueExportService.ExportAsync(
            request,
            User.FindFirstValue(ClaimTypes.Name) ?? User.Identity?.Name,
            cancellationToken);

        // Content-Type lấy từ file kết quả, không suy lại từ tham số: service là nơi duy nhất biết
        // nó vừa dựng Excel hay PDF, controller không có lý do đoán lần thứ hai.
        return result.Success
            ? File(result.Data!.Content, result.Data.ContentType, result.Data.FileName)
            : Failure(result);
    }

    // Cùng một hàm ở RevenueReportController, AuditLogExportController… — chép lại cố ý: tách
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
