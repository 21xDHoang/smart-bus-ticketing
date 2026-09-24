using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Auth;

/// <summary>
/// Body của POST /api/auth/register — hành khách tự tạo tài khoản.
/// Quy tắc kiểm tra phải KHỚP với form Đăng ký phía frontend (RegisterForm.tsx):
/// SĐT di động Việt Nam 10 số bắt đầu bằng 0[35789], email đúng định dạng,
/// mật khẩu đủ mạnh (ít nhất 8 ký tự, gồm cả chữ và số).
/// </summary>
public class RegisterRequest
{
    [Required(ErrorMessage = "Họ và tên không được để trống")]
    [StringLength(200, MinimumLength = 2, ErrorMessage = "Họ và tên phải từ 2 đến 200 ký tự")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email không được để trống")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng")]
    [StringLength(256, ErrorMessage = "Email tối đa 256 ký tự")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Số điện thoại không được để trống")]
    [RegularExpression(@"^0[35789]\d{8}$", ErrorMessage = "Số điện thoại không hợp lệ")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mật khẩu không được để trống")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Mật khẩu phải có ít nhất 8 ký tự")]
    [RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).+$", ErrorMessage = "Mật khẩu phải gồm cả chữ và số")]
    public string Password { get; set; } = string.Empty;
}
