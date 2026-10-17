using System.Net;
using System.Text.Json;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Options;

namespace SmartBus.Api.Services;

/// <summary>Nội dung một tin Web Push — thứ service worker của frontend đọc từ <c>event.data.json()</c>.</summary>
public sealed record WebPushMessage
{
    /// <summary>Dòng tiêu đề của thông báo hệ điều hành.</summary>
    public required string Title { get; init; }

    /// <summary>Nội dung thân — thông báo phải đọc được một mình (cùng luật nội dung bảng Notifications).</summary>
    public required string Body { get; init; }

    /// <summary>Đường dẫn trong app mở ra khi bấm thông báo (mặc định trang chủ ở sw.js).</summary>
    public string? Url { get; init; }

    /// <summary>Nhãn nhóm — thông báo cùng tag thay thế nhau thay vì xếp chồng trên thanh thông báo.</summary>
    public string? Tag { get; init; }
}

/// <summary>Kết cục một lượt gửi tới một push endpoint.</summary>
public enum PushDeliveryResult
{
    /// <summary>Push service nhận (2xx) — tin đã được giao cho trình duyệt xử lý.</summary>
    Sent,

    /// <summary>404/410 — subscription đã chết phía trình duyệt (hủy đăng ký, hết hạn), phải dọn khỏi store.</summary>
    Stale,

    /// <summary>Lỗi vận chuyển (mạng, 5xx, 429…) — KHÔNG phải lỗi nghiệp vụ, không được dọn subscription.</summary>
    Failed,
}

/// <summary>
/// Client HTTP thuần nói chuyện với push service (FCM/Mozilla/…) — lớp bọc mỏng quanh gói
/// <c>Lib.Net.Http.WebPush</c> 2.2.0: thư viện lo mã hoá RFC 8291 + ký VAPID RFC 8292 (kể cả claim
/// <c>sub</c> lấy từ <see cref="VapidAuthentication.Subject"/>), lớp này chỉ ánh xạ thiết bị/tin
/// của dự án sang hình dạng của thư viện và quy kết cục thành ba giá trị <see cref="PushDeliveryResult"/>.
///
/// Hợp đồng với người gọi: KHÔNG BAO GIỜ ném lỗi vận chuyển — mọi kết cục mạng (kể cả timeout)
/// gom về <see cref="PushDeliveryResult.Failed"/> để <see cref="WebPushSender"/> quyết định, vì
/// một thiết bị hỏng không được chặn các thiết bị còn lại. Riêng lỗi CẤU HÌNH (thiếu khoá VAPID,
/// endpoint sai dạng) vẫn ném: đó là lỗi lập trình phải sửa, không phải sự cố mạng (cùng lối
/// "thiếu khoá bí mật của cổng nào thì client cổng đó ném ngay khi được dựng" của MoMoGatewayService).
///
/// TTL: <see cref="PushMessage.TimeToLive"/> có sẵn nhưng chưa đặt tường minh — push service dùng
/// mặc định của nó (thường giữ tin tới 4 tuần). Xem danh sách việc còn lại ở docs/54 §5.
/// </summary>
public sealed class WebPushClient
{
    private readonly PushServiceClient _pushClient;
    private readonly WebPushOptions _options;
    private readonly ILogger<WebPushClient> _logger;

    public WebPushClient(HttpClient http, IOptions<WebPushOptions> options, ILogger<WebPushClient> logger)
    {
        _pushClient = new PushServiceClient(http);
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Gửi <paramref name="message"/> tới <paramref name="device"/> — xem doc lớp về hợp đồng lỗi.</summary>
    public async Task<PushDeliveryResult> SendAsync(
        WebPushSubscriptionInfo device,
        WebPushMessage message,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(message, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var subscription = new PushSubscription { Endpoint = device.Endpoint };
        subscription.SetKey(PushEncryptionKeyName.P256DH, device.P256Dh);
        subscription.SetKey(PushEncryptionKeyName.Auth, device.Auth);

        // Dựng VAPID ngay tại lượt gửi, không dựng ở constructor: host test resolve client để kiểm
        // tra đăng ký DI mà không cấu hình khoá, còn thư viện ném ngay khi khoá null. Lỗi CẤU HÌNH
        // vẫn ném đúng lúc gửi (hợp đồng của lớp), còn lúc khởi động app thật đã có
        // WebPushOptions.KiemTra chặn trước.
        var vapid = new VapidAuthentication(_options.PublicKey, _options.KhoaRiengThoBase64Url())
        {
            // Thiếu Subject là JWT VAPID không có claim "sub" — FCM có thể từ chối (403). Options
            // đã kiểm tra Subject lúc khởi động nên đây luôn có giá trị.
            Subject = _options.Subject,
        };

        try
        {
            await _pushClient.RequestPushMessageDeliveryAsync(
                subscription,
                new PushMessage(payload),
                vapid,
                cancellationToken);

            return PushDeliveryResult.Sent;
        }
        catch (PushServiceClientException ex)
            when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return PushDeliveryResult.Stale;
        }
        catch (PushServiceClientException ex)
        {
            _logger.LogWarning(
                "Push service trả {StatusCode} cho thiết bị {Endpoint} — giữ subscription, thử lại lần sau.",
                (int)ex.StatusCode,
                device.Endpoint);
            return PushDeliveryResult.Failed;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "Lỗi vận chuyển khi gửi push tới {Endpoint}.", device.Endpoint);
            return PushDeliveryResult.Failed;
        }
    }
}
