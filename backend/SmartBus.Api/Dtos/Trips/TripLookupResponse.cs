namespace SmartBus.Api.Dtos.Trips;

/// <summary>
/// Một chuyến xe trong kết quả tra cứu danh sách chuyến theo ngày — GET /api/trips.
/// Khớp mục "Chuyến xe — /trips" của docs/api-contract.md.
/// Đổi tên trường ở đây là đổi hình dạng API: phải sửa api-contract.md trước rồi báo người viết
/// frontend (Băng, Hạnh, Thịnh).
///
/// Kèm sẵn <see cref="RouteCode"/>/<see cref="RouteName"/> — khác danh sách chuyến của một tuyến
/// (<c>routeId</c> đã nằm trong đường dẫn), kết quả ở đây trải nhiều tuyến nên mỗi dòng phải tự
/// nói mình thuộc tuyến nào; cùng lối <see cref="Drivers.DriverTripResponse"/> kèm sẵn mã/tên
/// tuyến cho màn hình ca làm việc.
///
/// KHÔNG trả 4 trường vị trí (currentStopId/currentLat/currentLng/positionUpdatedAt): vị trí là
/// chuyện của nhóm story theo dõi thời gian thực (Sprint 3) — cùng lối
/// <see cref="TripDetailResponse"/> và <see cref="Drivers.DriverTripResponse"/>.
/// </summary>
public class TripLookupResponse
{
    public Guid Id { get; set; }

    /// <summary>Tuyến của chuyến.</summary>
    public Guid RouteId { get; set; }

    /// <summary>Mã tuyến hiển thị cho hành khách, ví dụ "01", "B10".</summary>
    public string RouteCode { get; set; } = string.Empty;

    /// <summary>Tên tuyến, ví dụ "Bến xe Mỹ Đình — Bến xe Gia Lâm".</summary>
    public string RouteName { get; set; } = string.Empty;

    /// <summary>Xe chạy chuyến này.</summary>
    public Guid BusId { get; set; }

    /// <summary>
    /// Biển số xe — kèm sẵn để màn hình danh sách hiển thị mà không phải gọi thêm API xe, cùng lối
    /// <see cref="TripResponse.BusLicensePlate"/>.
    /// </summary>
    public string BusLicensePlate { get; set; } = string.Empty;

    /// <summary>Giờ khởi hành thực tế của chuyến — UTC, serialize ra ISO 8601 kèm hậu tố Z.</summary>
    public DateTime DepartureTime { get; set; }

    /// <summary>Giờ dự kiến tới bến cuối. null khi chưa chốt.</summary>
    public DateTime? ArrivalTime { get; set; }

    /// <summary>
    /// "Scheduled" | "Running" | "Completed" | "Cancelled" — tên chuỗi của enum
    /// <see cref="Entities.TripStatus"/>. Trả CHUỖI chứ không phải enum — cùng lý do
    /// <see cref="TripResponse.Status"/>.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    /// <summary>null khi chuyến chưa được sửa lần nào.</summary>
    public DateTime? UpdatedAt { get; set; }
}
