namespace SmartBus.Api.Dtos.Buses;

/// <summary>
/// Tóm tắt một chuyến của xe — dùng cho <see cref="BusResponse.CurrentTrip"/> và
/// <see cref="BusResponse.NextTrip"/>. Khớp mục "Xe buýt — /buses" của docs/api-contract.md.
///
/// Kèm sẵn mã/tên tuyến và tên tài xế để màn hình đội xe hiển thị mà không phải gọi thêm API
/// tuyến hay API tài xế cho từng dòng — cùng lối <c>TripResponse.BusLicensePlate</c>.
/// </summary>
public class BusTripSummaryResponse
{
    public Guid TripId { get; set; }

    public Guid RouteId { get; set; }

    /// <summary>Mã tuyến hiển thị cho hành khách — "01", "B10".</summary>
    public string RouteCode { get; set; } = string.Empty;

    /// <summary>Tên tuyến, ví dụ "Bến Thành — Chợ Lớn".</summary>
    public string RouteName { get; set; } = string.Empty;

    /// <summary>Giờ khởi hành của chuyến — UTC, serialize ra ISO 8601 kèm hậu tố Z.</summary>
    public DateTime DepartureTime { get; set; }

    /// <summary>Tài xế được phân công — null khi chưa phân công (chuyến sinh hàng loạt).</summary>
    public Guid? DriverId { get; set; }

    /// <summary>Họ tên tài xế; null đúng khi <see cref="DriverId"/> null.</summary>
    public string? DriverName { get; set; }

    /// <summary>
    /// "Scheduled" | "Running" | "Completed" | "Cancelled" — tên chuỗi của enum
    /// <see cref="Entities.TripStatus"/>, không phải số thứ tự.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Trạm gần nhất xe đã đi qua (<c>Trips.CurrentStopId</c>) — chỉ có nghĩa với chuyến đang chạy.
    /// Null ở <see cref="BusResponse.NextTrip"/> và khi chuyến chưa báo vị trí.
    /// </summary>
    public string? CurrentStopName { get; set; }
}
