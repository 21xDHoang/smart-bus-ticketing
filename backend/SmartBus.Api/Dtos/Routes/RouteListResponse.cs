namespace SmartBus.Api.Dtos.Routes;

/// <summary>
/// Kết quả phân trang của GET /api/routes.
/// <see cref="Items"/> là trang hiện tại, <see cref="Total"/> là tổng số dòng khớp bộ lọc —
/// frontend dùng <see cref="Total"/> để vẽ phân trang của AntD Table.
/// </summary>
public class RouteListResponse
{
    public IReadOnlyList<RouteResponse> Items { get; set; } = [];

    public int Total { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }
}
