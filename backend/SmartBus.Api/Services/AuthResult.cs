using SmartBus.Api.Dtos.Auth;

namespace SmartBus.Api.Services;

/// <summary>
/// Kết quả nghiệp vụ trả từ <see cref="IAuthService"/> lên Controller.
/// Controller chỉ đổi thành HTTP status, không tự phán đoán nghiệp vụ.
/// </summary>
public class AuthResult
{
    private AuthResult(
        bool success,
        AuthResponse? data,
        string? error,
        IReadOnlyDictionary<string, string[]>? errors,
        Guid? userId)
    {
        Success = success;
        Data = data;
        Error = error;
        Errors = errors;
        UserId = userId;
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

    /// <summary>
    /// Người thực hiện thao tác xác thực — Controller dùng để ghi nhật ký đăng nhập
    /// (US 23, task B28). NULL khi không tra ra được tài khoản nào: đăng nhập thất bại
    /// với SĐT không tồn tại, hoặc các luồng không thuộc phạm vi ghi nhật ký
    /// (đăng ký, làm mới token) không truyền giá trị này.
    ///
    /// Không nằm trong AuthResponse trả cho client — chỉ lưu hành nội bộ giữa Service và Controller.
    /// </summary>
    public Guid? UserId { get; }

    public static AuthResult Ok(AuthResponse data) => new(true, data, null, null, null);

    /// <summary>Đăng nhập thành công kèm người thực hiện.</summary>
    public static AuthResult Ok(AuthResponse data, Guid userId) => new(true, data, null, null, userId);

    public static AuthResult Fail(string error) => new(false, null, error, null, null);

    /// <summary>
    /// Thất bại kèm người thực hiện nếu tra ra được — sai mật khẩu, tài khoản bị khoá.
    /// Không tra ra tài khoản nào (SĐT không tồn tại) thì truyền NULL.
    /// </summary>
    public static AuthResult Fail(string error, Guid? userId) => new(false, null, error, null, userId);

    public static AuthResult Fail(string error, IReadOnlyDictionary<string, string[]> errors)
        => new(false, null, error, errors, null);
}
