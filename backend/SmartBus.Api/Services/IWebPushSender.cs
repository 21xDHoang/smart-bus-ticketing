namespace SmartBus.Api.Services;

/// <summary>
/// Chỗ duy nhất các service nghiệp vụ gọi để ĐẨY Web Push cho một người dùng — người tiêu thụ
/// dự kiến: service báo xe cách trạm &lt;500m (Kiên, dòng 15 Sprint 5), service đẩy sự cố theo
/// tuyến (Kiên, dòng 29), notifier kết quả xử lý huỷ/đổi vé và hoàn tiền khi nhóm nối kênh mới.
///
/// 🔴 Hợp đồng KHÔNG-BAO-GIỜ-NÉM-LỖI (cùng lối <c>ITicketRequestResultNotifier</c>): một thiết
/// bị hỏng, hết pin hay push service nghẽn KHÔNG được kéo sập nghiệp vụ chính — tin trong ứng
/// dụng đã ghi bảng Notifications, Web Push chỉ là kênh phụ trợ. Mọi lỗi bị nuốt kèm log; riêng
/// subscription bị push service báo chết (404/410) được DỌN khỏi store để lần sau không gửi lại.
/// </summary>
public interface IWebPushSender
{
    /// <summary>
    /// Gửi một tin tới MỌI thiết bị của người dùng (một khách đăng ký trên ba trình duyệt thì nhận
    /// đủ ba). Người dùng không có thiết bị nào là no-op thành công — không phải lỗi.
    /// </summary>
    Task SendToUserAsync(Guid userId, WebPushMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gửi tin tới đúng MỘT thiết bị — cho nút "Gửi thông báo thử" của màn cài đặt. Trả <c>false</c>
    /// khi thiết bị không tồn tại hoặc không phải của <paramref name="userId"/> (một câu cho cả hai
    /// ca, cùng lối "404 chung" của dự án).
    /// </summary>
    Task<bool> SendToDeviceAsync(Guid userId, Guid deviceId, WebPushMessage message, CancellationToken cancellationToken = default);
}
