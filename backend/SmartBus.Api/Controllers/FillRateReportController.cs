using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.Reports;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Thống kê tỷ lệ lấp đầy theo chuyến, tuyến, khung giờ (US 20 "Tỷ lệ lấp đầy", Sprint 4, task
/// *"API thống kê tỷ lệ lấp đầy theo chuyến, tuyến, khung giờ"* — Nguyễn Duy Kiên). Hợp đồng đầy
/// đủ ở mục "Tỷ lệ lấp đầy — /reports/fill-rate" của docs/api-contract.md.
///
/// Một endpoint duy nhất trả ba cách nhìn trên cùng tập chuyến: <c>trips[]</c>, <c>routes[]</c>,
/// <c>timeSlots[]</c> + <c>summary</c>. Màn hình không phải gọi ba lượt rồi tự đối chiếu tổng —
/// ba dải luôn cộng khớp vì cùng dựng từ một danh sách (xem doc
/// <see cref="FillRateReportService"/>).
///
/// Phân quyền gắn ở cả lớp: story 20 là nghiệp vụ của quản lý — Admin và Quản lý xem được, vai
/// trò khác nhận 403. GET thuần đọc nên không có dòng nào vào nhật ký kiểm toán (middleware chỉ
/// ghi POST/PUT/PATCH/DELETE).
/// </summary>
[ApiController]
[Route("api/reports/fill-rate")]
[Authorize(Policy = RbacPolicies.ManagerOrAbove)]
public class FillRateReportController : ControllerBase
{
    private readonly IFillRateReportService _fillRateReportService;

    public FillRateReportController(IFillRateReportService fillRateReportService)
        => _fillRateReportService = fillRateReportService;

    /// <summary>
    /// Tỷ lệ lấp đầy của các chuyến khởi hành trong kỳ. Mọi tham số bỏ trống được: mặc định 30
    /// ngày gần nhất (giờ Việt Nam), toàn bộ tuyến, mọi khung giờ.
    ///
    /// Không có token → 401; vai trò không phải Admin/Quản lý → 403; from sau to, khoảng quá 366
    /// ngày hoặc <c>timeSlot</c> lạ → 400; <c>routeId</c> không tồn tại → 404.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(FillRateReportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        [FromQuery] FillRateReportRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _fillRateReportService.GetAsync(request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    // Cùng một hàm ở AuthController, AdminUserController, FaresController, RoutesController,
    // StopsController, RouteStopsController, RouteTripsController, TripsController,
    // TripAssignmentController, TripLocationController, RevenueReportController và
    // RevenueBreakdownController. Cố ý chép lại thay vì tách thành lớp dùng chung: tách ra thì
    // phải sửa controller của người khác, mà luật nhóm không cho. Các bản giống nhau là cái giá
    // rẻ hơn.
    private IActionResult ValidationError() => BadRequest(new
    {
        message = "Dữ liệu đầu vào không hợp lệ",
        errors = ModelState.Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray()),
    });

    // Cùng một hàm ở MyTicketsController, SeatHoldLookupController, TripSearchController,
    // RoutesController, FaresController, VoucherValidationController, TripLocationController,
    // RevenueReportController và RevenueBreakdownController — chép lại cố ý, cùng lý do trên.
    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };
}
