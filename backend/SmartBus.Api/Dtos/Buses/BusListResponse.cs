namespace SmartBus.Api.Dtos.Buses;

/// <summary>
/// Kết quả phân trang của GET /api/buses — cùng khuôn với <see cref="Routes.RouteListResponse"/>.
/// </summary>
public class BusListResponse
{
    /// <summary>Các xe của trang hiện tại.</summary>
    public List<BusResponse> Items { get; set; } = [];

    /// <summary>Tổng số xe khớp bộ lọc — không phải số dòng trong <see cref="Items"/>.</summary>
    public int Total { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }
}
