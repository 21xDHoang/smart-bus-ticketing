using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Drivers;

/// <summary>
/// Body của POST /api/drivers — tạo hồ sơ tài xế mới.
/// Quy tắc SĐT và mật khẩu CỐ Ý giống hệt form đăng ký và <see cref="Admin.CreateAdminUserRequest"/>:
/// một hệ thống không nên có hai chuẩn mật khẩu khác nhau tuỳ vào ai tạo tài khoản.
///
/// Không có trường roleCode (khác CreateAdminUserRequest): hồ sơ tạo từ /drivers LUÔN mang vai
/// trò Driver — gửi vai trò khác vào đây chỉ là lỗi chờ xảy ra, muốn đổi vai trò thì có
/// PUT /admin/users/{id}/roles của Admin.
/// </summary>
public class CreateDriverRequest
{
    [Required(ErrorMessage = "Họ và tên không được để trống")]
    [StringLength(200, MinimumLength = 2, ErrorMessage = "Họ và tên phải từ 2 đến 200 ký tự")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Số điện thoại không được để trống")]
    [RegularExpression(@"^0[35789]\d{8}$", ErrorMessage = "Số điện thoại không hợp lệ")]
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Không bắt buộc — tài xế vẫn đăng nhập được bằng SĐT khi chưa có email.</summary>
    [EmailAddress(ErrorMessage = "Email không đúng định dạng")]
    [StringLength(256, ErrorMessage = "Email tối đa 256 ký tự")]
    public string? Email { get; set; }

    [Required(ErrorMessage = "Mật khẩu không được để trống")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Mật khẩu phải có ít nhất 8 ký tự")]
    [RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).+$", ErrorMessage = "Mật khẩu phải gồm cả chữ và số")]
    public string Password { get; set; } = string.Empty;
}
