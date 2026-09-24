using BCryptHasher = BCrypt.Net.BCrypt;

namespace SmartBus.Api.Services;

/// <summary>
/// Nơi duy nhất gọi thư viện BCrypt khi cần tạo mới mật khẩu.
/// Task mã hóa mật khẩu BCrypt — story 22, Trần Trung Hiếu.
///
/// Lớp tĩnh vì không giữ trạng thái: băm và so khớp là hàm thuần.
/// Chỉ dùng cho luồng TẠO mật khẩu (đăng ký, đổi mật khẩu); việc kiểm tra
/// mật khẩu khi đăng nhập vẫn nằm trong <see cref="AuthService"/>.
/// </summary>
public static class PasswordService
{
    // BCrypt.Net-Next bật sẵn EnhancedEntropy (băm sơ bằng SHA-384 cho mật khẩu dài),
    // nên mật khẩu trên 72 byte không bị cắt cụt như BCrypt gốc.
    private const int WorkFactor = 12;

    /// <summary>Băm mật khẩu thô bằng BCrypt. KHÔNG lưu mật khẩu thô vào CSDL.</summary>
    public static string HashPassword(string plainPassword)
        => BCryptHasher.HashPassword(plainPassword, workFactor: WorkFactor);

    /// <summary>So khớp mật khẩu nhập vào với bản băm đã lưu.</summary>
    public static bool Verify(string plainPassword, string passwordHash)
        => BCryptHasher.Verify(plainPassword, passwordHash);
}
