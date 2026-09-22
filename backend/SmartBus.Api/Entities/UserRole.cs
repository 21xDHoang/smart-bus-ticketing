namespace SmartBus.Api.Entities;

/// <summary>
/// Bảng UserRoles — bảng nối nhiều-nhiều giữa Users và Roles.
/// Một tài khoản có thể giữ nhiều vai trò (ví dụ vừa Quản lý vừa Tài xế).
/// Vai trò chính dùng để phát JWT vẫn nằm ở <see cref="User.RoleId"/>.
/// Task migrate bảng này là của Vàng Thị Dăm (story 22).
/// </summary>
public class UserRole
{
    public Guid UserId { get; set; }

    public User? User { get; set; }

    public Guid RoleId { get; set; }

    public Role? Role { get; set; }
}
