namespace SmartBus.Api.Dtos.Notifications;

/// <summary>
/// Một thông báo trong ứng dụng trả về cho chính người nhận — GET /api/notifications/me
/// (US 10 *"Thông báo trạm"*, task *"API lịch sử thông báo đã nhận của hành khách"*,
/// dòng 19 Sprint 5 — Vàng Thị Dăm). Hợp đồng đầy đủ ở mục "Thông báo trong ứng dụng — /notifications"
/// của docs/api-contract.md.
///
/// Bảy trường giữ NGUYÊN XI interface <c>AppNotification</c> trong
/// <c>frontend/src/api/notificationApi.ts</c> — bản nháp của Nguyễn Đình Băng đã ở trên <c>main</c>
/// (commit <c>5be51ed</c>; chuông + badge ở <c>NotificationBell.tsx</c> đã gọi
/// <c>notificationApi.listMine()</c>). FE đã code
/// xong cách vẽ dựa trên đúng bộ tên này, đổi một chữ là vỡ màn hình mà không ai thấy lỗi biên dịch
/// (TypeScript của họ không đọc được file này) — cùng lý do đã ghi ở <c>RefundResponse</c>.
///
/// <see cref="Type"/> và <see cref="RelatedEntityType"/> là CHUỖI chứ không phải enum: dự án không bật
/// <c>JsonStringEnumConverter</c> nên enum tuôn ra JSON thành con số (bẫy đã ghi ở <c>RefundResponse</c>
/// và <c>TicketResponse</c>) — service <c>ToString()</c> trong bộ nhớ rồi mới đưa vào DTO.
///
/// Cố ý KHÔNG có trường "đã đọc": bảng <c>Notifications</c> chưa có cột đó, và lý do hoãn đã ghi ở
/// doc-comment của entity <c>Notification</c> — chưa ai tiêu thụ trạng thái ấy, hình dạng đúng của nó
/// phụ thuộc badge của Băng (dòng 22). Thêm trường ở đây trước khi có cột là hứa một thứ backend không
/// có.
/// </summary>
public class NotificationResponse
{
    public Guid Id { get; set; }

    /// <summary>
    /// Một trong sáu giá trị của <c>Entities.NotificationType</c>: <c>CancellationApproved</c>,
    /// <c>CancellationRejected</c>, <c>TicketChangeApproved</c>, <c>TicketChangeRejected</c>,
    /// <c>RefundSucceeded</c>, <c>RefundFailed</c>. Quyết định nhãn và biểu tượng ở màn danh sách.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Dòng tiêu đề ngắn — dòng đầu của mỗi mục trong danh sách.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Nội dung đầy đủ, có thể xuống dòng. A8.5 chốt thông báo phải ĐỦ ĐỂ ĐỌC MỘT MÌNH (lưu lại để
    /// khách xem khi offline), nên trường này đi kèm ngay trong danh sách chứ không nằm sau một
    /// endpoint chi tiết — lý do đầy đủ ở hợp đồng, mục "Vì sao không có GET /notifications/{id}".
    /// </summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// Bảng mà <see cref="RelatedEntityId"/> trỏ tới — <c>CancellationRequest</c>, <c>TicketChange</c>
    /// hoặc <c>Refund</c> (<c>Entities.NotificationTarget</c>). Null với thông báo không gắn bản ghi
    /// nào, ví dụ thông báo hệ thống của Sprint 5 (bảo trì, đổi chính sách).
    /// </summary>
    public string? RelatedEntityType { get; set; }

    /// <summary>
    /// Id bản ghi mà thông báo nói về — màn danh sách bấm vào một dòng thì mở màn tương ứng với
    /// <see cref="RelatedEntityType"/>. Null cùng lúc với <see cref="RelatedEntityType"/>.
    /// </summary>
    public Guid? RelatedEntityId { get; set; }

    /// <summary>Thời điểm thông báo được sinh (UTC) — cũng là cột sắp xếp của danh sách.</summary>
    public DateTime CreatedAt { get; set; }
}
