using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.SeatHolds;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Giữ ghế tạm thời (POST /api/seat-holds) — story 3, Nguyễn Duy Kiên.
/// Hợp đồng đầy đủ ở mục "Giữ chỗ — /seat-holds" của docs/api-contract.md.
///
/// [Authorize] trần (không policy): hành khách tự giữ ghế cho mình — RBAC của dự án chỉ có
/// AdminOnly/ManagerOrAbove, không có policy Passenger. Người giữ lấy từ claim NameIdentifier như
/// FeedbackSubmissionController; userId KHÔNG có trong body nên không ai giữ ghế hộ người khác được.
///
/// Đứng ở controller riêng cho bề mặt api/seat-holds: POST /seat-holds (giữ ghế — task này),
/// release (Hoàng), kiểm tra trạng thái và gia hạn (Hai task của Hiếu) mỗi task một controller —
/// cùng lối TripSearchController đứng chung bề mặt api/trips. POST ở gốc không tranh chấp với hai
/// đoạn con {sessionCode} của hai controller kia.
/// </summary>
[ApiController]
[Route("api/seat-holds")]
[Authorize]
public class SeatHoldCreateController : ControllerBase
{
    private readonly ISeatHoldCreateService _seatHoldCreateService;

    public SeatHoldCreateController(ISeatHoldCreateService seatHoldCreateService)
        => _seatHoldCreateService = seatHoldCreateService;

    /// <summary>
    /// Giữ tạm nhóm ghế của một chuyến trong 10 phút; trả 201 kèm phiên vừa tạo (status = Holding).
    /// Không có token → 401; body sai khuôn → 400; chuyến không tồn tại → 404; ghế không thuộc xe
    /// của chuyến → 400 errors.seatIds; chuyến đã huỷ / đã chạy xong / ghế đã có người giữ → 409.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(SeatHoldSessionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateSeatHoldRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        if (!TryGetCurrentUserId(out var userId))
        {
            return MissingCurrentUser();
        }

        var result = await _seatHoldCreateService.CreateAsync(userId, request, cancellationToken);

        // Trả 201 bằng StatusCode thay vì CreatedAtAction: hành động đọc một phiên nằm ở
        // SeatHoldLookupController (GET /seat-holds/{sessionCode} — file của Hiếu) và khoá tra cứu
        // là sessionCode sinh trong service, không phải id trên đường dẫn — cùng lối
        // FeedbackSubmissionController và AuthController.Register.
        return result.Success
            ? StatusCode(StatusCodes.Status201Created, result.Data)
            : Failure(result);
    }

    /// <summary>
    /// Người gọi lấy từ claim NameIdentifier — cùng hàm của SeatHoldLookupController,
    /// SeatHoldExtendController, FeedbackSubmissionController và MonthlyPassLookupController.
    /// </summary>
    private bool TryGetCurrentUserId(out Guid userId)
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private IActionResult MissingCurrentUser()
        => Unauthorized(new { message = "Không xác định được người dùng đang đăng nhập." });

    // Cùng một hàm ở SeatHoldExtendController, SeatHoldLookupController, FeedbackSubmissionController,
    // RouteTripsController, RoutesController, FaresController, TripSearchController…
    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };

    /// <summary>
    /// Body sai khuôn (thiếu tripId, seatIds rỗng…) — Program.cs đã tắt filter validate tự động của
    /// ASP.NET Core (SuppressModelStateInvalidFilter) để mọi lỗi đi qua đúng một khuôn
    /// { message, errors } của dự án, nên controller phải tự kiểm và tự dựng.
    ///
    /// Khoá lỗi đổi sang camelCase bằng JsonNamingPolicy vì tên trường trong ModelState là tên C#
    /// ("TripId"), còn frontend gắn lỗi theo tên trường trong JSON ("tripId") — cùng lối
    /// FeedbackSubmissionController.
    /// </summary>
    private IActionResult ValidationError() => BadRequest(new
    {
        message = "Dữ liệu đầu vào không hợp lệ",
        errors = ModelState.Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray()),
    });
}
