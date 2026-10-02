using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Trips;

/// <summary>
/// Đầu vào của <c>ITripConflictService</c> (task story 14 "Service kiểm tra trùng lịch tài xế và
/// trùng xe giữa các chuyến" — Phùng Duy Hoàng): một khung giờ chạy kèm tài nguyên điều xe cần
/// soi trùng.
///
/// Chưa gắn endpoint nào — đây là DTO ở tầng service. API gán xe + tài xế vào chuyến (task 113 —
/// Nguyễn Duy Kiên) và API đổi xe/đổi tài xế khi có sự cố (task kế tiếp — Phùng Duy Hoàng) sẽ
/// bind nó khi có endpoint, nên các thuộc tính kiểm tra dữ liệu viết theo lối bind được từ HTTP
/// ngay từ đầu.
/// </summary>
public class TripConflictCheckRequest
{
    /// <summary>
    /// Xe cần kiểm tra trùng khung giờ. null = bỏ qua vế xe (ví dụ luồng chỉ đổi tài xế).
    /// </summary>
    public Guid? BusId { get; set; }

    /// <summary>
    /// Tài xế cần kiểm tra trùng khung giờ. null = bỏ qua vế tài xế.
    /// </summary>
    public Guid? DriverId { get; set; }

    /// <summary>
    /// Giờ khởi hành của chuyến đang gán — bắt buộc. Để <c>DateTimeOffset?</c> chứ không phải
    /// <c>DateTime</c>: thiếu hẳn trường này sẽ nhận giá trị mặc định và <c>[Required]</c> không
    /// bắt được — cùng lý do <see cref="CreateTripRequest.DepartureTime"/>.
    /// </summary>
    [Required(ErrorMessage = "Giờ khởi hành không được để trống")]
    public DateTimeOffset? DepartureTime { get; set; }

    /// <summary>
    /// Giờ dự kiến tới bến cuối — null khi chưa chốt, khi đó khung giờ coi như một mốc tại giờ
    /// khởi hành (cùng luật với phép kiểm tra "trùng khung giờ" khi tạo lịch trình).
    /// </summary>
    public DateTimeOffset? ArrivalTime { get; set; }

    /// <summary>
    /// Chuyến đang được gán — loại khỏi kết quả để nó không tự báo trùng chính mình.
    /// null = không loại chuyến nào.
    /// </summary>
    public Guid? ExcludeTripId { get; set; }
}
