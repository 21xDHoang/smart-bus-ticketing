namespace SmartBus.Api.Dtos.Trips;

/// <summary>
/// Một chuyến xe — khớp mục "Lịch trình chạy xe — /routes/{routeId}/trips" của docs/api-contract.md.
/// Đổi tên trường ở đây là đổi hình dạng API: phải sửa api-contract.md trước rồi báo người viết
/// frontend (Băng, Hạnh, Thịnh).
/// </summary>
public class TripResponse
{
    public Guid Id { get; set; }

    /// <summary>Tuyến của chuyến.</summary>
    public Guid RouteId { get; set; }

    /// <summary>Xe chạy chuyến này.</summary>
    public Guid BusId { get; set; }

    /// <summary>
    /// Biển số xe — kèm sẵn để màn hình lập lịch trình hiển thị mà không phải gọi thêm API xe,
    /// cùng lối <c>RouteStopResponse</c> kèm sẵn tên trạm cho màn hình kéo-thả.
    /// </summary>
    public string BusLicensePlate { get; set; } = string.Empty;

    /// <summary>
    /// Tài xế được phân công cho chuyến — null khi chưa phân công.
    ///
    /// Chuyến sinh hàng loạt (<c>POST .../trips/generate</c>) ra đời ở trạng thái chưa phân công,
    /// việc gán làm sau bằng <c>PATCH .../trips/driver-assignment</c>. Đây là trường màn hình
    /// "Phân công điều xe" dùng để lọc ra các chuyến còn thiếu tài xế.
    /// </summary>
    public Guid? DriverId { get; set; }

    /// <summary>
    /// Họ tên tài xế — kèm sẵn để màn hình phân công hiển thị mà không phải gọi thêm
    /// <c>GET /drivers/{id}</c> cho từng dòng, cùng lối <see cref="BusLicensePlate"/>.
    ///
    /// null đúng khi và chỉ khi <see cref="DriverId"/> là null: không có bảng Drivers riêng
    /// (quy ước A8.4), tài xế là <c>User</c> mang vai trò Driver nên tên đọc từ <c>Users.FullName</c>.
    /// </summary>
    public string? DriverName { get; set; }

    /// <summary>Giờ khởi hành thực tế của chuyến — UTC, serialize ra ISO 8601 kèm hậu tố Z.</summary>
    public DateTime DepartureTime { get; set; }

    /// <summary>Giờ dự kiến tới bến cuối. null khi chưa chốt.</summary>
    public DateTime? ArrivalTime { get; set; }

    /// <summary>
    /// "Scheduled" | "Running" | "Completed" | "Cancelled" — tên chuỗi của enum
    /// <see cref="Entities.TripStatus"/>. Trả CHUỖI chứ không phải enum: Program.cs không đăng ký
    /// JsonStringEnumConverter nên kiểu enum sẽ serialize thành 0, 1, 2… — cùng lý do
    /// <see cref="Routes.RouteResponse.Status"/>.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Trạm gần nhất xe vừa đi qua — null khi chuyến chưa chạy (quy ước A8.6).</summary>
    public Guid? CurrentStopId { get; set; }

    /// <summary>Vĩ độ hiện tại của xe — double precision, không bao giờ là float (quy ước A3).</summary>
    public double? CurrentLat { get; set; }

    /// <summary>Kinh độ hiện tại của xe.</summary>
    public double? CurrentLng { get; set; }

    /// <summary>Lần cuối vị trí được cập nhật. null khi chưa từng báo vị trí.</summary>
    public DateTime? PositionUpdatedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>null khi chuyến chưa được sửa lần nào.</summary>
    public DateTime? UpdatedAt { get; set; }
}
