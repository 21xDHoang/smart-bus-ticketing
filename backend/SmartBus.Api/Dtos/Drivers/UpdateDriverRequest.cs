using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Drivers;

/// <summary>
/// Body của PUT /api/drivers/{id} — sửa hồ sơ tài xế.
/// Không có mật khẩu (đổi mật khẩu là nghiệp vụ riêng), không có trạng thái (khóa tài khoản
/// có DELETE riêng) — cùng lối <see cref="Admin.UpdateAdminUserRequest"/>.
/// </summary>
public class UpdateDriverRequest
{
    [Required(ErrorMessage = "Họ và tên không được để trống")]
    [StringLength(200, MinimumLength = 2, ErrorMessage = "Họ và tên phải từ 2 đến 200 ký tự")]
    public string FullName { get; set; } = string.Empty;

    /// <summary>SĐT là tên đăng nhập — đổi được nhưng phải không trùng tài khoản khác.</summary>
    [Required(ErrorMessage = "Số điện thoại không được để trống")]
    [RegularExpression(@"^0[35789]\d{8}$", ErrorMessage = "Số điện thoại không hợp lệ")]
    public string PhoneNumber { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "Email không đúng định dạng")]
    [StringLength(256, ErrorMessage = "Email tối đa 256 ký tự")]
    public string? Email { get; set; }
}
