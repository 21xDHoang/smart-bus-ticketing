using SmartBus.Api.Dtos.Auth;

namespace SmartBus.Api.Services;

/// <summary>Nghiệp vụ xác thực: đăng ký, đăng nhập, làm mới token, đăng xuất.</summary>
public interface IAuthService
{
    /// <summary>
    /// Tạo tài khoản Hành khách mới (mã hóa mật khẩu bằng BCrypt), đăng ký xong
    /// vào thẳng hệ thống — trả về cặp access/refresh token như đăng nhập.
    /// Trùng SĐT/email trả lỗi kèm tên trường để frontend gắn vào ô input.
    /// </summary>
    Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);

    /// <summary>Kiểm tra số điện thoại + mật khẩu, cấp cặp access/refresh token.</summary>
    Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Đổi refresh token cũ lấy cặp token mới. Token cũ bị thu hồi ngay khi dùng
    /// (xoay vòng token) nên mỗi refresh token chỉ dùng được đúng một lần.
    /// </summary>
    Task<AuthResult> RefreshTokenAsync(string rawRefreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Thu hồi refresh token — kết thúc phiên đăng nhập. Gọi lại nhiều lần vẫn an toàn.
    /// Trả về id người dùng sở hữu token để Controller ghi nhật ký đăng xuất (US 23, task B28);
    /// NULL khi token không tra được hoặc đã thu hồi từ trước — lúc đó không có phiên nào
    /// kết thúc và không có dữ liệu nào thay đổi.
    /// </summary>
    Task<Guid?> LogoutAsync(string rawRefreshToken, CancellationToken cancellationToken = default);
}
