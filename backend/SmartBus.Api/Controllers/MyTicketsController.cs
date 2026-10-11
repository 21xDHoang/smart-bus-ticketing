using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Vé điện tử của chính người gọi — GET /api/tickets/me và GET /api/tickets/{id}. Task *"API tra cứu
/// vé của tôi (đã mua / đã dùng / đã huỷ)"* (dòng 45, US 4, Sprint 3, Trần Trung Hiếu). Hợp đồng đầy
/// đủ ở mục "Vé điện tử" của docs/api-contract.md.
///
/// [Authorize] trần (không policy): hành khách tự xem vé của mình — RBAC của dự án chỉ có
/// AdminOnly/ManagerOrAbove, không có policy Passenger. Phạm vi "của tôi" nằm ở truy vấn theo
/// UserId trong service: vé của người khác trả 404, không có tham số nào để dò vé người khác.
///
/// Đứng ở controller riêng cho bề mặt api/tickets: các task soát vé của Sprint sau (POST
/// /tickets/validate…) sẽ là controller khác trên cùng bề mặt — mỗi task một controller, cùng lối
/// api/seat-holds và api/vouchers.
/// </summary>
[ApiController]
[Route("api/tickets")]
[Authorize]
public class MyTicketsController : ControllerBase
{
    private readonly IMyTicketsService _myTicketsService;

    public MyTicketsController(IMyTicketsService myTicketsService)
        => _myTicketsService = myTicketsService;

    /// <summary>
    /// Danh sách vé của chính người gọi — mọi trạng thái (Paid / Used / Cancelled), mảng trần,
    /// không phân trang, không hứa thứ tự (hợp đồng mục "GET /tickets/me").
    ///
    /// Không có token → 401; không có vé nào → 200 kèm mảng rỗng. Không có nhánh lỗi nào khác.
    /// </summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(IReadOnlyList<Dtos.Tickets.TicketResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return MissingCurrentUser();
        }

        var tickets = await _myTicketsService.ListAsync(userId, cancellationToken);

        return Ok(tickets);
    }

    /// <summary>
    /// Một vé theo id — chỉ khi vé thuộc chính người gọi. Màn "Mã QR vé" mở trực tiếp một vé trong
    /// danh sách bằng id trên URL.
    ///
    /// Không có token → 401; vé không tồn tại hoặc của người khác → 404; id không phải GUID → 404
    /// do ràng buộc {id:guid}.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Dtos.Tickets.TicketResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return MissingCurrentUser();
        }

        var result = await _myTicketsService.GetByIdAsync(userId, id, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>
    /// Người gọi lấy từ claim NameIdentifier — cùng hàm của SeatHoldLookupController,
    /// MonthlyPassLookupController, AdminUserController và DriversController. Cố ý chép lại thay vì
    /// tách lớp dùng chung: tách ra thì phải sửa cả controller của người khác, mà luật nhóm không
    /// cho sửa file của người khác. Các bản giống nhau là cái giá rẻ hơn.
    /// </summary>
    private bool TryGetCurrentUserId(out Guid userId)
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private IActionResult MissingCurrentUser()
        => Unauthorized(new { message = "Không xác định được người dùng đang đăng nhập." });

    // Cùng một hàm ở SeatHoldLookupController, TripSearchController, RoutesController,
    // FaresController và VoucherValidationController — chép lại cố ý, cùng lý do trên.
    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };
}
