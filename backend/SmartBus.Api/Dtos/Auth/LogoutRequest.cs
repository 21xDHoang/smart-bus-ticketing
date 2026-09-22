using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Auth;

/// <summary>Body của POST /api/auth/logout.</summary>
public class LogoutRequest
{
    [Required(ErrorMessage = "Refresh token không được để trống")]
    public string RefreshToken { get; set; } = string.Empty;
}
