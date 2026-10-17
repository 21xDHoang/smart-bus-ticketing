using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.Reports;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Đề xuất tăng/giảm tần suất chạy xe theo dữ liệu — GET /api/reports/frequency-proposals. US 20
/// "Tỷ lệ lấp đầy", Sprint 4, task *"API đề xuất tăng/giảm tần suất chạy xe theo dữ liệu"* —
/// Phùng Duy Hoàng. Hợp đồng đầy đủ ở mục "Đề xuất tần suất chạy xe — /reports/frequency-proposals"
/// của docs/api-contract.md.
///
/// Controller riêng cho bề mặt /reports/frequency-proposals, đúng lệ "mỗi task một controller" của
/// story 20 (báo cáo gốc là PeakHoursReportController của Hiếu). Không sửa file của Hiếu — service
/// này tiêu thụ IPeakHoursReportService công khai của báo cáo gốc.
///
/// Phân quyền gắn ở cả lớp: story 20 nói "Là quản lý, tôi muốn xem thống kê" — Admin và Quản lý
/// dùng được, các vai trò khác nhận 403. GET thuần đọc nên không có dòng nào vào nhật ký kiểm
/// toán (middleware chỉ ghi POST/PUT/PATCH/DELETE).
/// </summary>
[ApiController]
[Route("api/reports/frequency-proposals")]
[Authorize(Policy = RbacPolicies.ManagerOrAbove)]
public class FrequencyProposalController : ControllerBase
{
    private readonly IFrequencyProposalService _frequencyProposalService;

    public FrequencyProposalController(IFrequencyProposalService frequencyProposalService)
        => _frequencyProposalService = frequencyProposalService;

    /// <summary>
    /// Đề xuất số chuyến nên chạy cho từng giờ, dựa trên mật độ khách so với trung bình cả kỳ.
    /// Giờ cao điểm chỉ tăng hoặc giữ, giờ không cao điểm chỉ giảm hoặc giữ.
    ///
    /// Không có token → 401; vai trò không phải Admin/Quản lý → 403; from sau to hoặc khoảng
    /// quá 366 ngày → 400; tuyến không tồn tại → 404 — tất cả lỗi đầu vào do báo cáo gốc quyết
    /// định, endpoint này không định nghĩa lại.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(FrequencyProposalResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        [FromQuery] FrequencyProposalRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _frequencyProposalService.GetAsync(request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    // Cùng một hàm ở AuthController, AdminUserController, FaresController, RoutesController,
    // StopsController, RouteStopsController, RouteTripsController, TripsController,
    // TripAssignmentController, TripLocationController, PeakHoursReportController và
    // RevenueReportController. Cố ý chép lại thay vì tách thành lớp dùng chung: tách ra thì phải
    // sửa controller của người khác, mà luật nhóm không cho. Các bản giống nhau là cái giá rẻ hơn.
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
    // PeakHoursReportController và RevenueReportController — chép lại cố ý, cùng lý do trên.
    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };
}
