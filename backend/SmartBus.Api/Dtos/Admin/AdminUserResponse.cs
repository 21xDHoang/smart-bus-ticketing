namespace SmartBus.Api.Dtos.Admin;

/// <summary>
/// Một tài khoản trong màn hình quản trị người dùng.
/// Khớp interface <c>AdminUser</c> của frontend (frontend/src/api/adminUserApi.ts) — đổi tên
/// trường ở đây thì phải báo Băng/Hạnh sửa theo, xem docs/03-quy-uoc.md mục 2.2 điều 5.
/// </summary>
public class AdminUserResponse
{
    public Guid Id { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Không bắt buộc — tài khoản vẫn đăng nhập được bằng SĐT khi chưa có email.</summary>
    public string? Email { get; set; }

    /// <summary>Tài khoản đang mở (true) hay đã bị khóa (false).</summary>
    public bool IsActive { get; set; }

    /// <summary>Mã vai trò chính (<c>Users.RoleId</c>) — cột "Vai trò" trên bảng.</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// Toàn bộ vai trò tài khoản đang giữ: vai trò chính cộng các vai trò ở bảng nối
    /// <c>UserRoles</c>. Màn hình sửa vai trò cần đủ danh sách này, không chỉ vai trò chính.
    /// </summary>
    public string[] Roles { get; set; } = [];

    public DateTime CreatedAt { get; set; }
}
