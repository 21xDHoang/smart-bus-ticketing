using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.NotificationTemplates;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Quản trị mẫu thông báo — GET /api/notification-templates và PUT /api/notification-templates/{type}.
/// US 10 "Thông báo trạm", Sprint 5, task *"Màn hình Admin quản lý mẫu thông báo"* — Dương Thị Hạnh.
/// Hợp đồng đầy đủ ở mục "Màn quản lý mẫu thông báo — /notification-templates" của
/// docs/api-contract.md.
///
/// [Authorize(Policy = ManagerOrAbove)]: sửa câu chữ thông báo là việc của quản lý/vận hành — cùng
/// quyền VouchersController (màn quản lý voucher) và khác hẳn MyNotificationsController
/// ([Authorize] trần cho hành khách tự đọc thông báo của mình).
///
/// PUT dùng <c>{type}</c> (tên enum) làm khoá chứ không phải <c>{id}</c>: khoá nghiệp vụ của bảng
/// mẫu ĐÃ là Type — mỗi loại tối đa một mẫu (docs/37 §2), nên lưu là upsert theo type. Hệ quả đã biết:
/// AuditLogMiddleware không nhận <c>{type}</c> là <c>{id}</c> nên dòng nhật ký cho lượt PUT ghi
/// Target "NotificationTemplates" KHÔNG kèm id — đủ biết "có người sửa mẫu lúc nào", chấp nhận được.
/// </summary>
[ApiController]
[Route("api/notification-templates")]
[Authorize(Policy = RbacPolicies.ManagerOrAbove)]
public class NotificationTemplatesController : ControllerBase
{
    private readonly INotificationTemplateService _notificationTemplateService;

    public NotificationTemplatesController(INotificationTemplateService notificationTemplateService)
        => _notificationTemplateService = notificationTemplateService;

    /// <summary>
    /// Danh sách mẫu đang tồn tại, sắp theo thứ tự khai báo NotificationType. Chưa có mẫu nào → mảng
    /// rỗng (loại chưa soạn không xuất hiện — frontend tự vẽ).
    ///
    /// Không có token → 401; thiếu quyền → 403.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<NotificationTemplateResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
        => Ok(await _notificationTemplateService.ListAsync(cancellationToken));

    /// <summary>
    /// Soạn hoặc sửa mẫu cho một loại — upsert theo <c>{type}</c> (tên enum). Chưa có mẫu cho loại đó
    /// thì tạo, có rồi thì sửa Title/Body/IsActive.
    ///
    /// Loại không tồn tại → 404; body sai khuôn → 400 kèm <c>errors.&lt;field&gt;</c>; chỗ trống
    /// không hợp lệ → 400 kèm <c>errors.title</c>/<c>errors.body</c>; hai lượt soạn cùng loại song
    /// song → 409.
    /// </summary>
    [HttpPut("{type}")]
    [ProducesResponseType(typeof(NotificationTemplateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Upsert(
        string type,
        [FromBody] UpsertNotificationTemplateRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        // Parse tên enum từ đường dẫn — phân biệt hoa thường. Tên sai → 404 như một "tài nguyên loại
        // thông báo" không tồn tại, không phải 400: type là khoá định danh, không phải dữ liệu form.
        if (!Enum.TryParse<NotificationType>(type, out var notificationType))
        {
            return NotFound(new { message = "Không tìm thấy loại thông báo." });
        }

        var result = await _notificationTemplateService.UpsertAsync(
            notificationType,
            request,
            cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>
    /// Body sai khuôn — Program.cs đã tắt filter validate tự động của ASP.NET Core để mọi lỗi đi qua
    /// đúng một khuôn { message, errors } của dự án, nên controller phải tự kiểm và tự dựng. Khoá lỗi
    /// đổi sang camelCase vì tên trường trong ModelState là tên C# ("Title"), còn frontend gắn lỗi theo
    /// tên trường JSON ("title") — cùng lối VouchersController.
    /// </summary>
    private IActionResult ValidationError() => BadRequest(new
    {
        message = "Dữ liệu đầu vào không hợp lệ",
        errors = ModelState.Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray()),
    });

    // Cùng một hàm ở VouchersController, SubscriptionsController, AdminUserController… — chép lại cố
    // ý: tách lớp dùng chung thì phải sửa controller của người khác, mà luật nhóm không cho.
    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };
}
