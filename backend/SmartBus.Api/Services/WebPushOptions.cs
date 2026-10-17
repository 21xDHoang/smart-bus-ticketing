
using System.Buffers.Text;
using System.Security.Cryptography;

namespace SmartBus.Api.Services;

/// <summary>
/// Cấu hình Web Push (VAPID) — đọc từ section <c>WebPush</c> của cấu hình. Task *"Tích hợp Web
/// Push (PWA) để nhận thông báo trên trình duyệt"* (US 10 "Thông báo trạm", Sprint 4 dòng 42 —
/// Trần Trung Hiếu).
///
/// VAPID là cặp khoá P-256 định danh MÁY CHỦ gửi push: trình duyệt giữ khoá công khai lúc
/// subscribe, và kiểm chữ ký JWT trên mỗi lượt gửi. Một cặp khoá cho TOÀN BỘ ứng dụng — đổi
/// khoá là mọi thiết bị đã đăng ký đều phải subscribe lại, nên nhóm nên chọn MỘT cặp khoá
/// dùng chung (sinh một lần, chia sẻ qua chat nhóm như chuỗi kết nối CSDL).
///
/// Định dạng khoá — chuẩn chung của hệ sinh thái Web Push (gói <c>web-push</c> của Node trả về
/// đúng hai định dạng này khi sinh bằng <c>generateVAPIDKeys()</c>):
///   · <see cref="PublicKey"/>: điểm công khai UNCOMPRESSED 65 byte (0x04 ‖ x ‖ y), base64url.
///   · <see cref="PrivateKey"/>: khoá riêng PKCS#8 DER, base64url.
/// <c>Lib.Net.Http.WebPush</c> 2.2.0 lại nhận khoá riêng dạng SỐ MŨ THÔ 32 byte, nên
/// <see cref="KhoaRiengThoBase64Url"/> quy đổi ngay trước khi ký — cấu hình vẫn giữ PKCS#8 để
/// đổi thư viện/phiên bản không phải sinh lại khoá (mọi công cụ Web Push đều xuất PKCS#8).
/// Cách sinh (Node, có sẵn trên máy — frontend dùng npm):
///   node -e "const w=require('web-push'); console.log(w.generateVAPIDKeys())"
/// (cần cài web-push trước: npm install web-push) — hoặc bằng openssl (hướng dẫn từng bước:
/// docs/54-tich-hop-web-push-pwa.md §2).
///
/// 🔴 LUẬT 2 — repo PUBLIC: <see cref="PrivateKey"/> KHÔNG BAO GIỜ nằm trong code hay appsettings
/// mẫu. Máy dev đặt qua <c>dotnet user-secrets</c> hoặc appsettings.Development.json (đã bị
/// gitignore), khi deploy đặt qua biến môi trường <c>WebPush__PrivateKey</c> — đúng khuôn
/// VnPayOptions/ZaloPayOptions/TicketQrOptions.
/// </summary>
public class WebPushOptions
{
    public const string SectionName = "WebPush";

    /// <summary>
    /// Claim <c>sub</c> của JWT VAPID — định danh người gửi cho push service. Đặc tả yêu cầu dạng
    /// <c>mailto:</c> hoặc URL <c>https:</c>; giá trị này không phải bí mật (không có hộp thư nào
    /// phải tồn tại), chỉ để truy vết. Mặc định là địa chỉ đặt chỗ của dự án — nhóm đổi nếu muốn.
    /// </summary>
    public string Subject { get; set; } = "mailto:smartbus-ticketing@example.com";

    /// <summary>Điểm công khai uncompressed 65 byte, base64url — gửi cho trình duyệt lúc subscribe.</summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>Khoá riêng PKCS#8 DER, base64url — chỉ máy chủ giữ (luật 2 — xem doc lớp).</summary>
    public string PrivateKey { get; set; } = string.Empty;

