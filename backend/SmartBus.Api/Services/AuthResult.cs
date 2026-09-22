using SmartBus.Api.Dtos.Auth;

namespace SmartBus.Api.Services;

/// <summary>
/// Kết quả nghiệp vụ trả từ <see cref="IAuthService"/> lên Controller.
/// Controller chỉ đổi thành HTTP status, không tự phán đoán nghiệp vụ.
/// </summary>
public class AuthResult
{
    private AuthResult(bool success, AuthResponse? data, string? error)
    {
        Success = success;
        Data = data;
        Error = error;
    }

    public bool Success { get; }

    public AuthResponse? Data { get; }

    /// <summary>Thông báo lỗi hiển thị cho người dùng cuối — không chứa chi tiết kỹ thuật.</summary>
    public string? Error { get; }

    public static AuthResult Ok(AuthResponse data) => new(true, data, null);

    public static AuthResult Fail(string error) => new(false, null, error);
}
