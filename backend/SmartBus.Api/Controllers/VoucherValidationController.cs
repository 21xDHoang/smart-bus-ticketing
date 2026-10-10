using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.Vouchers;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Kiểm tra voucher cho một đơn hàng (POST /api/vouchers/validate) — dòng 52 + 53, US 18,
/// Nguyễn Duy Kiên. Hợp đồng đầy đủ ở mục "Voucher — /vouchers" của docs/api-contract.md.
///
/// [Authorize] trần (không policy), cùng lối SeatHoldCreateController: hành khách tự kiểm mã cho
/// đơn của mình — RBAC của dự án chỉ có AdminOnly/ManagerOrAbove, không có policy Passenger. Không
/// đọc claim người dùng vì phép kiểm này KHÔNG phụ thuộc ai đang hỏi: cùng một mã, cùng một chuyến,
/// cùng một số tiền thì câu trả lời như nhau cho mọi người. (Luật "mỗi khách một lượt" chưa được
/// chốt — khi chốt thì đây là chỗ phải thêm tham số người dùng, xem "Câu hỏi mở" của hợp đồng.)
///
/// Đứng ở file riêng trên bề mặt api/vouchers: bốn endpoint CRUD của dòng 51 (Trần Trung Hiếu) dùng
/// chung route base — <c>validate</c> là một đoạn đường dẫn cố định, <c>{id}</c> là tham số, nên
/// không tranh chấp. Mỗi task một controller, cùng lối api/seat-holds và api/trips.
/// </summary>
[ApiController]
[Route("api/vouchers")]
[Authorize]
public class VoucherValidationController : ControllerBase
{
    private readonly IVoucherValidationService _voucherValidationService;

    public VoucherValidationController(IVoucherValidationService voucherValidationService)
        => _voucherValidationService = voucherValidationService;

    /// <summary>
    /// Kiểm tra mã voucher và tính số tiền được giảm — KHÔNG ghi gì xuống CSDL.
    ///
    /// 🔴 Mã không dùng được vẫn trả **200** kèm <c>valid: false</c> + <c>reasonCode</c>, KHÔNG phải
    /// 400/404: đây là endpoint XEM TRƯỚC mà màn thanh toán gọi trong lúc khách gõ mã, nên "mã này
    /// không dùng được" là câu TRẢ LỜI của phép kiểm chứ không phải request hỏng — trả 4xx sẽ buộc
    /// frontend coi mọi lần gõ sai là lỗi hệ thống.
    ///
    /// Không có token → 401; body sai khuôn (thiếu <c>code</c>/<c>tripId</c>) → 400 kèm
    /// <c>errors.&lt;field&gt;</c>; chuyến không tồn tại → 404 (lỗi phía GỌI, khác hẳn voucher bị
    /// từ chối); còn lại → 200.
    /// </summary>
    [HttpPost("validate")]
    [ProducesResponseType(typeof(VoucherValidationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Validate(
        [FromBody] ValidateVoucherRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _voucherValidationService.ValidateAsync(request, cancellationToken);

        // Cả hai nhánh của phép kiểm đều là 200 với ServiceResult.Ok — nhánh "mã không dùng được"
        // cũng nằm trong Data, không phải Error. Chỉ chuyến không tồn tại mới rơi xuống Failure.
        return result.Success
            ? Ok(result.Data)
            : Failure(result);
    }

    // Cùng một hàm ở SeatHoldCreateController, SeatHoldLookupController, FeedbackSubmissionController,
    // RouteTripsController, RoutesController, FaresController, TripSearchController…
    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };

    /// <summary>
    /// Body sai khuôn (thiếu mã, thiếu chuyến, số tiền ngoài khoảng…) — Program.cs đã tắt filter
    /// validate tự động của ASP.NET Core (SuppressModelStateInvalidFilter) để mọi lỗi đi qua đúng
    /// một khuôn { message, errors } của dự án, nên controller phải tự kiểm và tự dựng.
    ///
    /// Khoá lỗi đổi sang camelCase bằng JsonNamingPolicy vì tên trường trong ModelState là tên C#
    /// ("TripId"), còn frontend gắn lỗi theo tên trường trong JSON ("tripId") — cùng lối
    /// SeatHoldCreateController.
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
