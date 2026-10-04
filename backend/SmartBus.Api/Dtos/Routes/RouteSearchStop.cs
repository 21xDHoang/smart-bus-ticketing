namespace SmartBus.Api.Dtos.Routes;

/// <summary>
/// Một trạm trên tuyến trong kết quả tìm tuyến — đủ để màn hình tra cứu vẽ lộ trình.
///
/// Cố ý gọn hơn <see cref="RouteStops.RouteStopResponse"/> (không có toạ độ, địa chỉ, khoảng
/// cách): màn hình tra cứu chỉ cần tên và thứ tự; cần toạ độ thì gọi
/// <c>GET /routes/{routeId}/stops</c>. Cố ý khác kiểu trạm của <c>TripDetail.stops[]</c>:
/// tuyến là của tuyến — giữ tên <c>stopId</c>/<c>stopName</c>/<c>stopOrder</c> để sau này màn
/// đặt vé (Sprint 3) dùng thẳng <c>stopId</c> làm điểm lên xuống mà không phải sửa hợp đồng.
/// </summary>
public class RouteSearchStop
{
    public Guid StopId { get; set; }

    public string StopName { get; set; } = string.Empty;

    /// <summary>Thứ tự trạm trên tuyến, tính từ 1 — khớp <c>RouteStop.StopOrder</c>.</summary>
    public int StopOrder { get; set; }
}
