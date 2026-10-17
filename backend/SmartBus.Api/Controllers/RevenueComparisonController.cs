using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.Reports;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// So sánh doanh thu kỳ hiện tại với kỳ liền trước cùng độ dài — GET
/// /api/reports/revenue/comparison. US 19 "Doanh thu", Sprint 4, task *"API so sánh doanh thu giữa
/// các kỳ (tăng/giảm %)"* — Phùng Duy Hoàng. Hợp đồng đầy đủ ở mục "Báo cáo doanh thu —
/// /reports/revenue" của docs/api-contract.md.
///
/// Cùng bề mặt /reports/revenue với <see cref="RevenueReportController"/> của Hiếu, nhưng
/// controller RIÊNG — đúng lệ "mỗi task một controller" đã ghi ở controller kia, không sửa file
/// của người khác.
///
/// Phân quyền gắn ở cả lớp: story 19 là nghiệp vụ của quản lý, Admin và Quản lý dùng được, vai
/// trò khác nhận 403. GET thuần đọc nên không có dòng nào vào nhật ký kiểm toán.
/// </summary>
[ApiController]
[Route("api/reports/revenue/comparison")]
[Authorize(Policy = RbacPolicies.ManagerOrAbove)]
public class RevenueComparisonController : ControllerBase
{
    private readonly IRevenueComparisonService _revenueComparisonService;

    public RevenueComparisonController(IRevenueComparisonService revenueComparisonService)
        => _revenueComparisonService = revenueComparisonService;

    /// <summary>
    /// So sánh kỳ from → to với kỳ liền trước cùng số ngày: doanh thu và số giao dịch của hai kỳ,
    /// chênh lệch tuyệt đối và % tăng/giảm. Mọi tham số bỏ trống được — mặc định 30 ngày gần
    /// nhất, toàn công ty.
    ///
    /// Không có token → 401; vai trò không phải Admin/Quản lý → 403; from sau to hoặc khoảng quá
    /// 366 ngày → 400; tuyến không tồn tại → 404.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(RevenueComparisonResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        [FromQuery] RevenueComparisonRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _revenueComparisonService.GetAsync(request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    // Cùng một hàm ở RevenueReportController, AuthController, AdminUserController... — cố ý chép
    // lại thay vì tách lớp dùng chung: tách ra thì phải sửa controller của người khác, mà luật
    // nhóm không cho. Các bản giống nhau là cái giá rẻ hơn.
    private IActionResult ValidationError() => BadRequest(new
    {
        message = "Dữ liệu đầu vào không hợp lệ",
        errors = ModelState.Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray()),
    });

    // Cùng một hàm ở RevenueReportController và nhiều controller khác — chép lại cố ý, cùng lý do.
    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };
}
