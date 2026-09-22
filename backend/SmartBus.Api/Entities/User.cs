namespace SmartBus.Api.Entities;

/// <summary>
/// Bảng Users — tài khoản của cả 4 vai trò.
/// Task migrate bảng này là của Vàng Thị Dăm (story 22).
/// </summary>
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Số điện thoại đăng nhập, duy nhất.</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string FullName { get; set; } = string.Empty;

    /// <summary>Mật khẩu đã băm bằng BCrypt — KHÔNG bao giờ lưu mật khẩu thô.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Tài khoản bị khóa thì không đăng nhập được.</summary>
    public bool IsActive { get; set; } = true;

    public Guid RoleId { get; set; }

    public Role? Role { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}
