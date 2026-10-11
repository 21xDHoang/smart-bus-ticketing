using SmartBus.Api.Dtos.SeatHolds;

namespace SmartBus.Api.Services;

/// <summary>
/// Lớp bọc phát sự kiện realtime cho <see cref="ISeatHoldReleaseService"/> — task *"Đồng bộ trạng thái
/// ghế realtime giữa các phiên (SignalR)"*, dòng 11 Sprint 3 (US 2).
///
/// Cùng lối <see cref="NotifyingSeatHoldCreateService"/> bọc service giữ ghế của Kiên:
/// <see cref="SeatHoldReleaseService"/> là file của Phùng Duy Hoàng (quy ước E1), lớp bọc đứng trước
/// để chỗ nhả ghế không phải biết gì về SignalR.
///
/// Phát khi <see cref="ServiceResult{T}.Success"/> — KỂ CẢ lượt gọi lại trên phiên đã kết thúc
/// (endpoint nhả là idempotent, vẫn trả 200): gọi lại vẫn là cùng đích đến "ghế đã tự do", client nhận
/// thêm một lời nhắc rồi gọi lại sơ đồ ghế và thấy không đổi — vô hại, và rẻ hơn hẳn một phép so
/// "lần nhả đầu hay lần nhả lại" ở đây. Nhánh 409 (phiên đã chốt thành vé) không đổi ghế nên không phát.
/// </summary>
public class NotifyingSeatHoldReleaseService : ISeatHoldReleaseService
{
    private readonly SeatHoldReleaseService _inner;

    private readonly ISeatMapNotifier _notifier;

    public NotifyingSeatHoldReleaseService(SeatHoldReleaseService inner, ISeatMapNotifier notifier)
    {
        _inner = inner;
        _notifier = notifier;
    }

    public async Task<ServiceResult<SeatHoldSessionResponse>> ReleaseAsync(
        Guid userId,
        string sessionCode,
        CancellationToken cancellationToken = default)
    {
        var result = await _inner.ReleaseAsync(userId, sessionCode, cancellationToken);

        // TripId chỉ có trong RESPONSE: đầu vào của nhả ghế là mã phiên, không phải chuyến — đây là
        // nguồn duy nhất biết phiên vừa trả ghế ở chuyến nào.
        if (result.Success && result.Data is { } data)
        {
            await _notifier.SeatMapChangedAsync(data.TripId);
        }

        return result;
    }
}
