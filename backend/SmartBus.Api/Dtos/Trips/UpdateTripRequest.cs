using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Trips;

/// <summary>
/// Body của PUT /api/routes/{routeId}/trips/{id} — sửa một chuyến đã có.
/// Body gồm đủ 2 trường bắt buộc của <see cref="CreateTripRequest"/> cộng thêm <c>status</c> —
/// cùng lối PUT /routes/{id} nhận đủ các trường của CreateRoute cộng thêm status.
/// </summary>
public class UpdateTripRequest
{
    /// <summary>Xe chạy chuyến — ràng buộc như <see cref="CreateTripRequest.BusId"/>.</summary>
    [Required(ErrorMessage = "Xe buýt không được để trống")]
    public Guid? BusId { get; set; }

    /// <summary>Giờ khởi hành — ràng buộc như <see cref="CreateTripRequest.DepartureTime"/>.</summary>
    [Required(ErrorMessage = "Giờ khởi hành không được để trống")]
    public DateTimeOffset? DepartureTime { get; set; }

    /// <summary>Giờ dự kiến tới bến cuối — bỏ trống = bỏ hẳn (gán null), khác lối PUT /routes giữ nguyên.</summary>
    public DateTimeOffset? ArrivalTime { get; set; }

    /// <summary>
    /// Trạng thái: "Scheduled" | "Running" | "Completed" | "Cancelled".
    /// Bỏ trống = giữ nguyên trạng thái hiện tại. Mã ngoài bốn giá trị trên → 400 <c>errors.status</c>.
    /// Đây cũng là cách mở lại chuyến đã huỷ: PUT với <c>status: "Scheduled"</c>.
    /// </summary>
    [StringLength(20, ErrorMessage = "Mã trạng thái tối đa 20 ký tự")]
    public string? Status { get; set; }
}
