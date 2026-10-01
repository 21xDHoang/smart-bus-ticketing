namespace SmartBus.Api.Dtos.Drivers;

/// <summary>
/// Một ca làm việc của tài xế — một dòng Trips mà <c>Trips.DriverId</c> trỏ vào tài xế này.
/// Khớp mục "Hồ sơ tài xế — /drivers" của docs/api-contract.md.
///
/// Kèm sẵn routeCode/routeName để màn hình ca làm việc hiển thị mà không phải gọi thêm API
/// tuyến — cùng lối <see cref="Trips.TripResponse.BusLicensePlate"/> kèm sẵn biển số cho màn
/// hình lập lịch trình.
///
/// KHÔNG trả 4 trường vị trí (currentStopId/currentLat/currentLng/positionUpdatedAt): vị trí là
/// chuyện của nhóm story theo dõi thời gian thực (Sprint 3) — cùng lối <see cref="Trips.TripDetailResponse"/>.
/// </summary>
public class DriverTripResponse
{
    public Guid Id { get; set; }

    /// <summary>Tuyến của chuyến.</summary>
    public Guid RouteId { get; set; }

    public string RouteCode { get; set; } = string.Empty;

    public string RouteName { get; set; } = string.Empty;

    /// <summary>Xe chạy chuyến này.</summary>
    public Guid BusId { get; set; }

    public string BusLicensePlate { get; set; } = string.Empty;

    /// <summary>Giờ khởi hành thực tế của chuyến — UTC, serialize ra ISO 8601 kèm hậu tố Z.</summary>
    public DateTime DepartureTime { get; set; }

    /// <summary>Giờ dự kiến tới bến cuối. null khi chưa chốt.</summary>
    public DateTime? ArrivalTime { get; set; }

    /// <summary>
    /// "Scheduled" | "Running" | "Completed" | "Cancelled" — tên chuỗi của enum
    /// <see cref="Entities.TripStatus"/>. Trả CHUỖI chứ không phải enum — cùng lý do
    /// <see cref="Trips.TripResponse.Status"/>.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    /// <summary>null khi chuyến chưa được sửa lần nào.</summary>
    public DateTime? UpdatedAt { get; set; }
}