    /// <summary>
    /// Khoá riêng ở định dạng thư viện gửi push cần: 32 byte số mũ thô (phần <c>d</c> của khoá
    /// P-256), base64url. Tách <c>d</c> khỏi PKCS#8 bằng chính .NET rồi mã hoá lại — khoá trong
    /// cấu hình vẫn là PKCS#8 chuẩn. Chỉ gọi lúc gửi tin nên host test resolve client mà không cần
    /// khoá không bị ném; cấu hình hỏng thì ném đúng lúc gửi (lỗi lập trình, không phải lỗi mạng).
    /// </summary>
    public string KhoaRiengThoBase64Url()
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(Base64Url.DecodeFromChars(PrivateKey), out _);

        var d = ecdsa.ExportParameters(includePrivateParameters: true).D;
        if (d is null || d.Length != 32)
        {
            throw new InvalidOperationException(
                "WebPush:PrivateKey phải là khoá P-256 PKCS#8 (số mũ 32 byte). Xem docs/54 §2.");
        }

        return Base64Url.EncodeToString(d);
    }

    /// <summary>
    /// Thiếu mảnh nào trả tên mảnh đó để chỗ khởi động báo lỗi đúng chỗ (cùng lối
    /// <see cref="EmailOptions.MissingPiece"/>). Khoá không đúng độ dài cũng tính là thiếu cấu
    /// hình — nó không ném lúc đọc mà ném lúc ký/đăng ký, xa chỗ khai báo, khó lần. Khoá giải mã
    /// không ra (chưa thay chuỗi khuôn mẫu, hoặc dán nhầm định dạng) cũng tính là thiếu: base64url
    /// nào cũng qua được phép so chuỗi, nhưng chỉ khoá đúng 65/32 byte mới dùng được.
    /// </summary>
    public string? MissingPiece()
    {
        if (string.IsNullOrWhiteSpace(Subject) || !Subject.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            return "WebPush:Subject";
        }

        if (!LaDiemCongKhai(PublicKey))
        {
            return "WebPush:PublicKey";
        }

        if (!LaSoMuBiMat(PrivateKey))
        {
            return "WebPush:PrivateKey";
        }

        return null;
    }

    /// <summary>
    /// Kiểm tra cấu hình Web Push ngay lúc khởi động — thiếu thì dừng app kèm hướng dẫn thay vì để
    /// lỗi nổ tận lúc khách bấm "Bật thông báo" (cùng lối <c>ChuoiKetNoiCsdl.KiemTra</c>). Gọi ở
    /// Program.cs, bỏ qua ở môi trường Testing: host test không cần khoá thật (test đặt qua
    /// UseSetting, và controller test không đi qua nhánh này).
    /// </summary>
    public static void KiemTra(IConfigurationSection section)
    {
        var missing = section.Get<WebPushOptions>()?.MissingPiece();
        if (missing is null)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Thiếu cấu hình Web Push ({missing}). Sinh một cặp khoá VAPID rồi điền vào mục WebPush " +
            "của appsettings.Development.json (máy local, file đã bị gitignore) hoặc biến môi trường " +
            "WebPush__PublicKey/WebPush__PrivateKey (deploy) — hướng dẫn từng bước ở " +
            "docs/54-tich-hop-web-push-pwa.md §2.");
    }

    /// <summary>Điểm công khai phải là điểm P-256 uncompressed 65 byte (0x04 ‖ x ‖ y), base64url.</summary>
    private static bool LaDiemCongKhai(string value)
    {
        try
        {
            var decoded = Base64Url.DecodeFromChars(value);
            return decoded.Length == 65 && decoded[0] == 0x04;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Khoá riêng phải giải mã base64url được và là PKCS#8 P-256 đọc được — kiểm trước ở
    /// đây thì lỗi nổ lúc khởi động, không phải lúc gửi tin đầu tiên.</summary>
    private static bool LaSoMuBiMat(string value)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportPkcs8PrivateKey(Base64Url.DecodeFromChars(value), out _);
            return ecdsa.KeySize == 256;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return false;
        }
    }
}
