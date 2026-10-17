using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.WebPush;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Web Push (PWA) — thiết bị trình duyệt nhận thông báo hệ điều hành khi tab đã đóng: GET khoá
/// VAPID, đăng ký/liệt kê/gỡ thiết bị, gửi tin thử. Task *"Tích hợp Web Push (PWA) để nhận thông
/// báo trên trình duyệt"* (US 10 "Thông báo trạm", Sprint 4 dòng 42 — Trần Trung Hiếu). Hợp đồng
/// đầy đủ ở mục "Web Push — /web-push" của docs/api-contract.md; luồng và cách chạy ở
/// docs/54-tich-hop-web-push-pwa.md.
///
/// Phân quyền: <c>[Authorize]</c> trần, KHÔNG gắn policy vai trò — nhận thông báo trình duyệt là
/// tính năng của HÀNH KHÁCH. Phạm vi "của tôi" nằm ngay trong truy vấn theo UserId (cùng lối
/// /subscriptions, /refunds): không có tham số nào để đọc thiết bị của người khác, và ca "của
/// người khác" trả 404 chứ không phải 403 — không để lộ thiết bị nào đang đăng ký.
///
/// Tên tài nguyên "devices" chứ không phải "subscriptions": bảng Subscriptions đã mang nghĩa theo
/// dõi tuyến/trạm (docs/37), thiết bị push là khái niệm khác — xem doc
/// <see cref="IWebPushSubscriptionStore"/>.
///
/// Ghi nhật ký: POST và DELETE đi qua AuditLogMiddleware (POST → Create với Target là id dòng mới
/// đọc từ body trả về; DELETE → Delete với Target "Devices:{id}"; POST …/test → Create) — đã khai
/// trong BangPhuSong của AuditLogCoverageTests. Target "Devices" là bảng CHƯA tồn tại (store đang
/// trong bộ nhớ, chờ migration của Dăm) — đánh đổi đã ghi ở docs/54 §6.
/// </summary>
[ApiController]
[Route("api/web-push")]
[Authorize]
public class WebPushController : ControllerBase
{
    private readonly IWebPushDeviceService _deviceService;

    public WebPushController(IWebPushDeviceService deviceService)
        => _deviceService = deviceService;

    /// <summary>
    /// Khoá công khai VAPID của máy chủ — trình duyệt truyền làm <c>applicationServerKey</c> cho
    /// <c>PushManager.subscribe()</c>. Cùng một khoá cho mọi thiết bị của mọi người dùng.
    /// </summary>
    [HttpGet("vapid-public-key")]
    [ProducesResponseType(typeof(VapidPublicKeyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult VapidPublicKey()
        => Ok(new VapidPublicKeyResponse { PublicKey = _deviceService.GetVapidPublicKey() });

    /// <summary>
    /// Danh sách thiết bị đã đăng ký của chính người gọi, mới nhất trước. Chưa đăng ký thiết bị
    /// nào → mảng rỗng.
    /// </summary>
    [HttpGet("devices")]
    [ProducesResponseType(typeof(List<WebPushDeviceResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult ListDevices()
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return MissingCurrentUser();
        }

        return Ok(_deviceService.ListMine(userId));
    }

    /// <summary>
    /// Đăng ký (hoặc đăng ký lại) một thiết bị. Thiết bị mới → 201 kèm dòng vừa tạo; đăng ký lại
    /// đúng endpoint đã có → 200 kèm dòng đã ghi đè (trình duyệt gọi lại mỗi lần mở app, không
    /// phải lỗi trùng).
    ///
    /// Không có token → 401; thiếu trường hoặc endpoint không phải https / khoá sai độ dài → 400
    /// theo từng trường.
    /// </summary>
    [HttpPost("devices")]
    [ProducesResponseType(typeof(WebPushDeviceResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(WebPushDeviceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RegisterDevice(
        [FromBody] RegisterWebPushDeviceRequest request,
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

        var result = _deviceService.Register(userId, request, Request.Headers.UserAgent.ToString());

        if (!result.Success)
        {
            return Failure(result);
        }

        return result.Data!.Created
            ? StatusCode(StatusCodes.Status201Created, result.Data.Device)
            : Ok(result.Data.Device);
    }

    /// <summary>
    /// Gửi tin thử tới đúng một thiết bị — nút "Gửi thông báo thử" của màn cài đặt, để khách kiểm
    /// chứng thiết bị nhận được mà không chờ thông báo thật. Thiết bị không tồn tại hoặc của
    /// người khác → 404 (một câu). Kênh gửi không bao giờ ném lỗi (hợp đồng IWebPushSender) nên
    /// 204 chỉ nghĩa là "đã chuyển cho push service", không hứa thông báo đã hiện.
    /// </summary>
    [HttpPost("devices/{id:guid}/test")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SendTest(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return MissingCurrentUser();
        }

        var result = await _deviceService.SendTestAsync(userId, id, cancellationToken);

        return result.Success ? NoContent() : Failure(result);
    }

    /// <summary>
    /// Gỡ thiết bị — XOÁ khỏi store (không có cờ nào để lật, cùng lối huỷ theo dõi docs/37 §3.2).
    /// Frontend cũng gọi <c>pushSubscription.unsubscribe()</c> phía trình duyệt cho dứt khoát.
    ///
    /// Không có token → 401; thiết bị không tồn tại hoặc của người khác → 404 (một câu).
    /// </summary>
    [HttpDelete("devices/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult RemoveDevice(Guid id)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return MissingCurrentUser();
        }

        var result = _deviceService.Remove(userId, id);

        return result.Success ? NoContent() : Failure(result);
    }

    /// <summary>
    /// Người gọi lấy từ claim NameIdentifier — cùng hàm của SubscriptionsController,
    /// MyNotificationsController, MyTicketsController, SeatHoldLookupController,
    /// MonthlyPassLookupController, AdminUserController và DriversController. Cố ý chép lại thay vì
    /// tách lớp dùng chung: tách ra thì phải sửa controller của người khác, mà luật nhóm không
    /// cho. Các bản giống nhau là cái giá rẻ hơn.
    /// </summary>
    private bool TryGetCurrentUserId(out Guid userId)
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private IActionResult MissingCurrentUser()
        => Unauthorized(new { message = "Không xác định được người dùng đang đăng nhập." });

    // Cùng một hàm ở SubscriptionsController, TripLocationController, AuthController,
    // AdminUserController, FaresController, RoutesController, StopsController, RouteStopsController,
    // RouteTripsController, TripsController và TripAssignmentController. Cố ý chép lại thay vì tách
    // thành lớp dùng chung: tách ra thì phải sửa controller của người khác, mà luật nhóm không
    // cho. Các bản giống nhau là cái giá rẻ hơn.
    private IActionResult ValidationError() => BadRequest(new
    {
        message = "Dữ liệu đầu vào không hợp lệ",
        errors = ModelState.Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray()),
    });

    // Cùng một hàm ở SubscriptionsController, MyTicketsController, SeatHoldLookupController,
    // TripSearchController, RoutesController, FaresController và VoucherValidationController —
    // chép lại cố ý, cùng lý do trên.
    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };
}
