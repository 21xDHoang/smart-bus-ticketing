namespace SmartBus.Api.Entities;

/// <summary>
/// Mã của 4 vai trò mặc định — giá trị nằm ở cột <see cref="Role.Code"/> và được phát ra
/// trong claim <c>ClaimTypes.Role</c> của JWT.
/// Khai báo thành hằng để policy phân quyền, controller và test dùng chung một nguồn:
/// gõ sai tên vai trò sẽ là lỗi biên dịch, không phải lỗi âm thầm lúc chạy.
///
/// Khác <see cref="RoleIds"/> (dùng <c>static readonly Guid</c>): ở đây buộc phải là <c>const string</c>
/// vì được dùng làm tham số attribute — <c>[Authorize(Roles = RoleCodes.Admin)]</c> chỉ nhận hằng
/// biên dịch được.
/// </summary>
public static class RoleCodes
{
    public const string Admin = "Admin";

    public const string Manager = "Manager";

    public const string Driver = "Driver";

    public const string Passenger = "Passenger";
}
