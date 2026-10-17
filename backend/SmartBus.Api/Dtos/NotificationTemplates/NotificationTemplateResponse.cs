namespace SmartBus.Api.Dtos.NotificationTemplates;

/// <summary>
/// Một mẫu thông báo trả về cho màn quản trị — GET /api/notification-templates (danh sách) và
/// PUT /api/notification-templates/{type} (dòng vừa lưu). (US 10 "Thông báo trạm", Sprint 5 dòng 24 —
/// Dương Thị Hạnh.)
///
/// <see cref="Type"/> là CHUỖI chứ không phải enum: dự án không bật <c>JsonStringEnumConverter</c>
/// nên để nguyên enum trong DTO là JSON tuôn ra con số (bẫy đã ghi ở <c>NotificationResponse</c>) —
/// service <c>ToString()</c> trong bộ nhớ rồi mới đưa vào DTO.
///
/// Mẫu là phần GHI ĐÈ, không phải điều kiện để gửi (docs/37 §2.1): không có dòng cho một loại nào
/// (hoặc dòng đang tắt <see cref="IsActive"/>) thì tầng notifier dùng câu mặc định trong code.
/// </summary>
public class NotificationTemplateResponse
{
    /// <summary>Khoá chính — hiện chỉ để frontend giữ khi sửa lại; thao tác lưu định danh theo Type.</summary>
    public Guid Id { get; set; }

    /// <summary>Loại thông báo mà mẫu soạn cho — một trong tám giá trị của <c>NotificationType</c>.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Dòng tiêu đề của mẫu, tối đa 200 ký tự — cùng độ rộng <c>Notifications.Title</c>.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Nội dung đầy đủ, <c>text</c> — xuống dòng tự do, không đặt trần độ dài.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// Mẫu có đang dùng hay không. Tắt KHÔNG có nghĩa là tắt thông báo — nghĩa là quay về câu mặc
    /// định trong code (docs/37 §2.2).
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>Thời điểm tạo mẫu (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Thời điểm sửa gần nhất (UTC); null với dòng vừa tạo chưa sửa lần nào.</summary>
    public DateTime? UpdatedAt { get; set; }
}
