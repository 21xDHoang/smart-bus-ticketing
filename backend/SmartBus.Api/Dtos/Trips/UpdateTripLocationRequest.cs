using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Trips;

/// <summary>
/// Body của PUT /api/trips/{id}/location — báo vị trí hiện tại của xe trong một chuyến (US 10
/// "Thông báo trạm", Sprint 4, task *"API cập nhật vị trí chuyến (CurrentLat/CurrentLng/
/// CurrentStopId)"* — Trần Trung Hiếu).
///
/// Một lần gọi là MỘT ảnh chụp vị trí: ba trường cùng về một thời điểm, không tách lẻ từng
/// trường. PUT chứ không PATCH vì vị trí là trạng thái được THAY THẾ toàn bộ mỗi lần báo —
/// gọi lại y hệt nhiều lần chỉ làm mới <c>positionUpdatedAt</c>, không đổi gì khác (idempotent).
///
/// Hợp đồng: docs/api-contract.md mục "PUT /trips/{id}/location".
/// </summary>
public class UpdateTripLocationRequest
{
    /// <summary>
    /// Vĩ độ hiện tại của xe. Để <c>double?</c> chứ không phải <c>double</c>: kiểu không
    /// nullable thì body thiếu hẳn trường này nhận giá trị mặc định 0.0 và <c>[Required]</c>
    /// không bắt được — mà 0.0 là vĩ độ CÓ THẬT trên bản đồ (xích đạo), không thể nhận biết
    /// bằng giá trị mặc định (cùng lý do AssignStopToRouteRequest dùng Guid?).
    /// </summary>
    [Required(ErrorMessage = "Vĩ độ là bắt buộc")]
    [Range(-90, 90, ErrorMessage = "Vĩ độ phải từ -90 đến 90")]
    public double? CurrentLat { get; set; }

    /// <summary>Kinh độ hiện tại của xe — <c>double?</c> cùng lý do vĩ độ ở trên.</summary>
    [Required(ErrorMessage = "Kinh độ là bắt buộc")]
    [Range(-180, 180, ErrorMessage = "Kinh độ phải từ -180 đến 180")]
    public double? CurrentLng { get; set; }

    /// <summary>
    /// Trạm xe vừa đi qua gần nhất — phải là trạm thuộc tuyến của chuyến. Bỏ trống khi thiết bị
    /// không xác định được trạm: chỉ cập nhật toạ độ, giữ nguyên trạm hiện tại.
    /// </summary>
    public Guid? CurrentStopId { get; set; }
}
