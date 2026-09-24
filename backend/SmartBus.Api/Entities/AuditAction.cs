namespace SmartBus.Api.Entities;

/// <summary>
/// Loại hành động được ghi vào nhật ký (US 23). Lưu dạng chuỗi trong CSDL (quy ước A3):
/// "LoginFailed" đọc là hiểu, còn 2 thì phải tra bảng mã.
///
/// Cố ý giữ ở mức thao tác chung (Create/Update/Delete) chứ không tách theo từng nghiệp vụ
/// ("CreateRoute", "UpdateFare"…): middleware ghi log tự động suy ra hành động từ HTTP verb
/// (POST → Create, PUT/PATCH → Update, DELETE → Delete), nên không phải sửa enum mỗi lần
/// có màn hình mới. Chi tiết bị tác động nằm ở cột <see cref="AuditLog.Target"/>.
/// </summary>
public enum AuditAction
{
    /// <summary>Đăng nhập thành công.</summary>
    Login,

    /// <summary>Đăng xuất (thu hồi refresh token).</summary>
    Logout,

    /// <summary>
    /// Đăng nhập thất bại — sai mật khẩu, tài khoản bị khoá, hoặc SĐT không tồn tại.
    /// Trường hợp SĐT không tồn tại thì <see cref="AuditLog.UserId"/> để NULL vì
    /// không tra ra tài khoản nào.
    /// </summary>
    LoginFailed,

    /// <summary>Tạo bản ghi mới (POST).</summary>
    Create,

    /// <summary>Sửa bản ghi đã có (PUT/PATCH) — gồm cả khóa/mở khóa tài khoản và gán vai trò.</summary>
    Update,

    /// <summary>Xoá bản ghi (DELETE).</summary>
    Delete
}
