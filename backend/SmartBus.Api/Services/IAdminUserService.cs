using SmartBus.Api.Dtos.Admin;

namespace SmartBus.Api.Services;

/// <summary>
/// Nghiệp vụ quản trị người dùng: CRUD tài khoản, khóa/mở khóa, gán/thu hồi vai trò.
/// Story 22 — Nguyễn Duy Kiên.
///
/// Mọi thao tác ở đây đều do Admin thực hiện; việc chặn người không phải Admin là của
/// <see cref="RbacPolicies.AdminOnly"/> ở Controller, không lặp lại trong Service.
/// </summary>
public interface IAdminUserService
{
    /// <summary>Danh sách tài khoản — tìm kiếm, lọc theo vai trò chính và trạng thái, phân trang.</summary>
    Task<ServiceResult<AdminUserListResponse>> ListAsync(
        ListAdminUsersRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Chi tiết một tài khoản. Không tìm thấy trả lỗi loại <see cref="ServiceErrorKind.NotFound"/>.</summary>
    Task<ServiceResult<AdminUserResponse>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tạo tài khoản với vai trò chỉ định, mật khẩu băm bằng BCrypt.
    /// Trùng SĐT/email trả lỗi kèm tên trường để frontend gắn vào ô input.
    /// </summary>
    Task<ServiceResult<AdminUserResponse>> CreateAsync(
        CreateAdminUserRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Sửa hồ sơ (họ tên, SĐT, email). Vai trò và trạng thái có endpoint riêng.</summary>
    Task<ServiceResult<AdminUserResponse>> UpdateAsync(
        Guid id,
        UpdateAdminUserRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Khóa hoặc mở khóa tài khoản. Không cho tự khóa chính mình: Admin tự khóa xong thì
    /// không còn đường nào mở lại vì mọi endpoint quản trị đều đòi vai trò Admin.
    /// </summary>
    Task<ServiceResult<AdminUserResponse>> SetStatusAsync(
        Guid id,
        bool isActive,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Xoá mềm — khóa tài khoản, KHÔNG xoá dữ liệu. Quy ước A4 cấm thêm cột IsDeleted và
    /// chỉ cho dùng cột trạng thái sẵn có, nên "xoá" ở đây chính là <c>IsActive = false</c>.
    /// </summary>
    Task<ServiceResult<AdminUserResponse>> DeleteAsync(
        Guid id,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Đặt lại toàn bộ vai trò của tài khoản: mã chưa có thì gán, mã không còn trong danh sách
    /// thì thu hồi. Không cho tự thu hồi vai trò Admin của chính mình.
    /// </summary>
    Task<ServiceResult<AdminUserResponse>> SetRolesAsync(
        Guid id,
        SetUserRolesRequest request,
        Guid currentUserId,
        CancellationToken cancellationToken = default);
}
