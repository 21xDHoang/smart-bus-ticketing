using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Admin;

/// <summary>
/// Body của POST /api/admin/users — Admin tạo tài khoản cho một trong 4 vai trò.
/// Quy tắc SĐT và mật khẩu CỐ Ý giống hệt form đăng ký (<see cref="Auth.RegisterRequest"/>):
/// một hệ thống không nên có hai chuẩn mật khẩu khác nhau tuỳ vào ai tạo tài khoản.
/// </summary>
public class CreateAdminUserRequest
{
    [Required(ErrorMessage = "Họ và tên không được để trống")]
    [StringLength(200, MinimumLength = 2, ErrorMessage = "Họ và tên phải từ 2 đến 200 ký tự")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Số điện thoại không được để trống")]
    [RegularExpression(@"^0[35789]\d{8}$", ErrorMessage = "Số điện thoại không hợp lệ")]
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Không bắt buộc — khác form đăng ký vì Admin có thể tạo tài khoản nội bộ chưa có email.</summary>
    [EmailAddress(ErrorMessage = "Email không đúng định dạng")]
    [StringLength(256, ErrorMessage = "Email tối đa 256 ký tự")]
    public string? Email { get; set; }

    [Required(ErrorMessage = "Mật khẩu không được để trống")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Mật khẩu phải có ít nhất 8 ký tự")]
    [RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).+$", ErrorMessage = "Mật khẩu phải gồm cả chữ và số")]
    public string Password { get; set; } = string.Empty;

    /// <summary>Mã vai trò ban đầu: Admin | Manager | Driver | Passenger. Bắt buộc, không đoán hộ.</summary>
    [Required(ErrorMessage = "Vai trò không được để trống")]
    [StringLength(50, ErrorMessage = "Mã vai trò tối đa 50 ký tự")]
    public string RoleCode { get; set; } = string.Empty;
}
