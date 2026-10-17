using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.WebPush;

/// <summary>
/// Body của POST /api/web-push/devices — đăng ký một thiết bị trình duyệt nhận Web Push. Ba trường
/// chính là nguyên xi kết quả <c>PushSubscription.toJSON()</c> của trình duyệt (endpoint + hai khoá)
/// — frontend không phải tự bịa hình dạng nào (US 10 "Thông báo trạm", Sprint 4 dòng 42 — Trần
/// Trung Hiếu). Hợp đồng: docs/api-contract.md mục "Web Push — /web-push".
///
/// Kiểm hình dạng chia hai tầng như các DTO khác của dự án: <c>[Required]</c> bắt chỗ thiếu trường
/// (ModelState), còn tầng service kiểm sâu (endpoint phải https, khoá đúng độ dài và giải mã được)
/// vì DataAnnotations không diễn đạt nổi các phép kiểm đó.
/// </summary>
public class RegisterWebPushDeviceRequest
{
    /// <summary>
    /// Push endpoint do trình duyệt cấp — nơi máy chủ POST tin tới. Bắt buộc, phải là URL tuyệt
    /// đối <c>https</c> (mọi push service thật đều https; nới cho http nghĩa là nhận một URL mở).
    /// </summary>
    [Required(ErrorMessage = "Push endpoint là bắt buộc")]
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Khoá công khai P-256 của thiết bị — điểm uncompressed 65 byte, base64url.</summary>
    [Required(ErrorMessage = "Khoá p256dh của thiết bị là bắt buộc")]
    public string P256Dh { get; set; } = string.Empty;

    /// <summary>Bí mật xác thực 16 byte của thiết bị, base64url.</summary>
    [Required(ErrorMessage = "Bí mật auth của thiết bị là bắt buộc")]
    public string Auth { get; set; } = string.Empty;

    /// <summary>Ngày hết hạn do trình duyệt báo (subscription có thể hẹn giờ); null = không hẹn giờ.</summary>
    public DateTimeOffset? ExpirationTime { get; set; }
}
