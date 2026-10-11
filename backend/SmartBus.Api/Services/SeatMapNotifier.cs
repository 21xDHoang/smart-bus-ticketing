using Microsoft.AspNetCore.SignalR;
using SmartBus.Api.Hubs;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="ISeatMapNotifier"/> — bắc cầu từ tầng nghiệp vụ sang Hub SignalR.
///
/// Dùng <see cref="IHubContext{THub,TClient}"/> chứ không phải Hub: người gọi là service sống NGOÀI
/// một kết nối cụ thể, và IHubContext mới là cửa để đẩy sự kiện tới client của NGƯỜI KHÁC — đúng
/// nhu cầu "phiên này thấy thay đổi do phiên kia gây ra". Đẩy qua kiểu mạnh <see cref="ISeatMapClient"/>
/// nên tên sự kiện không phải chuỗi trần ở đây; tên nhóm lấy từ <see cref="SeatMapHub.GroupOf"/>
/// (một chỗ sinh tên duy nhất).
///
/// ⚠️ Chưa có backplane: sự kiện chỉ chạm client nối vào CHÍNH máy chủ này — xem "Chưa có backplane
/// — giới hạn đã biết" của docs/api-contract.md.
/// </summary>
public class SeatMapNotifier : ISeatMapNotifier
{
    private readonly IHubContext<SeatMapHub, ISeatMapClient> _hub;

    private readonly ILogger<SeatMapNotifier> _logger;

    public SeatMapNotifier(
        IHubContext<SeatMapHub, ISeatMapClient> hub,
        ILogger<SeatMapNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task SeatMapChangedAsync(Guid tripId)
    {
        try
        {
            await _hub.Clients.Group(SeatMapHub.GroupOf(tripId)).SeatMapChanged(tripId);
        }
        catch (Exception ex)
        {
            // Nuốt lỗi có chủ đích (hợp đồng của ISeatMapNotifier): lời nhắc realtime là thứ phụ trợ,
            // còn người gọi đang ở cuối một thao tác nghiệp vụ đã thành công. Để lỗi kênh đẩy nổi lên
            // là biến một lượt giữ ghế thành công thành 500 — khách mất cả lượt giữ lẫn lời nhắc,
            // trong khi chỉ đáng mất lời nhắc (màn hình nào cũng bù được bằng lần tải trang kế tiếp).
            // Ghi log để sự cố kênh đẩy còn dấu vết, không im lặng tuyệt đối.
            _logger.LogWarning(ex, "Không đẩy được sự kiện SeatMapChanged cho chuyến {TripId}.", tripId);
        }
    }
}
