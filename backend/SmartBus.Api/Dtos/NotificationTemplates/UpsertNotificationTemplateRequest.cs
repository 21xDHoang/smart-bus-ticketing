using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.NotificationTemplates;

/// <summary>
/// Body của PUT /api/notification-templates/{type} — soạn hoặc sửa mẫu cho một loại thông báo
/// (US 10 "Thông báo trạm", Sprint 5 dòng 24 — Dương Thị Hạnh).
///
/// PUT là UPSERT theo <c>type</c> trên đường dẫn: chưa có mẫu cho loại đó thì tạo, có rồi thì sửa —
/// mỗi loại tối đa MỘT mẫu (chỉ mục duy nhất trên <c>NotificationTemplates.Type</c>, docs/37 §2).
///
/// Attribute chỉ chặn lỗi KHÔNG phụ thuộc trường khác (thiếu, quá dài). Việc kiểm chỗ trống hợp lệ
/// theo từng loại nằm ở tầng service — cần biết <c>type</c> để tra danh sách chỗ trống cho phép, nên
/// không thể là attribute (docs/37 §6.1).
/// </summary>
public class UpsertNotificationTemplateRequest
{
    /// <summary>Dòng tiêu đề. 200 là trần của <c>Notifications.Title</c> — mẫu lưu được mà dài hơn
    /// là một cái bẫy chỉ nổ ở lượt gửi (docs/37 §2).</summary>
    [Required(ErrorMessage = "Tiêu đề mẫu không được để trống")]
    [StringLength(200, ErrorMessage = "Tiêu đề mẫu tối đa 200 ký tự")]
    public string? Title { get; set; }

    /// <summary>Nội dung đầy đủ — <c>text</c>, không đặt trần độ dài (cùng lối <c>Notification.Body</c>).</summary>
    [Required(ErrorMessage = "Nội dung mẫu không được để trống")]
    public string? Body { get; set; }

    /// <summary>
    /// Mẫu có đang dùng hay không. Tắt = quay về câu mặc định trong code, KHÔNG mất chữ đã soạn
    /// (docs/37 §2.2). Frontend luôn gửi giá trị này theo công tắc trong form.
    /// </summary>
    public bool IsActive { get; set; }
}
