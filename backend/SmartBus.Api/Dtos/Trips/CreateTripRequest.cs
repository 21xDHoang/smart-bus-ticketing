using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Trips;

/// <summary>
/// Body của POST /api/routes/{routeId}/trips — thêm một chuyến xe lẻ cho tuyến.
///
/// Không nhận trạng thái: chuyến mới luôn bắt đầu ở <c>Scheduled</c>, muốn đổi trạng thái
/// thì dùng PUT — cùng lối tuyến mới luôn <c>Active</c> và Fare không cho đổi đối tượng ngay
/// lúc tạo.
///
/// Lập lịch trình định kỳ (nhiều chuyến cách đều tần suất) thì dùng POST .../trips/generate —
/// quy ước A8.3 chốt không tách bảng Schedule, chỉ dùng Trips + API sinh chuyến hàng loạt.
/// </summary>
public class CreateTripRequest
{
    /// <summary>
    /// Xe chạy chuyến này. Để <c>Guid?</c> chứ không phải <c>Guid</c>: kiểu không nullable thì
    /// body thiếu hẳn trường này sẽ nhận <c>Guid.Empty</c> và <c>[Required]</c> không bắt được —
    /// cùng lý do <see cref="RouteStops.AssignStopToRouteRequest.StopId"/>.
    /// </summary>
    [Required(ErrorMessage = "Xe buýt không được để trống")]
    public Guid? BusId { get; set; }

    /// <summary>
    /// Giờ khởi hành. Để <c>DateTimeOffset?</c> chứ không phải <c>DateTime</c> với lý do như
    /// trên, và kiểu này bắt buộc chuỗi gửi lên kèm múi giờ — nhận giờ trần "05:00" rồi đoán
    /// múi giờ là nguồn lỗi đúng giờ chuyến. Server quy về UTC khi lưu (quy ước A3: timestamptz).
    /// </summary>
    [Required(ErrorMessage = "Giờ khởi hành không được để trống")]
    public DateTimeOffset? DepartureTime { get; set; }

    /// <summary>
    /// Giờ dự kiến tới bến cuối. Không bắt buộc — lúc sinh chuyến có thể chưa chốt được
    /// (chưa có thời gian chạy chuẩn của tuyến). Có thì phải sau giờ khởi hành, kiểm tra ở
    /// <c>TripService</c> để thông báo đi qua đúng cấu trúc { message, errors }.
    /// </summary>
    public DateTimeOffset? ArrivalTime { get; set; }
}
