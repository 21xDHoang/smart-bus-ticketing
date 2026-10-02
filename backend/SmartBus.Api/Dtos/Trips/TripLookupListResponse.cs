namespace SmartBus.Api.Dtos.Trips;

/// <summary>
/// Kết quả phân trang của GET /api/trips — tra cứu danh sách chuyến theo ngày.
/// <see cref="Items"/> là trang hiện tại, <see cref="Total"/> là tổng số chuyến khớp bộ lọc —
/// frontend dùng <see cref="Total"/> để vẽ phân trang của AntD Table. Cùng khuôn
/// <see cref="TripListResponse"/>.
///
/// Cố ý đứng riêng thay vì dùng lại <see cref="TripListResponse"/>: items ở đây là
/// <see cref="TripLookupResponse"/> (có routeCode/routeName) chứ không phải
/// <see cref="TripResponse"/> của danh sách theo tuyến — hai hình dạng khác nhau.
/// </summary>
public class TripLookupListResponse
{
    public IReadOnlyList<TripLookupResponse> Items { get; set; } = [];

    public int Total { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }
}
