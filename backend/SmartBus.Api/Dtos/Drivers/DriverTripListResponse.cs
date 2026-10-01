namespace SmartBus.Api.Dtos.Drivers;

/// <summary>
/// Kết quả phân trang của GET /api/drivers/{id}/trips — ca làm việc của tài xế.
/// <see cref="Items"/> là trang hiện tại, <see cref="Total"/> là tổng số ca khớp bộ lọc —
/// frontend dùng <see cref="Total"/> để vẽ phân trang của AntD Table. Cùng khuôn
/// <see cref="Trips.TripListResponse"/>.
/// </summary>
public class DriverTripListResponse
{
    public IReadOnlyList<DriverTripResponse> Items { get; set; } = [];

    public int Total { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }
}
