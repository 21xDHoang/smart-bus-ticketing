namespace SmartBus.Api.Entities;

/// <summary>
/// Giờ đến một trạm trung gian do quản trị viên sửa tay cho <b>một chuyến cụ thể</b>.
/// Không lưu giờ dự kiến của mọi trạm — giờ dự kiến được suy ra từ
/// <c>RouteStops.DistanceKm</c> mỗi lần đọc; bảng này chỉ giữ <b>ngoại lệ</b>.
/// Trạm đầu và trạm cuối không có dòng ở đây: hai mốc đó là
/// <c>Trips.DepartureTime</c> / <c>Trips.ArrivalTime</c> (một nguồn sự thật duy nhất).
/// </summary>
public class TripStopTimeOverride
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TripId { get; set; }
    public Trip? Trip { get; set; }

    public Guid StopId { get; set; }
    public Stop? Stop { get; set; }

    /// <summary>Giờ đến do quản trị viên đặt, lưu UTC (cột <c>timestamptz</c>).</summary>
    public DateTime ArrivalTime { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}
