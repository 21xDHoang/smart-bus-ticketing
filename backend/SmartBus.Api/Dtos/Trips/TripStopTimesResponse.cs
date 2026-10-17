namespace SmartBus.Api.Dtos.Trips;

/// <summary>
/// Giờ đến của TẤT CẢ trạm trong một chuyến — hợp đồng ở docs/api-contract.md mục
/// "Giờ đến từng trạm — /routes/{routeId}/trips/{id}/stop-times".
///
/// <para>
/// Giờ dự kiến không lưu trong CSDL: mỗi lần đọc, service suy ra từ
/// <c>RouteStops.DistanceKm</c> cộng với giờ chạy của chuyến. Chỉ giờ do quản trị viên sửa
/// tay mới nằm ở bảng <c>TripStopTimeOverrides</c>.
/// </para>
/// </summary>
public class TripStopTimesResponse
{
    public Guid TripId { get; set; }

    public Guid RouteId { get; set; }

    /// <summary>Giờ khởi hành của chuyến — cũng là giờ đến trạm đầu.</summary>
    public DateTime DepartureTime { get; set; }

    /// <summary>Giờ đến bến cuối đang có hiệu lực (đã lưu, hoặc ước lượng từ km).</summary>
    public DateTime? ArrivalTime { get; set; }

    /// <summary>True khi <c>Trips.ArrivalTime</c> trống và giờ đến được ước lượng.</summary>
    public bool ArrivalTimeIsEstimated { get; set; }

    /// <summary>
    /// False khi tuyến/chuyến chưa có đủ km để suy ra giờ các trạm trung gian.
    /// Khi đó service KHÔNG bịa giờ: các mốc trung gian để null.
    /// </summary>
    public bool HasDistanceData { get; set; }

    /// <summary>Tổng km của các chặng (không tính khoảng cách của trạm đầu).</summary>
    public decimal TotalDistanceKm { get; set; }

    public List<TripStopTimeResponse> Stops { get; set; } = [];
}

/// <summary>Một trạm trong dòng thời gian của chuyến.</summary>
public class TripStopTimeResponse
{
    public Guid StopId { get; set; }

    public string StopName { get; set; } = string.Empty;

    public string StopAddress { get; set; } = string.Empty;

    public int StopOrder { get; set; }

    /// <summary>Khoảng cách từ trạm liền trước; trạm đầu luôn là 0.</summary>
    public decimal DistanceKm { get; set; }

    /// <summary>Khoảng cách cộng dồn tính từ trạm đầu.</summary>
    public decimal CumulativeDistanceKm { get; set; }

    /// <summary>Giờ đến đang có hiệu lực — giờ sửa tay nếu có, ngược lại là giờ dự kiến.</summary>
    public DateTime? ArrivalTime { get; set; }

    /// <summary>Giờ suy ra từ km, luôn được trả kể cả khi quản trị viên đã ghi đè.</summary>
    public DateTime? DerivedArrivalTime { get; set; }

    /// <summary>True khi giờ đến do quản trị viên đặt tay cho đúng chuyến này.</summary>
    public bool IsManual { get; set; }

    public bool IsFirst { get; set; }

    public bool IsLast { get; set; }
}
