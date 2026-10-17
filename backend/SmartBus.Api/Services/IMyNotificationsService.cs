using SmartBus.Api.Dtos.Notifications;

namespace SmartBus.Api.Services;

/// <summary>
/// Lịch sử thông báo trong ứng dụng của chính người gọi — GET /api/notifications/me (US 10
/// *"Thông báo trạm"*, task *"API lịch sử thông báo đã nhận của hành khách"*, dòng 19 Sprint 5 —
/// Vàng Thị Dăm). Hợp đồng đầy đủ ở mục "Thông báo trong ứng dụng — /notifications" của
/// docs/api-contract.md.
///
/// Giao diện CỐ Ý không có tham số nào để hỏi thông báo của người khác: hàm duy nhất nhận
/// <c>userId</c> của người đang đăng nhập và dùng nó làm điều kiện truy vấn — cùng lối
/// <see cref="IRefundLookupService"/> và <see cref="IMyTicketsService"/>. Chưa có màn quản trị nào
/// xem thông báo của người khác trong bảng phân công, nên chưa dựng bề mặt <c>/admin/notifications</c>.
/// </summary>
public interface IMyNotificationsService
{
    /// <summary>
    /// Thông báo của một người, mới nhất trước — mảng trần, không phân trang. Chưa nhận thông báo nào
    /// thì mảng RỖNG, không phải lỗi: *"tôi chưa có thông báo nào"* là một câu trả lời bình thường
    /// (mọi tài khoản vừa đăng ký đều ở trạng thái đó), nên endpoint này không có nhánh 404.
    /// </summary>
    Task<IReadOnlyList<NotificationResponse>> ListMineAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
