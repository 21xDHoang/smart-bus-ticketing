namespace SmartBus.Api.Dtos.Trips;

/// <summary>
/// Kết quả gán tài xế hàng loạt — PATCH /api/routes/{routeId}/trips/driver-assignment
/// (US 14, Nguyễn Duy Kiên). Hợp đồng ở mục "Lịch trình chạy xe — /routes/{routeId}/trips"
/// của docs/api-contract.md.
///
/// Mỗi phần tử <see cref="Items"/> giữ ĐÚNG khuôn của response
/// <c>PATCH /trips/{id}/assignment</c> (API đổi xe/đổi tài xế khi có sự cố — Phùng Duy Hoàng):
/// cùng bộ trường chuyến + <c>conflicts</c>. Hai endpoint anh em, một hình dạng — người viết
/// frontend học một lần là dùng được cả hai.
/// </summary>
public class DriverAssignmentResponse
{
    /// <summary>Tài xế vừa được gán cho cả lô.</summary>
    public Guid DriverId { get; set; }

    /// <summary>
    /// Họ tên tài xế — kèm sẵn để màn hình hiện thông báo "đã phân công cho …" mà không phải
    /// gọi thêm API, cùng lối <see cref="TripResponse.DriverName"/>.
    /// </summary>
    public string DriverName { get; set; } = string.Empty;

    /// <summary>
    /// Số chuyến THỰC SỰ được đổi tài xế trong lần gọi này. Chuyến đã đúng tài xế đó từ trước
    /// không tính (và <c>updatedAt</c> của nó giữ nguyên) — gọi lại y hệt lần hai trả 0.
    /// Bằng <see cref="Items"/>.Count trừ đi số chuyến đã đúng tài xế sẵn.
    /// </summary>
    public int AssignedCount { get; set; }

    /// <summary>Kết quả từng chuyến, theo đúng thứ tự trong <c>tripIds</c> mà client gửi lên.</summary>
    public List<DriverAssignmentItemResponse> Items { get; set; } = [];
}

/// <summary>Một chuyến sau khi gán tài xế — cùng khuôn <c>TripAssignmentResponse</c> của Hoàng.</summary>
public class DriverAssignmentItemResponse
{
    public Guid Id { get; set; }

    public Guid RouteId { get; set; }

    public Guid BusId { get; set; }

    /// <summary>Biển số xe — kèm sẵn cùng lý do <see cref="TripResponse.BusLicensePlate"/>.</summary>
    public string BusLicensePlate { get; set; } = string.Empty;

    /// <summary>Tài xế của chuyến sau khi gán. Không bao giờ null ở response này.</summary>
    public Guid DriverId { get; set; }

    public string DriverName { get; set; } = string.Empty;

    public DateTime DepartureTime { get; set; }

    public DateTime? ArrivalTime { get; set; }

    /// <summary>Tên chuỗi của enum <see cref="Entities.TripStatus"/> — cùng lý do
    /// <see cref="TripResponse.Status"/>.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>null khi chuyến chưa được sửa lần nào (chuyến đã đúng tài xế đó từ trước).</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Cảnh báo trùng lịch — KHÔNG phải lỗi: thao tác vẫn thành công (200) kể cả khi có mục ở đây,
    /// vì luồng điều hành được phép cố ý chấp nhận trùng (cùng triết lý với việc
    /// <c>PUT /routes/{routeId}/trips/{id}</c> cố ý không chặn trùng khung giờ).
    ///
    /// <c>busConflicts</c> luôn rỗng: endpoint này chỉ đổi tài xế, không soi vế xe.
    /// <c>driverConflicts</c> gồm các chuyến ĐÃ mang tài xế này trong CSDL (qua
    /// <see cref="ITripConflictService"/>) và các chuyến KHÁC trong cùng lô vừa gán trùng khung giờ
    /// với nhau — gộp lại, mỗi chuyến chỉ xuất hiện một lần.
    /// </summary>
    public TripConflictCheckResponse Conflicts { get; set; } = new();
}
