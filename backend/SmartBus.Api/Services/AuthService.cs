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

    private readonly AppDbContext _db;
    private readonly ITokenService _tokenService;

    public AuthService(AppDbContext db, ITokenService tokenService)
    {
        _db = db;
        _tokenService = tokenService;
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.PhoneNumber == request.PhoneNumber, cancellationToken);

        if (user is null || !BCryptHasher.Verify(request.Password, user.PasswordHash))
        {
            return AuthResult.Fail(InvalidCredentials);
        }

        // Kiểm tra khóa sau khi đã xác thực mật khẩu — lúc này người gọi đã chứng minh
        // sở hữu tài khoản, nên báo rõ "bị khóa" không làm lộ thông tin gì thêm.
        if (!user.IsActive)
        {
            return AuthResult.Fail(AccountLocked);
        }

        return AuthResult.Ok(await IssueTokensAsync(user, cancellationToken));
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

    public async Task LogoutAsync(string rawRefreshToken, CancellationToken cancellationToken = default)
    {
        var tokenHash = _tokenService.HashRefreshToken(rawRefreshToken);

        var stored = await _db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

        // Không tìm thấy hoặc đã thu hồi rồi → coi như đăng xuất thành công.
        // Đăng xuất phải idempotent: gọi lại lần hai không được báo lỗi.
        if (stored is null || stored.RevokedAt is not null)
        {
            return;
        }

        stored.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
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
