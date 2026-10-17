using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.Subscriptions;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Đăng ký / huỷ theo dõi thông báo theo tuyến và trạm — GET/POST/DELETE /api/subscriptions.
/// US 10 "Thông báo trạm", Sprint 5, task *"API đăng ký / huỷ theo dõi thông báo theo tuyến và
/// trạm"* — Trần Trung Hiếu. Hợp đồng đầy đủ ở mục "Theo dõi thông báo — /subscriptions" của
/// docs/api-contract.md.
///
/// Phân quyền: <c>[Authorize]</c> trần, KHÔNG gắn policy vai trò — theo dõi thông báo trạm là
/// tính năng của HÀNH KHÁCH. Phạm vi "của tôi" nằm ngay trong truy vấn theo
/// <c>Subscriptions.UserId</c> (cùng lối /refunds, /tickets/me): không có tham số nào để đọc
/// lượt theo dõi của người khác, và ca "của người khác" trả 404 chứ không phải 403 — không để
/// lộ lượt theo dõi nào đang tồn tại.
///
/// Ghi nhật ký: POST và DELETE đi qua AuditLogMiddleware (POST → Create với Target là id dòng
/// mới đọc từ body trả về; DELETE → Delete với Target "Subscriptions:{id}") — đã khai trong
/// BangPhuSong của AuditLogCoverageTests. ⚠️ Tên tham số đường dẫn phải đúng là <c>{id}</c>:
/// middleware suy tên bảng từ đoạn đứng ngay trước tham số đó.
/// </summary>
[ApiController]
[Route("api/subscriptions")]
[Authorize]
public class SubscriptionsController : ControllerBase
{
    private readonly ISubscriptionService _subscriptionService;

    public SubscriptionsController(ISubscriptionService subscriptionService)
        => _subscriptionService = subscriptionService;

    /// <summary>
    /// Danh sách lượt theo dõi của chính người gọi, mới nhất trước. Chưa theo dõi gì → mảng rỗng.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<SubscriptionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ListMine(CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return MissingCurrentUser();
        }

        return Ok(await _subscriptionService.ListMineAsync(userId, cancellationToken));
    }

    /// <summary>
    /// Đăng ký theo dõi một trạm — hoặc cả tuyến khi <c>stopId</c> bỏ trống.
    ///
    /// Không có token → 401; tuyến không tồn tại → 404; trạm không tồn tại → 404; trạm không
    /// thuộc tuyến → 400 theo trường stopId; đã theo dõi đúng tuyến/trạm này rồi → 409.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(SubscriptionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Subscribe(
        [FromBody] CreateSubscriptionRequest request,
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

        var result = await _subscriptionService.SubscribeAsync(userId, request, cancellationToken);

        return result.Success
            ? CreatedAtAction(nameof(ListMine), result.Data)
            : Failure(result);
    }

    /// <summary>
    /// Huỷ theo dõi — XOÁ dòng, không lật cờ (docs/37 §3.2).
    ///
    /// Không có token → 401; lượt theo dõi không tồn tại hoặc thuộc người khác → 404 (cùng một câu).
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Unsubscribe(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return MissingCurrentUser();
        }

        var result = await _subscriptionService.UnsubscribeAsync(userId, id, cancellationToken);

        return result.Success ? NoContent() : Failure(result);
    }

    /// <summary>
    /// Người gọi lấy từ claim NameIdentifier — cùng hàm của TripLocationController,
    /// MyTicketsController, SeatHoldLookupController, MonthlyPassLookupController, AdminUserController
    /// và DriversController. Cố ý chép lại thay vì tách lớp dùng chung: tách ra thì phải sửa
    /// controller của người khác, mà luật nhóm không cho. Các bản giống nhau là cái giá rẻ hơn.
    /// </summary>
    private bool TryGetCurrentUserId(out Guid userId)
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private IActionResult MissingCurrentUser()
        => Unauthorized(new { message = "Không xác định được người dùng đang đăng nhập." });

    // Cùng một hàm ở TripLocationController, AuthController, AdminUserController, FaresController,
    // RoutesController, StopsController, RouteStopsController, RouteTripsController, TripsController
    // và TripAssignmentController. Cố ý chép lại thay vì tách thành lớp dùng chung: tách ra thì
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

    // Cùng một hàm ở TripLocationController, MyTicketsController, SeatHoldLookupController,
    // TripSearchController, RoutesController, FaresController và VoucherValidationController —
    // chép lại cố ý, cùng lý do trên.
    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };
}
