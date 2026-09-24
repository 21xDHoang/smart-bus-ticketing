using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Admin;

/// <summary>
/// Body của PUT /api/admin/users/{id}/roles — gán và thu hồi vai trò trong cùng một thao tác.
/// Gửi lên DANH SÁCH ĐẦY ĐỦ vai trò muốn tài khoản giữ: mã nào chưa có thì được gán,
/// mã nào không còn trong danh sách thì bị thu hồi. Nhờ vậy thao tác là idempotent —
/// gửi lại cùng một body không tạo thêm dòng và không báo lỗi.
/// </summary>
public class SetUserRolesRequest
{
    /// <summary>Mã vai trò: Admin | Manager | Driver | Passenger. Ít nhất một mã.</summary>
    [Required(ErrorMessage = "Danh sách vai trò không được để trống")]
    [MinLength(1, ErrorMessage = "Tài khoản phải có ít nhất một vai trò")]
    public string[] RoleCodes { get; set; } = [];
}
