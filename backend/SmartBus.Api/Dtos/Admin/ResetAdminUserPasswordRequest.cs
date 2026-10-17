using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Admin;

/// <summary>
/// Body của PUT /api/admin/users/{id}/password — Admin đặt lại mật khẩu cho một tài khoản.
/// Ràng buộc giống hệt form đăng ký và <see cref="CreateAdminUserRequest"/>: một hệ thống
/// không nên có hai chuẩn mật khẩu khác nhau tuỳ vào ai đặt.
/// </summary>
public class ResetAdminUserPasswordRequest
{
    [Required(ErrorMessage = "Mật khẩu không được để trống")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Mật khẩu phải có ít nhất 8 ký tự")]
    [RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).+$", ErrorMessage = "Mật khẩu phải gồm cả chữ và số")]
    public string NewPassword { get; set; } = string.Empty;
}
