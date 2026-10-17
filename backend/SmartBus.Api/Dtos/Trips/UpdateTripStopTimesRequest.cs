using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Trips;

/// <summary>
/// Body của PUT /api/routes/{routeId}/trips/{id}/stop-times — ghi đè giờ các trạm TRUNG GIAN.
///
/// <para>
/// Thay thế TOÀN PHẦN: tập mốc gửi lên trở thành toàn bộ mốc ghi đè của chuyến. Mảng rỗng
/// (<c>{ "items": [] }</c>) xoá hết mốc ghi đè và đưa các trạm về giờ dự kiến từ km.
/// Vì vậy <see cref="Items"/> phải nullable + <c>[Required]</c>: body <c>{}</c> khác với mảng
/// rỗng, không được hiểu là "xoá hết" ngoài ý muốn.
/// </para>
///
/// <para>
/// Chỉ trạm trung gian mới sửa được. Trạm đầu là <c>Trips.DepartureTime</c>, bến cuối là
/// <c>Trips.ArrivalTime</c> — muốn đổi hai mốc đó thì sửa giờ chạy của chuyến.
/// </para>
/// </summary>
public class UpdateTripStopTimesRequest
{
    /// <summary>
    /// Danh sách mốc ghi đè. Bắt buộc phải có mặt trong body; được phép là mảng rỗng.
    /// </summary>
    [Required(ErrorMessage = "Danh sách giờ đến từng trạm không được để trống")]
    public List<UpdateTripStopTimeItem>? Items { get; set; }
}

/// <summary>Một trạm trung gian cần ghi đè giờ đến.</summary>
public class UpdateTripStopTimeItem
{
    /// <summary>Trạm cần đặt giờ — phải là trạm trung gian của tuyến.</summary>
    [Required(ErrorMessage = "Trạm không được để trống")]
    public Guid? StopId { get; set; }

    /// <summary>
    /// Giờ đến mới, gửi kèm múi giờ (ví dụ <c>2026-10-01T12:14:00+07:00</c>).
    /// Null = bỏ mốc ghi đè của trạm này, quay về giờ dự kiến từ km.
    ///
    /// Dùng <see cref="DateTimeOffset"/> để buộc client gửi rõ múi giờ — cùng quy ước với các
    /// DTO giờ khác của lịch trình (xem mục "Giờ gửi lên phải kèm múi giờ" trong api-contract).
    /// </summary>
    public DateTimeOffset? ArrivalTime { get; set; }
}
