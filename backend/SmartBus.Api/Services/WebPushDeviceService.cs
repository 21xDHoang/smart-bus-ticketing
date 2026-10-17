using System.Buffers.Text;
using Microsoft.Extensions.Options;
using SmartBus.Api.Dtos.WebPush;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt của <see cref="IWebPushDeviceService"/> — phần "hình dạng" của nghiệp vụ thiết bị: kiểm
/// dữ liệu vào, ánh xạ store ↔ DTO. Phần "gửi tin" nằm ở <see cref="WebPushSender"/> (hợp đồng
/// không-bao-giờ-ném-lỗi), phần "để đâu" nằm ở <see cref="IWebPushSubscriptionStore"/>.
///
/// Kiểm sâu cho ba trường khoá: endpoint phải là URL https tuyệt đối (chặn nhận một URL mở để máy
/// chủ POST tới), p256dh phải giải mã được thành điểm uncompressed 65 byte (nếu không thì mã hoá
/// ném tận lúc gửi — xa chỗ nhập, khó lần), auth phải là đúng 16 byte (đầu vào của HKDF, độ dài
/// khác là lỗi mã hoá ngầm). Cả ba đều không diễn đạt được bằng DataAnnotations nên nằm ở đây.
/// </summary>
public sealed class WebPushDeviceService : IWebPushDeviceService
{
    private readonly WebPushOptions _options;
    private readonly IWebPushSubscriptionStore _store;
    private readonly IWebPushSender _sender;

    public WebPushDeviceService(
        IOptions<WebPushOptions> options,
        IWebPushSubscriptionStore store,
        IWebPushSender sender)
    {
        _options = options.Value;
        _store = store;
        _sender = sender;
    }

    /// <inheritdoc />
    public string GetVapidPublicKey() => _options.PublicKey;

    /// <inheritdoc />
    public IReadOnlyList<WebPushDeviceResponse> ListMine(Guid userId)
        => _store.ListForUser(userId).Select(ToResponse).ToList();

    /// <inheritdoc />
    public ServiceResult<WebPushRegisterOutcome> Register(Guid userId, RegisterWebPushDeviceRequest request, string userAgent)
    {
        // Kiểm cả ba rồi gom lỗi — không nối bằng ||: chạy lười thì biến out của vế sau có thể
        // chưa được gán, và gom lỗi muốn BÁO CẢ BA lỗi cùng lúc chứ không phải lỗi đầu tiên.
        var hasEndpointError = TryKiemEndpoint(request.Endpoint, out var endpointError);
        var hasP256DhError = TryKiemP256Dh(request.P256Dh, out var p256DhError);
        var hasAuthError = TryKiemAuth(request.Auth, out var authError);

        if (hasEndpointError || hasP256DhError || hasAuthError)
        {
            var errors = new Dictionary<string, string[]>();
            if (endpointError is not null)
            {
                errors["endpoint"] = [endpointError];
            }

            if (p256DhError is not null)
            {
                errors["p256Dh"] = [p256DhError];
            }

            if (authError is not null)
            {
                errors["auth"] = [authError];
            }

            return ServiceResult<WebPushRegisterOutcome>.Invalid("Thiết bị đăng ký không hợp lệ", errors);
        }

        var device = new WebPushSubscriptionInfo
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Endpoint = request.Endpoint,
            P256Dh = request.P256Dh,
            Auth = request.Auth,
            ExpirationTime = request.ExpirationTime,
            UserAgent = userAgent,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var saved = _store.Upsert(device, out var created);
        return ServiceResult<WebPushRegisterOutcome>.Ok(new WebPushRegisterOutcome(ToResponse(saved), created));
    }

    /// <inheritdoc />
    public ServiceResult<bool> Remove(Guid userId, Guid deviceId)
        => _store.Remove(userId, deviceId)
            ? ServiceResult<bool>.Ok(true)
            : ServiceResult<bool>.NotFound("Thiết bị không tồn tại hoặc không phải của bạn.");

    /// <inheritdoc />
    public async Task<ServiceResult<bool>> SendTestAsync(Guid userId, Guid deviceId, CancellationToken cancellationToken)
    {
        var device = _store.Find(userId, deviceId);
        if (device is null)
        {
            return ServiceResult<bool>.NotFound("Thiết bị không tồn tại hoặc không phải của bạn.");
        }

        await _sender.SendToDeviceAsync(userId, deviceId, new WebPushMessage
        {
            Title = "Smart Bus — Thông báo thử",
            Body = "Thiết bị này đã nhận được Web Push. Bạn sẽ nhận thông báo như thế này khi xe sắp đến trạm hoặc có thay đổi lịch trình.",
            Url = "/notifications",
            Tag = "smartbus-test",
        }, cancellationToken);

        return ServiceResult<bool>.Ok(true);
    }

    private static WebPushDeviceResponse ToResponse(WebPushSubscriptionInfo device) => new()
    {
        Id = device.Id,
        Endpoint = device.Endpoint,
        ExpirationTime = device.ExpirationTime,
        UserAgent = device.UserAgent,
        CreatedAt = device.CreatedAt,
    };

    private static bool TryKiemEndpoint(string endpoint, out string? error)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host) || !uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
        {
            error = "Push endpoint phải là URL https tuyệt đối.";
            return true;
        }

        error = null;
        return false;
    }

    private static bool TryKiemP256Dh(string p256Dh, out string? error)
    {
        if (!TryGiaiBase64Url(p256Dh, out var decoded) || decoded.Length != 65 || decoded[0] != 0x04)
        {
            error = "p256dh phải là điểm P-256 uncompressed 65 byte dạng base64url.";
            return true;
        }

        error = null;
        return false;
    }

    private static bool TryKiemAuth(string auth, out string? error)
    {
        if (!TryGiaiBase64Url(auth, out var decoded) || decoded.Length != 16)
        {
            error = "auth phải là 16 byte dạng base64url.";
            return true;
        }

        error = null;
        return false;
    }

    private static bool TryGiaiBase64Url(string value, out byte[] decoded)
    {
        try
        {
            decoded = Base64Url.DecodeFromChars(value);
            return true;
        }
        catch (FormatException)
        {
            decoded = [];
            return false;
        }
    }
}
