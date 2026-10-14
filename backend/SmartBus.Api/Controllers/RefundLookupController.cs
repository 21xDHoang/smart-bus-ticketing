using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.Refunds;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Trạng thái hoàn tiền của chính người gọi — GET /api/refunds và GET /api/refunds/{refundCode}.
/// Task *"API tra cứu trạng thái hoàn tiền của giao dịch"* (US 8, Sprint 4, dòng 42 — Vàng Thị Dăm).
/// Hợp đồng đầy đủ ở mục "Hoàn tiền — /refunds" của docs/api-contract.md.
///
/// <b>Vì sao KHÔNG có đoạn <c>me</c> trong đường dẫn:</b> mọi khoản hoàn trả về ở đây đều đã là "của
/// tôi" — phạm vi nằm trong truy vấn theo <c>UserId</c> ở tầng service, không có tham số nào để hỏi
/// khoản của người khác. Thêm <c>/refunds/mine</c> sẽ là một đoạn đường dẫn không phân biệt gì cả.
/// Bề mặt quản trị tra khoản của người khác là <c>/api/admin/refunds</c> — tiền tố khác, controller
/// khác. Hệ quả cho frontend: bản nháp <c>myRefundApi.ts</c> đang gọi <c>/refunds/mine</c>, phải sửa
/// thành <c>/refunds</c> (⚠️ đã ghi trong hợp đồng).
///
/// <b>[Authorize] trần (không policy)</b>: RBAC của dự án chỉ có AdminOnly/ManagerOrAbove, không có
/// policy Passenger — cùng lối <see cref="FeedbackLookupController"/> và
/// <see cref="MonthlyPassLookupController"/>. Ca "khoản hoàn của người khác" trả 404 chứ không phải
/// 403: trả 403 là xác nhận mã đó có tồn tại, tức là để lộ thông tin cho người đang dò.
///
/// Đứng ở controller riêng trên cùng bề mặt <c>api/refunds</c> với bề mặt quản trị
/// (<c>api/admin/refunds</c> là tiền tố khác hẳn nên không đụng nhau) — mỗi bề mặt một controller,
/// cùng lối <see cref="FeedbackLookupController"/> đứng chung bề mặt api/feedbacks.
/// </summary>
[ApiController]
[Route("api/refunds")]
[Authorize]
public class RefundLookupController : ControllerBase
{
    private readonly IRefundLookupService _refundLookupService;

    public RefundLookupController(IRefundLookupService refundLookupService)
        => _refundLookupService = refundLookupService;

    /// <summary>
    /// Khoản hoàn của chính người gọi, mới nhất trước; trả 200 kèm MẢNG (có thể rỗng), không phân
    /// trang.
    ///
    /// <paramref name="request"/>.PaymentCode là thứ biến endpoint này thành câu trả lời cho *"trạng
    /// thái hoàn tiền của MỘT GIAO DỊCH"* — đúng câu chữ của task dòng 42. Bốn ca trả về <b>200 kèm
    /// mảng rỗng</b>, KHÔNG phải 404: người gọi chưa có khoản hoàn nào · mã giao dịch không tồn tại ·
    /// giao dịch của người khác · <c>status</c> gõ sai. Không có token → 401; <c>paymentCode</c> dài
    /// quá 64 ký tự hoặc <c>status</c> dài quá 20 ký tự → 400.
    ///
    /// 🔴 Hệ quả có chủ ý: endpoint này <b>không dùng được để dò xem một mã giao dịch có tồn tại hay
    /// không</b> — "không có khoản hoàn nào" và "giao dịch không tồn tại" trả lời giống hệt nhau. Đó
    /// là điều kiện để không ai đọc được giao dịch của người khác qua đây.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetMine(
        [FromQuery] ListMyRefundsRequest request,
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

        var refunds = await _refundLookupService.ListMineAsync(userId, request, cancellationToken);

        return Ok(refunds);
    }

    /// <summary>
    /// Một khoản hoàn theo mã hoàn tiền (<c>RF-xxxxxxxx</c>) — chỉ khi khoản đó thuộc người gọi.
    /// Không tồn tại, hoặc thuộc người khác → 404 với <b>cùng một câu</b>, không phân biệt.
    ///
    /// Không có nhánh 400: mã hoàn tiền là chuỗi tự do tra thẳng vào cột unique <c>RefundCode</c>,
    /// gõ sai chỉ có nghĩa là không tìm thấy. Đây là điểm khác với <c>paymentCode</c> ở endpoint danh
    /// sách — chỗ đó chặn độ dài vì mã dài hơn cột là mã không thể tồn tại.
    /// </summary>
    [HttpGet("{refundCode}")]
    public async Task<IActionResult> GetByRefundCode(string refundCode, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return MissingCurrentUser();
        }

        var result = await _refundLookupService.GetByRefundCodeAsync(
            userId, refundCode, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>
    /// Người gọi lấy từ claim NameIdentifier — cùng hàm của <see cref="FeedbackLookupController"/>,
    /// <see cref="MonthlyPassLookupController"/>, <see cref="MyTicketsController"/>. Cả hai endpoint
    /// đều cần: userId chính là phạm vi truy vấn, không phải chỉ để ghi tác giả.
    /// </summary>
    private bool TryGetCurrentUserId(out Guid userId)
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private IActionResult MissingCurrentUser()
        => Unauthorized(new { message = "Không xác định được người dùng đang đăng nhập." });

    /// <summary>
    /// Chỉ có nhánh NotFound là tới được đây: service không bao giờ trả Invalid/Conflict (độ dài tham
    /// số do DTO chặn ở controller, còn mã trạng thái lạ thì trả mảng rỗng chứ không phải lỗi) — hai
    /// nhánh còn lại giữ nguyên cho khớp khuôn Failure của dự án và để sau này thêm ca lỗi không phải
    /// sửa cấu trúc.
    /// </summary>
    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };

    // Cùng một hàm ở RouteTripsController, RoutesController, FaresController, TripLookupController,
    // TripSearchController, TripAssignmentController, MonthlyPassRenewalController,
    // FeedbackAdminController và FeedbackLookupController. Cố ý chép lại thay vì tách thành lớp dùng
    // chung: tách ra thì phải sửa cả controller của người khác, mà luật nhóm không cho sửa file của
    // người khác.
    private IActionResult ValidationError() => BadRequest(new
    {
        message = "Dữ liệu đầu vào không hợp lệ",
        errors = ModelState.Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray()),
    });
}
