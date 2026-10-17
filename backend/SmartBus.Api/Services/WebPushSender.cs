namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt của <see cref="IWebPushSender"/> — hợp đồng không-bao-giờ-ném-lỗi và dọn subscription
/// chết đã ghi ở doc interface. Tách "gửi từng thiết bị" (<see cref="WebPushClient"/>) khỏi "gửi
/// cho một người" (lớp này) để client thuần chỉ biết HTTP, còn chỗ này biết store và chính sách dọn.
///
/// Scoped chứ không phải singleton: bản EF của store sau này (chờ migration của Dăm) sẽ cần
/// AppDbContext scoped, và đổi store không được buộc đổi vòng đời của sender.
/// </summary>
public sealed class WebPushSender : IWebPushSender
{
    private readonly WebPushClient _client;
    private readonly IWebPushSubscriptionStore _store;
    private readonly ILogger<WebPushSender> _logger;

    public WebPushSender(WebPushClient client, IWebPushSubscriptionStore store, ILogger<WebPushSender> logger)
    {
        _client = client;
        _store = store;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SendToUserAsync(Guid userId, WebPushMessage message, CancellationToken cancellationToken = default)
    {
        foreach (var device in _store.ListForUser(userId))
        {
            await SendToOneDeviceAsync(device, message, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<bool> SendToDeviceAsync(Guid userId, Guid deviceId, WebPushMessage message, CancellationToken cancellationToken = default)
    {
        var device = _store.Find(userId, deviceId);
        if (device is null)
        {
            return false;
        }

        await SendToOneDeviceAsync(device, message, cancellationToken);
        return true;
    }

    /// <summary>
    /// Gửi một thiết bị rồi dọn nếu subscription đã chết. Toàn bộ lỗi — kể cả lỗi CẤU HÌNH (thiếu
    /// khoá VAPID) — đều nuốt ở đây kèm log: hợp đồng của interface cấm ném, và lỗi cấu hình đã
    /// được KiemTra báo ngay lúc khởi động ở Program.cs chứ không cần ném lại giữa chừng.
    /// </summary>
    private async Task SendToOneDeviceAsync(WebPushSubscriptionInfo device, WebPushMessage message, CancellationToken cancellationToken)
    {
        try
        {
            if (await _client.SendAsync(device, message, cancellationToken) == PushDeliveryResult.Stale)
            {
                _store.RemoveByEndpoint(device.Endpoint);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gửi Web Push tới {Endpoint} thất bại — bỏ qua thiết bị này.", device.Endpoint);
        }
    }
}
