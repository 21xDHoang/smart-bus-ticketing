namespace SmartBus.Api.Dtos.Trips;

/// <summary>
/// Chi tiết một chuyến xe — khớp mục "Chuyến xe — /trips" của docs/api-contract.md.
/// Đổi tên trường ở đây là đổi hình dạng API: phải sửa api-contract.md trước rồi báo người viết
/// frontend (⛔5).
///
/// Trả kèm dữ liệu của TUYẾN và của XE (không chỉ <see cref="RouteId"/>/<see cref="BusId"/>) để
/// màn hình chi tiết chuyến hiển thị được ngay mà không phải gọi thêm <c>/routes/{id}</c> rồi
/// tra xe — cùng lối <see cref="RouteStops.RouteStopResponse"/> trả kèm tên và địa chỉ trạm.
///
/// Cố ý KHÔNG có: vị trí hiện tại của xe (<c>currentLat</c>/<c>currentLng</c>/<c>currentStopId</c>)
/// — hình dạng response cho vị trí là chuyện của nhóm story theo dõi thời gian thực ở Sprint 3;
/// và <c>seatsRemaining</c> — cần bảng vé chưa migrate, là task của Hoàng trong story 1. Xem lý do
/// đầy đủ ở docs/api-contract.md.
/// </summary>
public class TripDetailResponse
{
    public Guid Id { get; set; }

    // ── Tuyến mà chuyến này chạy ───────────────────────────────────────────────────────────

    public Guid RouteId { get; set; }

    /// <summary>Mã tuyến hiển thị cho hành khách — "01", "B10"…</summary>
    public string RouteCode { get; set; } = string.Empty;

    /// <summary>Tên tuyến, ví dụ "Bến xe Mỹ Đình — Bến xe Gia Lâm".</summary>
    public string RouteName { get; set; } = string.Empty;

    /// <summary>Điểm đầu của tuyến — tên địa danh, không phải khoá ngoại tới Stops.</summary>
    public string Origin { get; set; } = string.Empty;

    /// <summary>Điểm cuối của tuyến.</summary>
    public string Destination { get; set; } = string.Empty;

    // ── Giờ chạy ───────────────────────────────────────────────────────────────────────────

    /// <summary>Giờ khởi hành. Cột timestamptz UTC — xem quy ước A3.</summary>
    public DateTime DepartureTime { get; set; }

    /// <summary>Giờ dự kiến tới bến cuối. null khi lúc sinh chuyến chưa chốt được.</summary>
    public DateTime? ArrivalTime { get; set; }

    /// <summary>
    /// "Scheduled" | "Running" | "Completed" | "Cancelled" — tên chuỗi của enum
    /// <see cref="Entities.TripStatus"/>.
    /// Trả về CHUỖI chứ không phải enum: Program.cs không đăng ký JsonStringEnumConverter, nên
    /// kiểu enum sẽ serialize thành 0, 1, 2… — đọc không hiểu, trái tinh thần quy ước A3.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    // ── Xe được gán cho chuyến ─────────────────────────────────────────────────────────────

    public Guid BusId { get; set; }

    /// <summary>Biển số xe — "29B-123.45". Duy nhất toàn hệ thống.</summary>
    public string LicensePlate { get; set; } = string.Empty;

    /// <summary>Loại xe — "Hyundai County 29 chỗ".</summary>
    public string BusType { get; set; } = string.Empty;

    /// <summary>Sức chứa của xe (số chỗ).</summary>
    public int Capacity { get; set; }

    /// <summary>
    /// "Active" | "Maintenance" | "Inactive" — tên chuỗi của enum <see cref="Entities.BusStatus"/>.
    /// Có mặt vì đây là cách duy nhất một xe rời khỏi đội (quy ước A4 cấm cột IsDeleted): chuyến
    /// vẫn Scheduled với xe đã vào bảo dưỡng, và người điều hành cần thấy đúng điều đó. Cột đã nằm
    /// sẵn trên dòng Bus được đọc cho <see cref="BusType"/> và <see cref="Capacity"/>.
    /// </summary>
    public string BusStatus { get; set; } = string.Empty;

    // ── Danh sách trạm dừng ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Các trạm của TUYẾN theo đúng thứ tự xe chạy, suy ra từ bảng nối RouteStops (quy ước A9 —
    /// không có bảng TripStops). Tuyến chưa gán trạm nào thì là mảng RỖNG, không phải null và
    /// cũng không phải 404.
    /// </summary>
    public IReadOnlyList<TripStopResponse> Stops { get; set; } = [];
}
