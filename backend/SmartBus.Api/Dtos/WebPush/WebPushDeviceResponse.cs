namespace SmartBus.Api.Dtos.WebPush;

/// <summary>
/// Hình dạng một thiết bị đã đăng ký Web Push trả về cho frontend — GET /api/web-push/devices
/// (danh sách thiết bị của tôi) và POST /api/web-push/devices (dòng vừa đăng ký). (US 10 "Thông
/// báo trạm", Sprint 4 dòng 42 — Trần Trung Hiếu.)
///
/// <see cref="Endpoint"/> bị cắt ngắn bởi frontend khi hiển thị (chuỗi FCM dài ~150 ký tự chứa
/// mã đăng ký); nó là khoá nghiệp vụ để DELETE và để máy chủ gửi tin, không phải dòng đọc.
/// Không trả hai khoá <c>p256dh</c>/<c>auth</c>: chúng chỉ máy chủ cần khi mã hoá, trả ra chỉ
/// thêm bề mặt lộ khoá.
/// </summary>
public class WebPushDeviceResponse
{
    /// <summary>Khoá của lượt đăng ký thiết bị — dùng cho DELETE /api/web-push/devices/{id}.</summary>
    public Guid Id { get; set; }

    /// <summary>Push endpoint — nơi máy chủ gửi tin tới thiết bị này.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Ngày hết hạn trình duyệt báo; null = không hẹn giờ.</summary>
    public DateTimeOffset? ExpirationTime { get; set; }

    /// <summary>Chuỗi User-Agent của trình duyệt lúc đăng ký — để màn cài đặt phân biệt được thiết bị.</summary>
    public string UserAgent { get; set; } = string.Empty;

    /// <summary>Thời điểm đăng ký (hoặc đăng ký lại) gần nhất (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
