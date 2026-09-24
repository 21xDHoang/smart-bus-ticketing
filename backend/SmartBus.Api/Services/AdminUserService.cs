using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Admin;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

public class AdminUserService : IAdminUserService
{
    private const string UserNotFound = "Không tìm thấy tài khoản";

    /// <summary>Dùng chung câu với <see cref="AuthService"/> để người dùng nhận một thông báo duy nhất.</summary>
    private const string PhoneExists = "Số điện thoại đã được đăng ký";

    private const string EmailExists = "Email đã được sử dụng";

    private const string RoleNotFound = "Vai trò không tồn tại";

    private const string CannotLockSelf = "Không thể tự khóa tài khoản của chính mình";

    private const string CannotRevokeOwnAdmin = "Không thể tự thu hồi vai trò Admin của chính mình";

    private const int DefaultPageSize = 10;

    /// <summary>
    /// Thứ tự ưu tiên chọn vai trò chính khi vai trò chính cũ bị thu hồi. Xếp theo mức quyền
    /// giảm dần để tài khoản còn nhiều vai trò không bị hạ xuống vai trò thấp nhất một cách vô cớ.
    /// </summary>
    private static readonly string[] PrimaryRolePriority =
    [
        RoleCodes.Admin,
        RoleCodes.Manager,
        RoleCodes.Driver,
        RoleCodes.Passenger,
    ];

    private readonly AppDbContext _db;

