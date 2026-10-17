using SmartBus.Api.Dtos.WebPush;

namespace SmartBus.Api.Services;

/// <summary>Kết quả một lượt đăng ký thiết bị — <see cref="Created"/> phân biệt 201 (thiết bị mới) và 200 (đăng ký lại).</summary>
public sealed record WebPushRegisterOutcome(WebPushDeviceResponse Device, bool Created);

/// <summary>
/// Nghiệp vụ thiết bị Web Push của chính người gọi — đăng ký, liệt kê, gỡ và gửi tin thử. Task
/// *"Tích hợp Web Push (PWA) để nhận thông báo trên trình duyệt"* (Sprint 4 dòng 42 — Trần Trung
/// Hiếu). Phạm vi "của tôi" kiểm ngay trong truy vấn theo userId (cùng lối /refunds, /tickets/me):
/// thiết bị của người khác và thiết bị không tồn tại cùng trả 404, không để lộ thiết bị nào đang
/// đăng ký.
/// </summary>
public interface IWebPushDeviceService
{
    /// <summary>Khoá công khai VAPID cho trình duyệt dùng lúc <c>PushManager.subscribe()</c>.</summary>
    string GetVapidPublicKey();

    /// <summary>Danh sách thiết bị của chính người gọi, mới nhất trước. Chưa có thiết bị nào → mảng rỗng.</summary>
    IReadOnlyList<WebPushDeviceResponse> ListMine(Guid userId);

    /// <summary>
    /// Đăng ký (hoặc đăng ký lại) một thiết bị. Đăng ký lại cùng endpoint thì ghi đè khoá — trình
    /// duyệt có thể cấp lại khoá cho cùng một subscription — và trả 200 thay vì thêm dòng thứ hai.
    /// </summary>
    ServiceResult<WebPushRegisterOutcome> Register(Guid userId, RegisterWebPushDeviceRequest request, string userAgent);

    /// <summary>Gỡ thiết bị — thiết bị không tồn tại hoặc của người khác → NotFound (một câu).</summary>
    ServiceResult<bool> Remove(Guid userId, Guid deviceId);

    /// <summary>Gửi tin thử tới đúng một thiết bị — thiết bị không tồn tại/của người khác → NotFound.</summary>
    Task<ServiceResult<bool>> SendTestAsync(Guid userId, Guid deviceId, CancellationToken cancellationToken);
}
