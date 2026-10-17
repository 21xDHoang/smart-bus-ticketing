using SmartBus.Api.Dtos.NotificationTemplates;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Quản trị mẫu thông báo — GET/PUT /api/notification-templates (US 10 "Thông báo trạm",
/// Sprint 5 dòng 24 — Dương Thị Hạnh). Hợp đồng đầy đủ ở mục
/// "Màn quản lý mẫu thông báo — /notification-templates" của docs/api-contract.md.
/// </summary>
public interface INotificationTemplateService
{
    /// <summary>
    /// Danh sách mẫu đang tồn tại, sắp theo thứ tự khai báo của <c>NotificationType</c>. Chỉ trả
    /// các dòng GHI ĐÈ — loại chưa có mẫu thì không xuất hiện (frontend tự vẽ chỗ trống).
    /// </summary>
    Task<List<NotificationTemplateResponse>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Soạn hoặc sửa mẫu cho một loại: chưa có dòng cho <paramref name="type"/> thì tạo, có rồi thì
    /// sửa <c>Title</c>/<c>Body</c>/<c>IsActive</c>. Chỗ trống không hợp lệ → kết quả Invalid kèm
    /// <c>errors.title</c>/<c>errors.body</c> (docs/37 §6.1).
    /// </summary>
    Task<ServiceResult<NotificationTemplateResponse>> UpsertAsync(
        NotificationType type,
        UpsertNotificationTemplateRequest request,
        CancellationToken cancellationToken = default);
}
