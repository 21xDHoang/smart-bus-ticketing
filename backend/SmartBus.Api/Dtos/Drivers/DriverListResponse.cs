namespace SmartBus.Api.Dtos.Drivers;

/// <summary>
/// Kết quả phân trang của GET /api/drivers.
/// <see cref="Items"/> là trang hiện tại, <see cref="Total"/> là tổng số tài xế khớp bộ lọc —
/// frontend dùng <see cref="Total"/> để vẽ phân trang của AntD Table. Cùng khuôn
/// <see cref="Buses.BusListResponse"/>.
/// </summary>
public class DriverListResponse
{
    public IReadOnlyList<DriverResponse> Items { get; set; } = [];

    public int Total { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }
}
