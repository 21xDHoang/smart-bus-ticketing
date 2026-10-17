using System.Collections.Concurrent;

namespace SmartBus.Api.Services;

/// <summary>
/// Bản trong bộ nhớ của <see cref="IWebPushSubscriptionStore"/> — lý do chưa có bản EF (chờ
/// migration bảng thiết bị của Dăm) và cách đổi sang bản thật đã ghi ở doc interface.
///
/// Singleton + <see cref="ConcurrentDictionary"/>: store là chỗ dùng chung duy nhất giữa mọi
/// request gửi tin, không có scope nào để theo. Khoá lưu theo người dùng rồi theo thiết bị để hai
/// câu hỏi nóng nhất ("thiết bị của tài khoản này" và "đăng ký lại cùng endpoint") đều là tra từ
/// điển O(1); câu hiếm ("dòng có endpoint X" — chỉ chạy khi push service báo subscription chết)
/// quét thẳng toàn bộ — danh sách vài chục dòng, không đáng dựng chỉ mục thứ hai.
/// </summary>
public sealed class InMemoryWebPushSubscriptionStore : IWebPushSubscriptionStore
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, WebPushSubscriptionInfo>> _byUser = new();
    private readonly object _lock = new();

    /// <inheritdoc />
    public WebPushSubscriptionInfo Upsert(WebPushSubscriptionInfo subscription, out bool created)
    {
        lock (_lock)
        {
            var devices = _byUser.GetOrAdd(subscription.UserId, _ => new ConcurrentDictionary<Guid, WebPushSubscriptionInfo>());

            foreach (var (id, existing) in devices)
            {
                if (string.Equals(existing.Endpoint, subscription.Endpoint, StringComparison.Ordinal))
                {
                    devices[id] = subscription with { Id = existing.Id };
                    created = false;
                    return subscription with { Id = existing.Id };
                }
            }

            devices[subscription.Id] = subscription;
            created = true;
            return subscription;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<WebPushSubscriptionInfo> ListForUser(Guid userId)
        => _byUser.TryGetValue(userId, out var devices)
            ? devices.Values.OrderByDescending(d => d.CreatedAt).ToList()
            : [];

    /// <inheritdoc />
    public WebPushSubscriptionInfo? Find(Guid userId, Guid id)
        => _byUser.TryGetValue(userId, out var devices) && devices.TryGetValue(id, out var device)
            ? device
            : null;

    /// <inheritdoc />
    public bool Remove(Guid userId, Guid id)
    {
        lock (_lock)
        {
            return _byUser.TryGetValue(userId, out var devices) && devices.TryRemove(id, out _);
        }
    }

    /// <inheritdoc />
    public void RemoveByEndpoint(string endpoint)
    {
        lock (_lock)
        {
            foreach (var (_, devices) in _byUser)
            {
                foreach (var (id, existing) in devices)
                {
                    if (string.Equals(existing.Endpoint, endpoint, StringComparison.Ordinal))
                    {
                        devices.TryRemove(id, out _);
                    }
                }
            }
        }
    }
}
