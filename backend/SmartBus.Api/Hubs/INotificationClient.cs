namespace SmartBus.Api.Hubs;

/// <summary>
/// Tập sự kiện server đẩy xuống client của <see cref="NotificationHub"/> — khai kiểu mạnh thay vì gọi
/// <c>Clients.Group(...).SendAsync("NotificationReceived", …)</c> bằng chuỗi trần.
///
/// Tên method ở đây CHÍNH LÀ tên sự kiện trong hợp đồng API: frontend đăng ký bằng đúng chuỗi
/// "NotificationReceived", nên đổi tên method này là đổi hình dạng API — phải sửa
/// <c>docs/api-contract.md</c> trước (quy ước 5), cùng lối đã ghi ở <see cref="ISeatMapClient"/>.
/// </summary>
public interface INotificationClient
{
    /// <summary>
    /// Báo cho client rằng tài khoản này VỪA có một thông báo mới. Payload CHỈ có id, KHÔNG kèm tiêu đề
    /// và nội dung — cùng nguyên tắc "hub không bao giờ là nguồn dữ liệu thứ hai" đã chốt cho
    /// <see cref="ISeatMapClient.SeatMapChanged"/>: client nhận id rồi tự gọi lại
    /// <c>GET /api/notifications/me</c> (Sprint 5 — *"API lịch sử thông báo đã nhận của hành khách"*,
    /// dòng 19, Vàng Thị Dăm) để lấy nội dung.
    ///
    /// Vì sao vẫn gửi kèm id dù client lấy được cả danh sách: id cho phép client tô đậm/hiện toast
    /// đúng dòng vừa tới mà không phải so sánh nội dung, và cho phép bỏ qua sự kiện trùng khi client
    /// vừa tự tải danh sách xong. Id là thứ duy nhất client cần mà nó không tự suy ra được.
    /// </summary>
    Task NotificationReceived(Guid notificationId);
}
