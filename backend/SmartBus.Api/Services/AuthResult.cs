using SmartBus.Api.Dtos.Auth;

namespace SmartBus.Api.Services;

/// <summary>
/// Kết quả nghiệp vụ trả từ <see cref="IAuthService"/> lên Controller.
/// Controller chỉ đổi thành HTTP status, không tự phán đoán nghiệp vụ.
/// </summary>
public class AuthResult
{
    private AuthResult(bool success, AuthResponse? data, string? error, IReadOnlyDictionary<string, string[]>? errors)
    {
        Success = success;
        Data = data;
        Error = error;
        Errors = errors;
    }

    public bool Success { get; }

    public AuthResponse? Data { get; }

    /// <summary>Thông báo lỗi hiển thị cho người dùng cuối — không chứa chi tiết kỹ thuật.</summary>
    public string? Error { get; }

    /// <summary>
    /// Lỗi theo từng trường để frontend gắn trực tiếp vào ô input (ví dụ SĐT đã tồn tại).
    /// Cùng cấu trúc { message, errors } thống nhất của dự án — xem docs/01-kien-truc.md.
    /// </summary>
    public IReadOnlyDictionary<string, string[]>? Errors { get; }

    public static AuthResult Ok(AuthResponse data) => new(true, data, null, null);

    public static AuthResult Fail(string error) => new(false, null, error, null);

    public static AuthResult Fail(string error, IReadOnlyDictionary<string, string[]> errors)
        => new(false, null, error, errors);
}
