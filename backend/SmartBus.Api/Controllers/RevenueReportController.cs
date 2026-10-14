using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.Reports;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Báo cáo doanh thu theo ngày / tuần / tháng — GET /api/reports/revenue. US 19 "Doanh thu",
/// Sprint 4, task *"API báo cáo doanh thu theo ngày / tuần / tháng"* — Trần Trung Hiếu. Hợp đồng
/// đầy đủ ở mục "Báo cáo doanh thu — /reports/revenue" của docs/api-contract.md.
///
/// Controller riêng cho bề mặt /reports/revenue: các task khác cùng story (theo tuyến / xe /
/// phương thức — Kiên; so sánh giữa các kỳ — Hoàng; xuất file — các task sau) sẽ mở rộng trên
/// cùng bề mặt này, mỗi task một controller.
///
/// Phân quyền gắn ở cả lớp: story 19 nói "Là quản lý, tôi muốn xem báo cáo doanh thu" — Admin
/// và Quản lý dùng được, các vai trò khác nhận 403. GET thuần đọc nên không có dòng nào vào
/// nhật ký kiểm toán (middleware chỉ ghi POST/PUT/PATCH/DELETE).
/// </summary>
[ApiController]
[Route("api/reports/revenue")]
[Authorize(Policy = RbacPolicies.ManagerOrAbove)]
public class RevenueReportController : ControllerBase
{
    private readonly IRevenueReportService _revenueReportService;

    public RevenueReportController(IRevenueReportService revenueReportService)
        => _revenueReportService = revenueReportService;

    /// <summary>
    /// Tổng hợp doanh thu theo độ mịn day/week/month: hai con số tổng (thẻ số liệu dashboard)
    /// + dãy bucket liên tục (biểu đồ). Mọi tham số bỏ trống được — mặc định 30 ngày gần nhất,
    /// gộp theo ngày, toàn công ty.
    ///
    /// Không có token → 401; vai trò không phải Admin/Quản lý → 403; granularity lạ → 400;
    /// from sau to hoặc khoảng quá 366 ngày → 400; tuyến không tồn tại → 404.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(RevenueReportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        [FromQuery] RevenueReportRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _revenueReportService.GetAsync(request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    // Cùng một hàm ở AuthController, AdminUserController, FaresController, RoutesController,
    // StopsController, RouteStopsController, RouteTripsController, TripsController,
    // TripAssignmentController và TripLocationController. Cố ý chép lại thay vì tách thành lớp
    // dùng chung: tách ra thì phải sửa controller của người khác, mà luật nhóm không cho. Các
    // bản giống nhau là cái giá rẻ hơn.
    private IActionResult ValidationError() => BadRequest(new
    {
        message = "Dữ liệu đầu vào không hợp lệ",
        errors = ModelState.Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray()),
    });

    // Cùng một hàm ở MyTicketsController, SeatHoldLookupController, TripSearchController,
    // RoutesController, FaresController, VoucherValidationController và TripLocationController —
    // chép lại cố ý, cùng lý do trên.
    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };
}
