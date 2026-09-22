namespace SmartBus.Api.Services;

/// <summary>
/// Bind từ section "Jwt" trong appsettings.Development.json (file này bị .gitignore chặn).
/// Mẫu đầy đủ nằm ở appsettings.Development.json.example.
/// </summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    /// <summary>Khoá ký HMAC-SHA256, tối thiểu 32 ký tự.</summary>
    public string Key { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 30;

    public int RefreshTokenDays { get; set; } = 7;
}