    public AdminUserService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<ServiceResult<AdminUserListResponse>> ListAsync(
        ListAdminUsersRequest request,
        CancellationToken cancellationToken = default)
    {
        var page = request.Page ?? 1;
        var pageSize = request.PageSize ?? DefaultPageSize;

        var query = _db.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            // So khớp không phân biệt hoa thường bằng ToLower() thay vì EF.Functions.ILike:
            // ILike là hàm riêng của Npgsql, dùng nó thì test chạy trên provider InMemory sẽ đổ.
            // Cách này dịch được sang SQL ở PostgreSQL và cũng chạy được ở InMemory —
            // cùng cách AuthService đang kiểm tra trùng email khi đăng ký.
            var keyword = request.Search.Trim().ToLowerInvariant();

            query = query.Where(u =>
                u.FullName.ToLower().Contains(keyword) ||
                u.PhoneNumber.Contains(keyword) ||
                (u.Email != null && u.Email.ToLower().Contains(keyword)));
        }

        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            var roleCode = request.Role.Trim();
            query = query.Where(u => u.Role != null && u.Role.Code == roleCode);
        }

        if (request.IsActive is not null)
        {
            var isActive = request.IsActive.Value;
            query = query.Where(u => u.IsActive == isActive);
        }

        var total = await query.CountAsync(cancellationToken);

        var users = await query
            .OrderByDescending(u => u.CreatedAt)
            // Chốt thêm theo Id: nhiều tài khoản tạo cùng lúc có thể trùng CreatedAt, thiếu khoá
            // phụ thì thứ tự giữa các trang không ổn định và phân trang bị trùng/thiếu dòng.
            .ThenBy(u => u.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return ServiceResult<AdminUserListResponse>.Ok(new AdminUserListResponse
        {
            Items = users.Select(ToResponse).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize,
        });
    }

    public async Task<ServiceResult<AdminUserResponse>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var user = await FindAsync(id, tracking: false, cancellationToken);

        return user is null
            ? ServiceResult<AdminUserResponse>.NotFound(UserNotFound)
            : ServiceResult<AdminUserResponse>.Ok(ToResponse(user));
    }

    public async Task<ServiceResult<AdminUserResponse>> CreateAsync(
        CreateAdminUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var phoneNumber = request.PhoneNumber.Trim();
        var email = NormalizeEmail(request.Email);
        var roleCode = request.RoleCode.Trim();

        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Code == roleCode, cancellationToken);
        if (role is null)
        {
            return ServiceResult<AdminUserResponse>.Invalid(
                RoleNotFound,
                new Dictionary<string, string[]> { ["roleCode"] = [RoleNotFound] });
        }

        // Kiểm tra trùng TRƯỚC khi băm mật khẩu (BCrypt tốn CPU) — request chắc chắn hỏng
        // thì không nên tiêu tốn tài nguyên.
        if (await _db.Users.AnyAsync(u => u.PhoneNumber == phoneNumber, cancellationToken))
        {
            return ServiceResult<AdminUserResponse>.Invalid(
                PhoneExists,
                new Dictionary<string, string[]> { ["phoneNumber"] = [PhoneExists] });
        }

        if (email is not null && await EmailTakenAsync(email, exceptUserId: null, cancellationToken))
        {
            return ServiceResult<AdminUserResponse>.Invalid(
                EmailExists,
                new Dictionary<string, string[]> { ["email"] = [EmailExists] });
        }

        var user = new User
        {
            FullName = request.FullName.Trim(),
            PhoneNumber = phoneNumber,
            Email = email,
            PasswordHash = PasswordService.HashPassword(request.Password),
            IsActive = true,
            RoleId = role.Id,
            Role = role,
            // Ghi luôn một dòng vào bảng nối, giống AuthService khi đăng ký: vai trò chính
            // phải luôn nằm trong UserRoles, nếu không màn hình sửa vai trò sẽ hiện thiếu.
            UserRoles = [new UserRole { RoleId = role.Id, Role = role }],
        };

        _db.Users.Add(user);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Hai request cùng SĐT chạy song song lọt qua bước kiểm tra ở trên —
            // ràng buộc unique trong CSDL là lớp bảo vệ cuối cùng.
            return ServiceResult<AdminUserResponse>.Invalid(
                PhoneExists,
                new Dictionary<string, string[]> { ["phoneNumber"] = [PhoneExists] });
        }

        return ServiceResult<AdminUserResponse>.Ok(ToResponse(user));
    }

    public async Task<ServiceResult<AdminUserResponse>> UpdateAsync(
        Guid id,
        UpdateAdminUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await FindAsync(id, tracking: true, cancellationToken);
        if (user is null)
        {
            return ServiceResult<AdminUserResponse>.NotFound(UserNotFound);
        }

        var phoneNumber = request.PhoneNumber.Trim();
        var email = NormalizeEmail(request.Email);

        if (phoneNumber != user.PhoneNumber &&
            await _db.Users.AnyAsync(u => u.Id != id && u.PhoneNumber == phoneNumber, cancellationToken))
        {
            return ServiceResult<AdminUserResponse>.Invalid(
                PhoneExists,
                new Dictionary<string, string[]> { ["phoneNumber"] = [PhoneExists] });
        }

        if (email is not null && await EmailTakenAsync(email, exceptUserId: id, cancellationToken))
        {
            return ServiceResult<AdminUserResponse>.Invalid(
                EmailExists,
                new Dictionary<string, string[]> { ["email"] = [EmailExists] });
        }

        user.FullName = request.FullName.Trim();
        user.PhoneNumber = phoneNumber;
        user.Email = email;
        user.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return ServiceResult<AdminUserResponse>.Invalid(
                PhoneExists,
                new Dictionary<string, string[]> { ["phoneNumber"] = [PhoneExists] });
        }

        return ServiceResult<AdminUserResponse>.Ok(ToResponse(user));
    }

    public async Task<ServiceResult<AdminUserResponse>> SetStatusAsync(
        Guid id,
        bool isActive,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var user = await FindAsync(id, tracking: true, cancellationToken);
        if (user is null)
        {
            return ServiceResult<AdminUserResponse>.NotFound(UserNotFound);
        }

        if (!isActive && id == currentUserId)
        {
            return ServiceResult<AdminUserResponse>.Conflict(CannotLockSelf);
        }

        // Gọi lại với đúng trạng thái hiện tại là chuyện bình thường (bấm hai lần, hai tab).
        // Trả về luôn thay vì báo lỗi để thao tác này là idempotent.
        if (user.IsActive == isActive)
        {
            return ServiceResult<AdminUserResponse>.Ok(ToResponse(user));
        }

        user.IsActive = isActive;
        user.UpdatedAt = DateTime.UtcNow;

        // Không cần thu hồi refresh token của tài khoản vừa bị khóa: JwtMiddleware đọc lại
        // IsActive từ CSDL ở MỌI request (kể cả /auth/refresh-token), nên hiệu lực là tức thì.
        await _db.SaveChangesAsync(cancellationToken);

        return ServiceResult<AdminUserResponse>.Ok(ToResponse(user));
    }

    public Task<ServiceResult<AdminUserResponse>> DeleteAsync(
        Guid id,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
        // Xoá mềm chính là khóa tài khoản — dùng lại đúng một đường để hai lối vào
        // không bao giờ lệch hành vi (ví dụ quên chặn tự xoá chính mình).
        => SetStatusAsync(id, isActive: false, currentUserId, cancellationToken);

    public async Task<ServiceResult<AdminUserResponse>> SetRolesAsync(
        Guid id,
        SetUserRolesRequest request,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var user = await FindAsync(id, tracking: true, cancellationToken);
        if (user is null)
        {
            return ServiceResult<AdminUserResponse>.NotFound(UserNotFound);
        }

        var requested = request.RoleCodes
            .Select(code => code.Trim())
            .Where(code => code.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (requested.Length == 0)
        {
            return InvalidRoleList("Tài khoản phải có ít nhất một vai trò");
        }

        var roles = await _db.Roles
            .Where(r => requested.Contains(r.Code))
            .ToListAsync(cancellationToken);

        var unknown = requested
            .Except(roles.Select(r => r.Code), StringComparer.Ordinal)
            .ToArray();

        if (unknown.Length > 0)
        {
            return InvalidRoleList($"Vai trò không tồn tại: {string.Join(", ", unknown)}");
        }

        // Tự thu hồi vai trò Admin của chính mình = tự đá mình khỏi mọi màn hình quản trị,
        // và không còn đường lấy lại vì chính endpoint này đã đòi vai trò Admin.
        if (id == currentUserId && roles.All(r => r.Code != RoleCodes.Admin))
        {
            return ServiceResult<AdminUserResponse>.Conflict(CannotRevokeOwnAdmin);
        }

        SyncUserRoles(user, roles);
        user.RoleId = PickPrimaryRole(roles, user.RoleId).Id;
        user.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return ServiceResult<AdminUserResponse>.Ok(ToResponse(user));
    }

    /// <summary>Nạp tài khoản kèm vai trò. Bảng nối phải Include vì đó là nơi gán/thu hồi vai trò ghi vào.</summary>
    private async Task<User?> FindAsync(Guid id, bool tracking, CancellationToken cancellationToken)
    {
        var query = _db.Users
            .Include(u => u.Role)
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .AsQueryable();

        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }

    /// <summary>
    /// Email không có ràng buộc unique ở CSDL (xem AppDbContext.Auth.cs) nên phải so khớp
    /// không phân biệt hoa thường ngay tại đây — cùng cách AuthService đang làm khi đăng ký.
    /// </summary>
    private Task<bool> EmailTakenAsync(string email, Guid? exceptUserId, CancellationToken cancellationToken)
    {
        var lower = email.ToLowerInvariant();

        var query = _db.Users.Where(u => u.Email != null && u.Email.ToLower() == lower);

        if (exceptUserId is not null)
        {
            var exceptId = exceptUserId.Value;
            query = query.Where(u => u.Id != exceptId);
        }

        return query.AnyAsync(cancellationToken);
    }

    /// <summary>
    /// Đồng bộ bảng nối <c>UserRoles</c> theo đúng danh sách vai trò mới:
    /// dòng không còn trong danh sách thì xoá (thu hồi), vai trò chưa có thì thêm (gán).
    /// </summary>
    private static void SyncUserRoles(User user, IReadOnlyCollection<Role> roles)
    {
        var wanted = roles.Select(r => r.Id).ToHashSet();

        // Chốt danh sách lại trước khi xoá — không sửa được collection đang duyệt.
        foreach (var revoked in user.UserRoles.Where(ur => !wanted.Contains(ur.RoleId)).ToList())
        {
            user.UserRoles.Remove(revoked);
        }

        var current = user.UserRoles.Select(ur => ur.RoleId).ToHashSet();

        foreach (var role in roles.Where(r => !current.Contains(r.Id)))
        {
            user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id, Role = role });
        }
    }

    /// <summary>
    /// Chọn vai trò chính sau khi đổi danh sách vai trò: giữ nguyên vai trò chính cũ nếu tài khoản
    /// vẫn còn vai trò đó, ngược lại lấy vai trò có mức quyền cao nhất trong danh sách mới.
    /// </summary>
    private static Role PickPrimaryRole(IReadOnlyCollection<Role> roles, Guid currentRoleId)
    {
        var kept = roles.FirstOrDefault(r => r.Id == currentRoleId);
        if (kept is not null)
        {
            return kept;
        }

        foreach (var code in PrimaryRolePriority)
        {
            var preferred = roles.FirstOrDefault(r => r.Code == code);
            if (preferred is not null)
            {
                return preferred;
            }
        }

        // Vai trò ngoài 4 mã mặc định (thêm sau này): vẫn phải chọn được một cái,
        // vì Users.RoleId là trường bắt buộc.
        return roles.First();
    }

    /// <summary>Chuỗi rỗng hoặc toàn khoảng trắng coi như không có email — tránh lưu "" gây trùng nhau.</summary>
    private static string? NormalizeEmail(string? email)
    {
        var trimmed = email?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static ServiceResult<AdminUserResponse> InvalidRoleList(string message)
        => ServiceResult<AdminUserResponse>.Invalid(
            message,
            new Dictionary<string, string[]> { ["roleCodes"] = [message] });

    private static AdminUserResponse ToResponse(User user) => new()
    {
        Id = user.Id,
        FullName = user.FullName,
        PhoneNumber = user.PhoneNumber,
        Email = user.Email,
        IsActive = user.IsActive,
        Role = user.Role?.Code ?? string.Empty,
        // Hợp nhất vai trò chính với bảng nối: bình thường vai trò chính đã nằm trong bảng nối
        // (AuthService ghi cả hai khi đăng ký), nhưng dữ liệu cũ có thể thiếu — lấy hợp thì
        // màn hình sửa vai trò không bao giờ hiện thiếu so với thực tế phân quyền.
        Roles = user.UserRoles
            .Select(ur => ur.Role?.Code)
            .Append(user.Role?.Code)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray(),
        CreatedAt = user.CreatedAt,
    };
}
