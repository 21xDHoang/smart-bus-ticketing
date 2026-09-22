namespace SmartBus.Api.Entities;

/// <summary>
/// Bảng Roles — 4 vai trò mặc định: Admin, Quản lý, Tài xế, Hành khách.
/// Seed 4 vai trò này là task của Vàng Thị Dăm (story 22).
/// </summary>
public class Role
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Mã vai trò dùng trong JWT claim: Admin | Manager | Driver | Passenger.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Tài khoản lấy vai trò này làm vai trò chính.</summary>
    public ICollection<User> Users { get; set; } = new List<User>();

    /// <summary>Các lượt gán vai trò này cho tài khoản (bảng nối UserRoles).</summary>
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}
