namespace SmartBus.Api.Dtos.Trips;

/// <summary>
/// Một dòng kết quả của GET /api/trips/search — màn hình kết quả tìm kiếm của hành khách
/// (story 1). Hợp đồng đầy đủ ở mục "GET /trips/search" của docs/api-contract.md.
///
/// <see cref="Price"/> là giá vé phổ thông (PassengerType.Standard) của tuyến, null khi tuyến
/// chưa cấu hình giá. <see cref="SeatsRemaining"/> hôm nay luôn bằng <see cref="Capacity"/>:
/// bảng vé/giữ chỗ chưa migrate (Sprint 3 mới có) nên chưa có gì để trừ — cùng ghi chú ở hợp đồng.
/// </summary>
public class TripSearchResult
{
    public Guid Id { get; set; }

    public Guid RouteId { get; set; }

    public string RouteCode { get; set; } = string.Empty;

    public string RouteName { get; set; } = string.Empty;

    public DateTime DepartureTime { get; set; }

    /// <summary>Giờ đến dự kiến; chuyến chưa chốt giờ đến → null (cột nullable trên Trips).</summary>
    public DateTime? ArrivalTime { get; set; }

    /// <summary>Giá vé phổ thông của tuyến (đồng). Tuyến chưa cấu hình giá → null.</summary>
    public decimal? Price { get; set; }

    public int SeatsRemaining { get; set; }

    public int Capacity { get; set; }

    public string BusType { get; set; } = string.Empty;
}
