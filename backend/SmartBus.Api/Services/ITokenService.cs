using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>Sinh và băm token. Không chạm CSDL.</summary>
public interface ITokenService
{
    /// <summary>Số ngày refresh token còn hiệu lực (từ cấu hình "Jwt:RefreshTokenDays").</summary>
    int RefreshTokenDays { get; }

    /// <summary>Ký access token JWT cho user, kèm claim vai trò để middleware RBAC đọc được.</summary>
    /// <returns>Chuỗi token và thời gian sống tính bằng giây.</returns>
    (string Token, int ExpiresInSeconds) CreateAccessToken(User user);

    /// <summary>Sinh refresh token thô bằng CSPRNG. Chỉ trả về đúng một lần cho client.</summary>
    string CreateRefreshToken();

    /// <summary>Băm refresh token thô để lưu / đối chiếu trong CSDL.</summary>
    string HashRefreshToken(string rawToken);
}
