using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.Reports;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Doanh thu theo tuyến / theo xe / theo phương thức thanh toán — ba endpoint trên cùng bề mặt
/// GET /api/reports/revenue (US 19 "Doanh thu", Sprint 4, task *"API doanh thu theo tuyến, theo
/// xe, theo phương thức thanh toán"* — Nguyễn Duy Kiên). Hợp đồng đầy đủ ở mục "Báo cáo doanh
/// thu — /reports/revenue" của docs/api-contract.md.
///
/// Controller riêng (không thêm action vào <see cref="RevenueReportController"/> — file của
/// Hiếu): cùng lối "mỗi task một controller" của <see cref="RevenueComparisonController"/>, dù cả
/// ba dùng chung tiền tố route.
///
/// Phân quyền gắn ở cả lớp: story 19 là nghiệp vụ của quản lý — Admin và Quản lý xem được, vai
/// trò khác nhận 403. GET thuần đọc nên không có dòng nào vào nhật ký kiểm toán (middleware chỉ
/// ghi POST/PUT/PATCH/DELETE).
/// </summary>
[ApiController]
[Route("api/reports/revenue")]
[Authorize(Policy = RbacPolicies.ManagerOrAbove)]
public class RevenueBreakdownController : ControllerBase
{
    private readonly IRevenueBreakdownService _revenueBreakdownService;

    public RevenueBreakdownController(IRevenueBreakdownService revenueBreakdownService)
        => _revenueBreakdownService = revenueBreakdownService;

    /// <summary>
    /// Doanh thu từng tuyến trong kỳ — một dòng cho mỗi tuyến có giao dịch Success. Mọi tham số
    /// bỏ trống được: mặc định 30 ngày gần nhất (giờ Việt Nam).
    ///
    /// Không có token → 401; vai trò không phải Admin/Quản lý → 403; from sau to hoặc khoảng quá
    /// 366 ngày → 400.
    /// </summary>
    [HttpGet("by-route")]
    [ProducesResponseType(typeof(RevenueByRouteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetByRoute(
        [FromQuery] RevenueBreakdownRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _revenueBreakdownService.GetByRouteAsync(request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>
    /// Doanh thu từng xe trong kỳ — một dòng cho mỗi xe chạy chuyến có giao dịch Success. Cùng
    /// tham số và mã lỗi với <see cref="GetByRoute"/>.
    /// </summary>
    [HttpGet("by-bus")]
    [ProducesResponseType(typeof(RevenueByBusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetByBus(
        [FromQuery] RevenueBreakdownRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _revenueBreakdownService.GetByBusAsync(request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>
    /// Doanh thu từng phương thức thanh toán trong kỳ — một dòng cho mỗi mã phương thức có giao
    /// dịch Success. Cùng tham số và mã lỗi với <see cref="GetByRoute"/>.
    /// </summary>
    [HttpGet("by-payment-method")]
    [ProducesResponseType(typeof(RevenueByPaymentMethodResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetByPaymentMethod(
        [FromQuery] RevenueBreakdownRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _revenueBreakdownService.GetByPaymentMethodAsync(request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    // Cùng một hàm ở AuthController, AdminUserController, FaresController, RoutesController,
    // StopsController, RouteStopsController, RouteTripsController, TripsController,
    // TripAssignmentController, TripLocationController và RevenueReportController. Cố ý chép lại
    // thay vì tách thành lớp dùng chung: tách ra thì phải sửa controller của người khác, mà luật
    // nhóm không cho. Các bản giống nhau là cái giá rẻ hơn.
    private IActionResult ValidationError() => BadRequest(new
    {
        message = "Dữ liệu đầu vào không hợp lệ",
        errors = ModelState.Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray()),
    });

    // Cùng một hàm ở MyTicketsController, SeatHoldLookupController, TripSearchController,
    // RoutesController, FaresController, VoucherValidationController, TripLocationController và
    // RevenueReportController — chép lại cố ý, cùng lý do trên.
    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };
}
