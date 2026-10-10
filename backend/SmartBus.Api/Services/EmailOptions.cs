namespace SmartBus.Api.Services;

/// <summary>
/// Cấu hình gửi email — đọc từ section <c>Email</c> của cấu hình (US 4, Sprint 3, task
/// *"Sinh file PDF vé có mã QR + gửi kèm qua email"*).
///
/// 🔴 LUẬT 2 — repo PUBLIC: <c>Password</c> là mật khẩu hộp thư gửi vé, KHÔNG BAO GIỜ nằm trong
/// code hay appsettings mẫu. Máy dev đặt qua <c>dotnet user-secrets</c>, khi deploy đặt qua biến
/// môi trường <c>Email__Password</c> — đúng khuôn VnPayOptions/ZaloPayOptions/TicketQrOptions.
///
/// Lỡ commit giá trị thật thì XOÁ FILE KHÔNG CỨU ĐƯỢC — phải vào nhà cung cấp email ĐỔI mật khẩu
/// ngay (repo public, bot quét commit trong vài phút).
/// </summary>
public class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>Máy chủ SMTP (ví dụ <c>smtp.gmail.com</c>).</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// Cổng SMTP — 587 (STARTTLS, mặc định hợp lệ nhất) hoặc 465 (SSL ngay từ đầu, khi đó đặt
    /// <see cref="UseStartTls"/> = false). 25 là cổng cũ, nhiều nhà cung cấp đã chặn.
    /// </summary>
    public int Port { get; set; } = 587;

    /// <summary>Tài khoản đăng nhập SMTP (thường chính là địa chỉ email gửi).</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Mật khẩu / ứng dụng mật khẩu của hộp thư gửi (luật 2 — xem doc lớp).</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Địa chỉ hiện trên dòng "Người gửi" của email vé.</summary>
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>Tên hiện kèm địa chỉ gửi — hành khách nhìn thấy "Smart Bus Ticketing" thay vì chuỗi email trần.</summary>
    public string FromName { get; set; } = "Smart Bus Ticketing";

    /// <summary>
    /// true = STARTTLS (nâng cấp lên TLS sau khi kết nối, hợp cổng 587) — mặc định.
    /// false = SSL/TLS ngay từ đầu (hợp cổng 465). Cả hai đường đều mã hoá; KHÔNG có đường gửi
    /// trần — email chứa vé là chứng từ, không được đi qua kênh không mã hoá.
    /// </summary>
    public bool UseStartTls { get; set; } = true;

    /// <summary>
    /// Thiếu mảnh nào trả tên mảnh đó để service báo lỗi đúng chỗ (cùng lối
    /// <see cref="VnPayOptions.MissingPiece"/>). Cổng ngoài khoảng hợp lệ cũng tính là thiếu cấu
    /// hình — nó không ném lúc đọc mà ném lúc gửi, xa chỗ khai báo, khó lần.
    /// </summary>
    public string? MissingPiece()
    {
        if (string.IsNullOrWhiteSpace(Host))
        {
            return "Email:Host";
        }

        if (Port is < 1 or > 65535)
        {
            return "Email:Port";
        }

        if (string.IsNullOrWhiteSpace(UserName))
        {
            return "Email:UserName";
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            return "Email:Password";
        }

        if (string.IsNullOrWhiteSpace(FromAddress))
        {
            return "Email:FromAddress";
        }

        return null;
    }
}
