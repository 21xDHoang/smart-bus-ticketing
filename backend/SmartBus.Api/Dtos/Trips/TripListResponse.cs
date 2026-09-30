namespace SmartBus.Api.Dtos.Trips;

/// <summary>
/// Kết quả phân trang của GET /api/routes/{routeId}/trips.
/// <see cref="Items"/> là trang hiện tại, <see cref="Total"/> là tổng số chuyến khớp bộ lọc —
/// frontend dùng <see cref="Total"/> để vẽ phân trang của AntD Table. Cùng khuôn
/// <see cref="Routes.RouteListResponse"/>.
/// </summary>
public class TripListResponse
{
    public IReadOnlyList<TripResponse> Items { get; set; } = [];

    public int Total { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }
}
