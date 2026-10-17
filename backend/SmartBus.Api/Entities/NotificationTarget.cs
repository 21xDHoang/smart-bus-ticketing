namespace SmartBus.Api.Entities;

/// <summary>
/// Bảng mà <see cref="Notification.RelatedEntityId"/> đang trỏ tới — một con trỏ ĐA HÌNH, nên không
/// thể là khoá ngoại thật (một cột không trỏ được vào hai bảng). Lưu dạng chuỗi trong CSDL (A3).
///
/// Vì sao cần cột này chứ không chỉ <see cref="Notification.RelatedEntityId"/> trần: màn danh sách
/// thông báo (Sprint 5 — "Hiển thị danh sách thông báo trong ứng dụng" của Nguyễn Đình Băng) phải
/// biết bấm vào một dòng thì mở màn nào. Với tám giá trị của <see cref="NotificationType"/> hiện tại
/// thì loại thông báo đã nói được điều đó, nhưng Sprint 5 sẽ có loại trỏ vào bảng khác hẳn
/// (<c>Incidents</c> cho sự cố) — lúc đó chỉ nhìn <see cref="Notification.Type"/> là không đủ, và
/// thêm cột sau tốn một migration.
///
/// Giá trị đặt theo TÊN ENTITY (A2 — số ít, PascalCase), không theo tên bảng: đây là tên kiểu dữ
/// liệu mà frontend ánh xạ sang đường dẫn màn hình, không phải tên bảng CSDL.
///
/// ⚠️ Cố ý KHÔNG dùng lối <c>"&lt;Tên bảng&gt;:&lt;Id&gt;"</c> của <see cref="AuditLog.Target"/>:
/// bảng nhật ký là chuỗi để ĐỌC bằng mắt trên màn Admin (US 23), còn đây là dữ liệu cho MÁY định
/// tuyến — tách thành hai cột có kiểu thì frontend không phải cắt chuỗi, và không có cách nào ghi ra
/// một chuỗi sai định dạng mà vẫn lưu được.
/// </summary>
public enum NotificationTarget
{
    /// <summary>Trỏ tới một dòng <c>CancellationRequests</c> — yêu cầu huỷ vé (US 5).</summary>
    CancellationRequest,

    /// <summary>Trỏ tới một dòng <c>TicketChanges</c> — yêu cầu đổi chuyến (US 5).</summary>
    TicketChange,

    /// <summary>
    /// Trỏ tới một dòng <c>Refunds</c> — khoản hoàn tiền (US 8). Mang thông báo kết quả cuối do task
    /// dòng 34 phát (hai loại <c>Refund…</c>), màn đến là "Hoàn tiền của tôi" của khách.
    /// </summary>
    Refund,

    /// <summary>
    /// Trỏ tới một dòng <c>Trips</c> — chuyến xe vừa đổi lịch trình (US 10). Mang hai loại
    /// <c>Trip…</c> do task dòng 44 phát; màn đến là chi tiết chuyến / vé của khách. Khác
    /// <c>Refund</c>, một chuyến bị dời giờ nhiều lần sinh nhiều thông báo cùng trỏ về đây.
    /// </summary>
    Trip
}
