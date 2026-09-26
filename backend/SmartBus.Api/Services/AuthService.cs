using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Auth;
using SmartBus.Api.Entities;
using BCryptHasher = BCrypt.Net.BCrypt;

namespace SmartBus.Api.Services;

public class AuthService : IAuthService
{
    /// <summary>
    /// Cố tình dùng chung một thông báo cho "không tìm thấy tài khoản" và "sai mật khẩu"
    /// để người ngoài không dò được số điện thoại nào đã đăng ký.
    /// </summary>
    private const string InvalidCredentials = "Số điện thoại hoặc mật khẩu không đúng";

    private const string AccountLocked = "Tài khoản đã bị khóa";

    private const string InvalidRefreshToken = "Refresh token không hợp lệ hoặc đã hết hạn";

    private const string PhoneExists = "Số điện thoại đã được đăng ký";

    private const string EmailExists = "Email đã được sử dụng";

    private readonly AppDbContext _db;
    private readonly ITokenService _tokenService;

    public AuthService(AppDbContext db, ITokenService tokenService)
    {
        _db = db;
        _tokenService = tokenService;
    }

    public async Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var fullName = request.FullName.Trim();
        var email = request.Email.Trim();
        var phoneNumber = request.PhoneNumber.Trim();

        // Kiểm tra trùng TRƯỚC khi băm mật khẩu (BCrypt tốn CPU) để request trùng không tốn tài nguyên.
        if (await _db.Users.AnyAsync(u => u.PhoneNumber == phoneNumber, cancellationToken))
        {
            return AuthResult.Fail(PhoneExists, new Dictionary<string, string[]> { ["phoneNumber"] = [PhoneExists] });
        }

        // Email không có ràng buộc unique ở CSDL nên so khớp không phân biệt hoa thường ngay tại đây.
        var lowerEmail = email.ToLowerInvariant();
        if (await _db.Users.AnyAsync(u => u.Email != null && u.Email.ToLower() == lowerEmail, cancellationToken))
        {
            return AuthResult.Fail(EmailExists, new Dictionary<string, string[]> { ["email"] = [EmailExists] });
        }

        // Tài khoản mới luôn là Hành khách; bảng nối UserRoles cũng ghi một dòng để
        // các màn hình quản lý vai trò nhìn thấy đủ, không cần xử lý riêng loại tài khoản này.
        var user = new User
        {
            FullName = fullName,
            Email = email,
            PhoneNumber = phoneNumber,
            PasswordHash = PasswordService.HashPassword(request.Password),
            RoleId = RoleIds.Passenger,
            UserRoles = new List<UserRole> { new() { RoleId = RoleIds.Passenger } },
        };

        _db.Users.Add(user);

        try
        {
            // IssueTokensAsync tự SaveChanges — user, dòng UserRoles và refresh token được ghi cùng một lượt.
            return AuthResult.Ok(await IssueTokensAsync(user, cancellationToken));
        }
        catch (DbUpdateException)
        {
            // Hai request cùng SĐT gửi đồng thời lọt qua được bước kiểm tra ở trên,
            // ràng buộc unique trong CSDL là lớp bảo vệ cuối cùng.
            return AuthResult.Fail(PhoneExists, new Dictionary<string, string[]> { ["phoneNumber"] = [PhoneExists] });
        }
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.PhoneNumber == request.PhoneNumber, cancellationToken);

        // Tách ba nhánh thất bại để Controller có được người thực hiện khi ghi nhật ký (US 23, B28):
        // SĐT không tồn tại thì UserId để NULL, còn sai mật khẩu / tài khoản bị khoá thì
        // đã tra ra tài khoản. Thông báo trả về vẫn dùng chung một câu (xem InvalidCredentials)
        // để người ngoài không dò được số điện thoại nào đã đăng ký.
        if (user is null)
        {
            return AuthResult.Fail(InvalidCredentials);
        }

        if (!BCryptHasher.Verify(request.Password, user.PasswordHash))
        {
            return AuthResult.Fail(InvalidCredentials, user.Id);
        }

        // Kiểm tra khóa sau khi đã xác thực mật khẩu — lúc này người gọi đã chứng minh
        // sở hữu tài khoản, nên báo rõ "bị khóa" không làm lộ thông tin gì thêm.
        if (!user.IsActive)
        {
            return AuthResult.Fail(AccountLocked, user.Id);
        }

        return AuthResult.Ok(await IssueTokensAsync(user, cancellationToken), user.Id);
    }

    public async Task<AuthResult> RefreshTokenAsync(string rawRefreshToken, CancellationToken cancellationToken = default)
    {
        var tokenHash = _tokenService.HashRefreshToken(rawRefreshToken);

        var stored = await _db.RefreshTokens
            .Include(t => t.User)
                .ThenInclude(u => u!.Role)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

        if (stored?.User is null || !stored.IsActive)
        {
            return AuthResult.Fail(InvalidRefreshToken);
        }

        if (!stored.User.IsActive)
        {
            return AuthResult.Fail(AccountLocked);
        }

        // Xoay vòng: thu hồi token vừa dùng trước khi cấp token mới.
        stored.RevokedAt = DateTime.UtcNow;

        return AuthResult.Ok(await IssueTokensAsync(stored.User, cancellationToken));
    }

    public async Task<Guid?> LogoutAsync(string rawRefreshToken, CancellationToken cancellationToken = default)
    {
        var tokenHash = _tokenService.HashRefreshToken(rawRefreshToken);

        var stored = await _db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

        // Không tìm thấy hoặc đã thu hồi rồi → coi như đăng xuất thành công.
        // Đăng xuất phải idempotent: gọi lại lần hai không được báo lỗi.
        // Trả NULL để Controller biết không có phiên nào kết thúc — nhật ký đăng xuất
        // chỉ ghi khi token thực sự bị thu hồi (có dữ liệu thay đổi).
        if (stored is null || stored.RevokedAt is not null)
        {
            return null;
        }

        stored.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return stored.UserId;
    }

    /// <summary>Cấp cặp access token + refresh token mới và lưu refresh token đã băm.</summary>
    private async Task<AuthResponse> IssueTokensAsync(User user, CancellationToken cancellationToken)
    {
        var (accessToken, expiresIn) = _tokenService.CreateAccessToken(user);
        var refreshToken = _tokenService.CreateRefreshToken();

        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = _tokenService.HashRefreshToken(refreshToken),
            ExpiresAt = DateTime.UtcNow.AddDays(_tokenService.RefreshTokenDays),
        });

        await _db.SaveChangesAsync(cancellationToken);

        return new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            TokenType = "Bearer",
            ExpiresIn = expiresIn,
        };
    }
}
