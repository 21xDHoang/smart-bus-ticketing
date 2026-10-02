namespace SmartBus.Api.Dtos.Trips;

/// <summary>
/// Một chuyến đang hoạt động trùng khung giờ với tài nguyên điều xe đang xét — đủ thông tin để
/// người gọi dựng thông báo dạng "trùng với chuyến 06:00 tuyến 01" mà không phải gọi thêm API nào.
///
/// Không kèm biển số xe / tên tài xế của chuyến trùng: chủ thể của thông báo là tài nguyên ĐANG
/// XÉT (người gọi đã có sẵn), còn chuyến trùng chỉ cần định danh + tuyến + khung giờ.
/// </summary>
public class ConflictingTripResponse
{
    public Guid Id { get; set; }

    public Guid RouteId { get; set; }

    /// <summary>Mã tuyến — kèm sẵn cùng lối <see cref="TripLookupResponse.RouteCode"/>.</summary>
    public string RouteCode { get; set; } = string.Empty;

    /// <summary>Tên tuyến — cần cho thông báo khi chuyến trùng nằm ở TUYẾN KHÁC (trùng xe giữa hai tuyến).</summary>
    public string RouteName { get; set; } = string.Empty;

    /// <summary>Xe của chuyến trùng — để người gọi biết chuyến này khớp ở vế xe hay không.</summary>
    public Guid BusId { get; set; }

    /// <summary>Tài xế của chuyến trùng — null khi chuyến chưa được phân công tài xế.</summary>
    public Guid? DriverId { get; set; }

    /// <summary>Giờ khởi hành — UTC, serialize ra ISO 8601 kèm hậu tố Z.</summary>
    public DateTime DepartureTime { get; set; }

    /// <summary>Giờ dự kiến tới bến cuối. null khi chưa chốt.</summary>
    public DateTime? ArrivalTime { get; set; }

    /// <summary>
    /// "Scheduled" hoặc "Running" — hai trạng thái duy nhất lọt qua bộ lọc; Cancelled/Completed
    /// không chiếm chỗ trên thời gian biểu nữa.
    /// </summary>
    public string Status { get; set; } = string.Empty;
}
