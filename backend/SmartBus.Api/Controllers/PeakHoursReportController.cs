using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.Reports;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Phân tích khung giờ cao điểm theo mật độ hành khách — GET /api/reports/peak-hours. US 20
/// "Tỷ lệ lấp đầy", Sprint 4, task *"API phân tích khung giờ cao điểm theo mật độ hành khách"* —
/// Trần Trung Hiếu. Hợp đồng đầy đủ ở mục "Khung giờ cao điểm — /reports/peak-hours" của
/// docs/api-contract.md.
///
/// Controller riêng cho bề mặt /reports/peak-hours: các task khác cùng story (tỷ lệ lấp đầy theo
/// chuyến/tuyến — Kiên; đề xuất tần suất — Hoàng) sẽ mở rộng trên bề mặt /reports, mỗi task một
/// controller. Báo cáo doanh thu (US 19) là controller chị em RevenueReportController.
///
/// Phân quyền gắn ở cả lớp: story 20 nói "Là quản lý, tôi muốn xem thống kê" — Admin và Quản lý
/// dùng được, các vai trò khác nhận 403. GET thuần đọc nên không có dòng nào vào nhật ký kiểm
/// toán (middleware chỉ ghi POST/PUT/PATCH/DELETE).
/// </summary>
[ApiController]
[Route("api/reports/peak-hours")]
[Authorize(Policy = RbacPolicies.ManagerOrAbove)]
public class PeakHoursReportController : ControllerBase
{
    private readonly IPeakHoursReportService _peakHoursReportService;

    public PeakHoursReportController(IPeakHoursReportService peakHoursReportService)
        => _peakHoursReportService = peakHoursReportService;

    /// <summary>
    /// Gộp khách của từng chuyến theo GIỜ KHỞI HÀNH (giờ Việt Nam) thành trục cố định 24 giờ,
    /// đánh dấu giờ cao điểm theo quy tắc mật độ ≥ trung bình cả kỳ.
    ///
    /// Không có token → 401; vai trò không phải Admin/Quản lý → 403; from sau to hoặc khoảng
    /// quá 366 ngày → 400; tuyến không tồn tại → 404.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PeakHoursReportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        [FromQuery] PeakHoursReportRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _peakHoursReportService.GetAsync(request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    // Cùng một hàm ở AuthController, AdminUserController, FaresController, RoutesController,
    // StopsController, RouteStopsController, RouteTripsController, TripsController,
    // TripAssignmentController, TripLocationController và RevenueReportController (nhánh
    // feature/19-api-bao-cao-doanh-thu, chưa merge lúc viết dòng này). Cố ý chép lại thay vì
    // tách thành lớp dùng chung: tách ra thì phải sửa controller của người khác, mà luật nhóm
    // không cho. Các bản giống nhau là cái giá rẻ hơn.
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
    // RevenueReportController (nhánh feature/19-api-bao-cao-doanh-thu, chưa merge lúc viết dòng
    // này) — chép lại cố ý, cùng lý do trên.
    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };
}
