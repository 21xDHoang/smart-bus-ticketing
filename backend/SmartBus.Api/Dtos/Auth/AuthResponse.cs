namespace SmartBus.Api.Dtos.Auth;

/// <summary>
/// Kết quả cấp token — trả về cho cả /auth/login và /auth/refresh-token.
/// Frontend gắn <see cref="AccessToken"/> vào header "Authorization: Bearer ...".
/// Thông tin người dùng (id, vai trò) nằm trong payload của access token.
/// </summary>
public class AuthResponse
{
    public string AccessToken { get; set; } = string.Empty;

    public string RefreshToken { get; set; } = string.Empty;

    public string TokenType { get; set; } = "Bearer";

    /// <summary>Thời gian sống của access token, tính bằng giây.</summary>
    public int ExpiresIn { get; set; }
}
