namespace SmartBus.Api.Dtos.Trips;

/// <summary>
/// Kết quả đổi xe/tài xế — trạng thái phân công của chuyến SAU khi đổi, kèm cảnh báo trùng lịch.
///
/// Không lặp lại <c>createdAt</c>: ai cần hồ sơ đầy đủ của chuyến thì gọi <c>GET /trips/{id}</c>;
/// response này trả lời đúng câu hỏi của luồng sự cố — "chuyến giờ do xe nào, tài xế nào chạy,
/// thay đổi này có vướng chuyến khác không".
/// </summary>
public class TripAssignmentResponse
{
    public Guid Id { get; set; }

    public Guid RouteId { get; set; }

    public Guid BusId { get; set; }

    public string BusLicensePlate { get; set; } = string.Empty;

    /// <summary>null khi chuyến chưa phân công tài xế.</summary>
    public Guid? DriverId { get; set; }

    /// <summary>Tên tài xế kèm sẵn để màn hình hiển thị, không phải gọi thêm /drivers/{id}.</summary>
    public string? DriverName { get; set; }

    /// <summary>Giờ khởi hành — UTC, serialize ra ISO 8601 kèm hậu tố Z.</summary>
    public DateTime DepartureTime { get; set; }

    public DateTime? ArrivalTime { get; set; }

    /// <summary>"Scheduled" hoặc "Running" — hai trạng thái duy nhất đổi được phân công.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Lần cuối chuyến được sửa. Request không thay đổi gì (giá trị truyền vào trùng hiện tại)
    /// thì giữ nguyên — không đánh dấu sửa cho một thao tác không sửa gì.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Chuyến đang hoạt động trùng khung giờ với tài nguyên VỪA ĐỔI (tách vế xe / vế tài xế —
    /// cùng hình dạng kết quả kiểm tra trùng lịch điều xe của story 14).
    /// Cảnh báo, KHÔNG chặn: rỗng nghĩa là thay đổi không tạo xung đột mới nào.
    /// </summary>
    public TripConflictCheckResponse Conflicts { get; set; } = new();
}
