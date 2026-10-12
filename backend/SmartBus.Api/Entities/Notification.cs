namespace SmartBus.Api.Entities;

/// <summary>
/// Bảng <c>Notifications</c> (A9 #15) — một THÔNG BÁO trong ứng dụng gửi cho một tài khoản.
/// Task Sprint 4 *"Ghi log và thông báo kết quả xử lý cho hành khách"* (Vàng Thị Dăm, dòng 7) là
/// người dùng đầu tiên: quản lý duyệt/từ chối yêu cầu huỷ hoặc đổi vé thì khách nhận một dòng ở đây
/// (nội dung do <c>TicketRequestResultNotifier</c> soạn) và một sự kiện đẩy qua
/// <c>/hubs/notifications</c>.
///
/// Bảng CHỈ GHI THÊM: không có <c>UpdatedAt</c> (quy ước A4 gọi tên đúng bảng này) và dòng đã ghi
/// không sửa. Cùng lối <see cref="AuditLog"/>.
///
/// ⚠️ Vì sao CHƯA có cột "đã đọc": A4 xếp bảng này vào nhóm chỉ-ghi-thêm, còn đánh dấu đã đọc là
/// một phép SỬA. Sprint 5 mới có màn *"Màn hình chi tiết thông báo và đánh dấu đã đọc"* (Dương Thị
/// Hạnh) và *"Hiển thị danh sách thông báo trong ứng dụng (chuông + badge)"* (Nguyễn Đình Băng) —
/// tức là chưa có ai tiêu thụ trạng thái đó, và hình dạng đúng của nó (một cột <c>ReadAt</c> trên
/// dòng thông báo, hay bảng nối theo thiết bị?) phụ thuộc vào badge của Băng. Thêm một cột nullable
/// sau này không tốn migration dữ liệu, còn đoán sai bây giờ thì phải sửa lại kèm migrate.
///
/// ⚠️ Cũng chưa có cột "đã gửi/đã đẩy thành công": sự kiện SignalR là kênh phụ trợ và cố ý nuốt lỗi
/// (xem <c>TicketRequestResultNotifier</c>). Dòng thông báo tồn tại nghĩa là *"nội dung đã được ghi
/// lại cho khách"* — đúng thứ A8.5 cần để khách đọc lại khi offline, không phải biên bản phát sóng.
/// </summary>
public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Người NHẬN thông báo — khác hẳn nghĩa của <see cref="AuditLog.UserId"/> (người THỰC HIỆN
    /// hành động). Ở đây người thực hiện là quản lý duyệt yêu cầu, còn người nhận là hành khách, nên
    /// hai cột không bao giờ được lẫn với nhau. Không nullable: một thông báo không có người nhận thì
    /// không có cách nào hiển thị, và mọi thông báo của dự án đều nhắm vào một tài khoản cụ thể.
    /// </summary>
    public Guid UserId { get; set; }

    public User? User { get; set; }

    /// <summary>Loại thông báo — xem <see cref="NotificationType"/>. Quyết định nhãn và biểu tượng ở màn danh sách.</summary>
    public NotificationType Type { get; set; }

    /// <summary>
    /// Dòng tiêu đề ngắn, <c>varchar(200)</c>. Có cột riêng chứ không nhét vào <see cref="Body"/> vì
    /// màn danh sách chỉ hiển thị tiêu đề: để chung một cột thì mỗi lượt vẽ danh sách phải tải cả
    /// phần thân (có thể vài KB vì <see cref="Body"/> là <c>text</c>), và không có chỗ để cắt ngắn.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Nội dung đầy đủ, <c>text</c> (A3 xếp "mô tả dài" vào text — không đặt trần độ dài, vì lý do từ
    /// chối do quản lý nhập được chép nguyên văn vào đây và nó không có độ dài trần hợp lý).
    ///
    /// 🔴 Nội dung phải ĐỦ ĐỂ ĐỌC MỘT MÌNH: A8.5 chốt *"lưu vào bảng để khách xem lại thông báo khi
    /// offline"*, nên thông báo không được là một câu trỏ về màn hình khác ("bấm để xem chi tiết").
    /// Kết quả xử lý và lý do từ chối nằm thẳng ở đây.
    /// </summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// Bảng mà <see cref="RelatedEntityId"/> trỏ tới — xem <see cref="NotificationTarget"/>. Nullable
    /// vì không phải thông báo nào cũng gắn với một bản ghi: thông báo hệ thống của Sprint 5 (bảo trì,
    /// thay đổi chính sách) không trỏ vào đâu cả.
    /// </summary>
    public NotificationTarget? RelatedEntityType { get; set; }

    /// <summary>
    /// Id bản ghi mà thông báo nói về — với hai giá trị hiện tại là id của dòng
    /// <c>CancellationRequests</c> hoặc <c>TicketChanges</c>. Nullable cùng lý do như
    /// <see cref="RelatedEntityType"/>.
    ///
    /// ⚠️ Cố ý KHÔNG đặt khoá ngoại thật: một cột không trỏ được vào hai bảng. Hệ quả chấp nhận được
    /// vì mọi khoá ngoại nghiệp vụ của dự án đều <c>Restrict</c> (A5) — yêu cầu huỷ/đổi không bị xoá,
    /// nên con trỏ này không mục theo thời gian. Đây cũng là lý do thông báo KHÔNG được dùng làm
    /// nguồn dữ liệu nghiệp vụ: nó là bản sao nội dung tại một thời điểm.
    /// </summary>
    public Guid? RelatedEntityId { get; set; }

    /// <summary>Thời điểm thông báo được sinh (UTC). Cũng là cột sắp xếp của danh sách thông báo.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
