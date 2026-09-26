namespace SmartBus.Api.Dtos.RouteStops;

/// <summary>
/// Một dòng bảng nối RouteStops — một trạm nằm trên một tuyến, kèm thứ tự chạy.
/// Khớp mục "Trạm trên tuyến — /routes/{routeId}/stops" của docs/api-contract.md.
///
/// Trả kèm tên, địa chỉ và toạ độ của trạm (không chỉ <see cref="StopId"/>) để màn hình
/// kéo-thả hiển thị được danh sách trạm mà không phải gọi thêm /stops rồi tự ghép. Dữ liệu
/// trạm ở đây chỉ để đọc — muốn sửa trạm thì gọi PUT /stops/{id} của StopsController.
///
/// Thư mục đặt tên số nhiều "RouteStops": namespace số ít sẽ trùng tên entity RouteStop
/// (cùng lý do thư mục "Routes" và "Fares").
/// </summary>
public class RouteStopResponse
{
    /// <summary>
    /// Khoá chính của DÒNG BẢNG NỐI. KHÁC <see cref="StopId"/> — xem chú thích ở đó.
    /// Đây mới là giá trị mà DELETE /routes/{routeId}/stops/{id} nhận.
    /// </summary>
    public Guid Id { get; set; }

    public Guid RouteId { get; set; }

    /// <summary>
    /// Khoá của TRẠM. Dùng cho POST /routes/{routeId}/stops và PUT /routes/{routeId}/stops/order
    /// (hai endpoint này nhận <c>stopId</c>), KHÔNG dùng cho DELETE — DELETE nhận
    /// <see cref="Id"/> của dòng bảng nối. Gửi nhầm giá trị này vào DELETE là 404.
    /// </summary>
    public Guid StopId { get; set; }

    public string StopName { get; set; } = string.Empty;

    public string StopAddress { get; set; } = string.Empty;

    /// <summary>double precision, không bao giờ là float (quy ước A3).</summary>
    public double Latitude { get; set; }

    public double Longitude { get; set; }

    /// <summary>Thứ tự trạm trên tuyến, tính từ 1 và liên tục.</summary>
    public int StopOrder { get; set; }

    /// <summary>
    /// Khoảng cách từ trạm liền trước tới trạm này (km), cột numeric(6,2).
    /// Trạm đầu tiên = 0.
    /// </summary>
    public decimal DistanceKm { get; set; }
}
