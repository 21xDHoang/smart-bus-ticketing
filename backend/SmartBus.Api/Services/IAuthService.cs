using SmartBus.Api.Dtos.Auth;

namespace SmartBus.Api.Services;

/// <summary>Nghiệp vụ xác thực: đăng nhập, làm mới token, đăng xuất.</summary>
public interface IAuthService
{
    /// <summary>Kiểm tra số điện thoại + mật khẩu, cấp cặp access/refresh token.</summary>
    Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Đổi refresh token cũ lấy cặp token mới. Token cũ bị thu hồi ngay khi dùng
    /// (xoay vòng token) nên mỗi refresh token chỉ dùng được đúng một lần.
    /// </summary>
    Task<AuthResult> RefreshTokenAsync(string rawRefreshToken, CancellationToken cancellationToken = default);

    /// <summary>Thu hồi refresh token — kết thúc phiên đăng nhập. Gọi lại nhiều lần vẫn an toàn.</summary>
    Task LogoutAsync(string rawRefreshToken, CancellationToken cancellationToken = default);
}
