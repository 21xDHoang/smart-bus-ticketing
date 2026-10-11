using Microsoft.AspNetCore.SignalR;

namespace SmartBus.Api.Hubs;

/// <summary>
/// Hub SignalR đồng bộ trạng thái ghế giữa các phiên — task *"Đồng bộ trạng thái ghế realtime giữa
/// các phiên (SignalR)"*, dòng 11 Sprint 3 (US 2). Hợp đồng đầy đủ ở mục "Đồng bộ ghế realtime —
/// /hubs/seat-map" của docs/api-contract.md.
///
/// Hub này KHÔNG trả dữ liệu: nó chỉ giữ danh sách client theo chuyến (nhóm "trip:{tripId}") và
/// nhận lệnh JoinTrip / LeaveTrip. Việc đẩy sự kiện nằm ở <see cref="Services.SeatMapNotifier"/> —
/// service nghiệp vụ không cầm Hub trực tiếp mà đi qua cổng <see cref="Services.ISeatMapNotifier"/>.
///
/// Ẩn danh (không [Authorize]), cùng mức với GET /trips/{id}/seats: sơ đồ ghế là thông tin khách
/// tra cứu được trước khi có tài khoản — xem "Vì sao hub ẩn danh" của hợp đồng.
/// </summary>
public class SeatMapHub : Hub<ISeatMapClient>
{
    /// <summary>
    /// Vào nhóm của một chuyến để nhận SeatMapChanged của chuyến đó. Gọi lại nhiều lần vô hại
    /// (AddToGroupAsync là idempotent) — cần thiết vì client phải gọi lại sau mỗi lần tự kết nối
    /// lại: đứt kết nối là mất hết nhóm đã vào.
    /// </summary>
    public Task JoinTrip(Guid tripId)
        => Groups.AddToGroupAsync(Context.ConnectionId, GroupOf(tripId));

    /// <summary>
    /// Rời nhóm của chuyến — gọi khi đổi chuyến hoặc rời màn hình. Chỉ là dọn sớm: ngắt kết nối
    /// SignalR cũng tự xoá mọi nhóm của connection đó.
    /// </summary>
    public Task LeaveTrip(Guid tripId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupOf(tripId));

    /// <summary>
    /// Tên nhóm của một chuyến — MỘT chỗ sinh tên nhóm duy nhất cho cả hub lẫn
    /// <see cref="Services.SeatMapNotifier"/>. Hai chỗ tự ghép chuỗi là hai chỗ có thể lệch nhau,
    /// và lệch ở đây nghĩa là sự kiện bắn vào một nhóm không có ai trong đó.
    /// </summary>
    internal static string GroupOf(Guid tripId) => $"trip:{tripId}";
}
