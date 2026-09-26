namespace SmartBus.Api.Dtos.AuditLogs;

/// <summary>
/// Một bản ghi nhật ký kiểm toán trả về cho GET /api/audit-logs — khớp mục "Nhật ký kiểm toán"
/// của docs/api-contract.md.
///
/// Sáu trường đầu khớp 1-1 với interface <c>AuditLog</c> mà frontend đã khai sẵn
/// (frontend/src/api/auditLogApi.ts) — màn hình danh sách của Hạnh và modal chi tiết dùng chung
/// kiểu đó, nên đổi tên trường ở đây là đổi hình dạng API: sửa api-contract.md trước.
/// </summary>
public class AuditLogResponse
{
    public Guid Id { get; set; }

    /// <summary>
    /// Người thao tác. <c>null</c> khi không xác định được — đăng nhập thất bại với SĐT không tồn
    /// tại, hoặc hành động do hệ thống tự làm.
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// Họ tên người thao tác, ghép từ bảng Users — CHỈ để hiển thị, không phải trường của
    /// AuditLogs. <c>null</c> khi <see cref="UserId"/> là <c>null</c>.
    /// </summary>
    public string? UserFullName { get; set; }

    /// <summary>
    /// SĐT người thao tác, cùng nguồn và cùng quy tắc null với <see cref="UserFullName"/>.
    /// </summary>
    public string? UserPhoneNumber { get; set; }

    /// <summary>
    /// "Login" | "Logout" | "LoginFailed" | "Create" | "Update" | "Delete" — tên chuỗi của enum
    /// <see cref="Entities.AuditAction"/>.
    ///
    /// Trả về CHUỖI chứ không phải enum: Program.cs không đăng ký JsonStringEnumConverter, nên
    /// kiểu enum sẽ serialize thành 0, 1, 2… — đọc không hiểu, trái tinh thần quy ước A3, và
    /// <c>getAuditActionMeta()</c> phía frontend sẽ rơi vào nhánh dự phòng nên mọi tag hiện số
    /// thay vì nhãn tiếng Việt. Cùng lý do <see cref="Routes.RouteResponse.Status"/> để kiểu string.
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// Đối tượng bị tác động, dạng <c>&lt;Tên bảng&gt;:&lt;Id&gt;</c> — ví dụ
    /// <c>"Routes:3f2a1b0c-…"</c>. <c>null</c> với đăng nhập / đăng xuất.
    /// </summary>
    public string? Target { get; set; }

    /// <summary>Địa chỉ IP của người gọi. <c>null</c> khi không lấy được.</summary>
    public string? IpAddress { get; set; }

    /// <summary>Thời điểm hành động xảy ra, UTC — cột thời gian của bộ lọc from/to.</summary>
    public DateTime CreatedAt { get; set; }
}
