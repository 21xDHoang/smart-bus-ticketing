using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Ghi một dòng vào bảng nhật ký hoạt động (US 23) — task story 23, Vàng Thị Dăm.
///
/// Tách khỏi <see cref="AuditLogMiddleware"/> vì middleware chỉ sinh được những hành động suy ra
/// từ HTTP verb (Create/Update/Delete). Nhóm còn lại — <see cref="AuditAction.Login"/>,
/// <see cref="AuditAction.Logout"/>, <see cref="AuditAction.LoginFailed"/> — không gắn với verb nào
/// nên chính luồng xác thực phải gọi thẳng service này (task "Ghi log đăng nhập / đăng xuất /
/// đăng nhập thất bại" của Hiếu). Không có service này thì chỗ đó phải chép lại logic ghi + bắt lỗi.
/// </summary>
public interface IAuditLogService
{
    /// <summary>
    /// Ghi một bản ghi nhật ký.
    ///
    /// KHÔNG bao giờ ném lỗi ra ngoài: mất một dòng nhật ký là thiệt hại nhỏ, còn làm hỏng thao tác
    /// nghiệp vụ của người dùng vì lý do ghi log là thiệt hại lớn hơn hẳn. Lỗi ghi được đẩy vào
    /// ILogger để còn tìm lại được.
    ///
    /// Cũng vì vậy hàm này KHÔNG nhận <c>CancellationToken</c>: bản ghi nhật ký không được mất
    /// chỉ vì client ngắt kết nối ngay sau khi thao tác đã thành công — mà lúc middleware gọi thì
    /// response đã sinh xong, token của request đã có thể bị huỷ.
    /// </summary>
    /// <param name="action">Loại hành động.</param>
    /// <param name="userId">
    /// Người thực hiện. NULL khi không xác định được — đăng nhập thất bại với SĐT không tồn tại,
    /// hoặc hành động do hệ thống tự làm.
    /// </param>
    /// <param name="target">
    /// Đối tượng bị tác động, dạng <c>&lt;Tên bảng&gt;:&lt;Id&gt;</c>.
    /// NULL khi không nhắm vào bản ghi nào (đăng nhập, đăng xuất).
    /// </param>
    /// <param name="ipAddress">Địa chỉ IP người gọi. NULL khi không lấy được.</param>
    Task RecordAsync(AuditAction action, Guid? userId, string? target, string? ipAddress);
}
