using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Auth;

/// <summary>Body của POST /api/auth/refresh-token.</summary>
public class RefreshTokenRequest
{
    [Required(ErrorMessage = "Refresh token không được để trống")]
    public string RefreshToken { get; set; } = string.Empty;
}
