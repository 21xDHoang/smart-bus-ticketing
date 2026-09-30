namespace SmartBus.Api.Dtos.Trips;

/// <summary>
/// Một trạm dừng trong chi tiết chuyến — khớp bảng <c>stops[]</c> ở mục "Chuyến xe — /trips"
/// của docs/api-contract.md.
///
/// Cùng hình dạng <see cref="RouteStops.RouteStopResponse"/> TRỪ hai trường:
///   • <c>id</c> — khoá của dòng bảng nối RouteStops. Chuyến KHÔNG sở hữu dòng đó: gỡ hay sửa
///     trạm là thao tác trên tuyến (<c>DELETE /routes/{routeId}/stops/{id}</c>), không phải trên
///     chuyến. Trả kèm ở đây là mời người gọi gửi nó vào một endpoint khác, nơi nó chỉ có nghĩa
///     khi đi kèm đúng routeId.
///   • <c>routeId</c> — đã có ở cấp ngoài cùng của <see cref="TripDetailResponse"/>, lặp lại ở
///     từng dòng chỉ làm response phình ra mà không thêm thông tin.
///
/// Thư mục đặt tên số nhiều "Trips": namespace số ít sẽ trùng tên entity <c>Trip</c> — cùng lý do
/// thư mục "Routes", "Fares" và "RouteStops".
/// </summary>
public class TripStopResponse
{
    /// <summary>Khoá của TRẠM. Không phải khoá của dòng RouteStops — xem chú thích ở lớp.</summary>
    public Guid StopId { get; set; }

    public string StopName { get; set; } = string.Empty;

    public string StopAddress { get; set; } = string.Empty;

    /// <summary>double precision, không bao giờ là float (quy ước A3).</summary>
    public double Latitude { get; set; }

    public double Longitude { get; set; }

    /// <summary>Thứ tự trạm trên tuyến, tính từ 1 và liên tục.</summary>
    public int StopOrder { get; set; }

    /// <summary>Khoảng cách từ trạm liền trước tới trạm này (km), cột numeric(6,2). Trạm đầu tiên = 0.</summary>
    public decimal DistanceKm { get; set; }
}
