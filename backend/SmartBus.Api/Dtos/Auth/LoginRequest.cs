using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Auth;

/// <summary>Body của POST /api/auth/login.</summary>
public class LoginRequest
{
    [Required(ErrorMessage = "Số điện thoại không được để trống")]
    [StringLength(20, MinimumLength = 9, ErrorMessage = "Số điện thoại không hợp lệ")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mật khẩu không được để trống")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Mật khẩu phải có ít nhất 6 ký tự")]
    public string Password { get; set; } = string.Empty;
}
