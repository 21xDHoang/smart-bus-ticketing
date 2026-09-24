namespace SmartBus.Api.Entities;

/// <summary>
/// Bảng AuditLogs — nhật ký truy cập và thao tác hệ thống (US 23), phục vụ kiểm toán.
///
/// Bảng CHỈ GHI THÊM: không có UpdatedAt (quy ước A4) và không sửa/xoá bản ghi —
/// sửa được nhật ký thì nhật ký mất giá trị kiểm toán.
///
/// Task migrate bảng này là của Vàng Thị Dăm (story 23).
/// </summary>
public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Tài khoản thực hiện hành động. NULL khi không xác định được người thực hiện:
    /// đăng nhập thất bại với SĐT không tồn tại, hoặc hành động do hệ thống tự làm.
    /// </summary>
    public Guid? UserId { get; set; }

    public User? User { get; set; }

    /// <summary>Loại hành động. Lưu dạng chuỗi trong CSDL — xem quy ước A3.</summary>
    public AuditAction Action { get; set; }

    /// <summary>
    /// Đối tượng bị tác động, ghi dạng <c>&lt;Tên bảng&gt;:&lt;Id&gt;</c> —
    /// ví dụ <c>"Routes:3f2a1b0c-…"</c>, <c>"Users:8c1d4e77-…"</c>.
    /// NULL với hành động không nhắm vào bản ghi nào (đăng nhập, đăng xuất).
    /// </summary>
    public string? Target { get; set; }

    /// <summary>
    /// Địa chỉ IP của người gọi. NULL khi không lấy được — sau proxy hoặc kết nối nội bộ
    /// có thể không có IP nguồn, và mất IP không phải lý do để mất cả bản ghi nhật ký.
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>Thời điểm hành động xảy ra (UTC) — cột "thời gian" của task này.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
