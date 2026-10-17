namespace SmartBus.Api.Dtos.MonthlyPasses.Admin;

/// <summary>
/// Kết quả phân trang của GET /api/monthly-passes. <see cref="Items"/> là trang hiện tại,
/// <see cref="Total"/> là tổng số dòng KHỚP BỘ LỌC (không phải số dòng trong
/// <see cref="Items"/>) — màn quản trị dùng <see cref="Total"/> để vẽ phân trang.
/// Cùng khuôn <see cref="Buses.BusListResponse"/>.
/// </summary>
public class MonthlyPassListResponse
{
    public IReadOnlyList<MonthlyPassAdminResponse> Items { get; set; } = [];

    public int Total { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }
}
