using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Trips;

/// <summary>
/// Body của POST /api/routes/{routeId}/trips/generate — sinh chuyến hàng loạt cho một lịch trình
/// định kỳ (quy ước A8.3).
///
/// Quản lý chọn tuyến + xe + mốc bắt đầu (ngày áp dụng + giờ khởi hành đầu tiên) + mốc kết thúc +
/// tần suất (phút) → hệ thống sinh N dòng Trips, chuyến đầu xuất phát đúng <see cref="StartTime"/>,
/// các chuyến sau cách đều <see cref="FrequencyMinutes"/> phút, chuyến cuối không vượt quá
/// <see cref="EndTime"/>.
/// </summary>
public class GenerateTripsRequest
{
    /// <summary>Xe chạy lịch trình này — ràng buộc như <see cref="CreateTripRequest.BusId"/>.</summary>
    [Required(ErrorMessage = "Xe buýt không được để trống")]
    public Guid? BusId { get; set; }

    /// <summary>
    /// Mốc bắt đầu — ngày áp dụng của lịch trình + giờ khởi hành của chuyến đầu tiên.
    /// ISO 8601 có kèm múi giờ, ví dụ <c>2026-10-01T05:00:00+07:00</c>.
    /// </summary>
    [Required(ErrorMessage = "Thời điểm bắt đầu không được để trống")]
    public DateTimeOffset? StartTime { get; set; }

    /// <summary>
    /// Mốc kết thúc — chuyến cuối cùng được sinh không vượt quá mốc này.
    /// Phải sau <see cref="StartTime"/> → nếu không: 400 <c>errors.endTime</c>.
    /// </summary>
    [Required(ErrorMessage = "Thời điểm kết thúc không được để trống")]
    public DateTimeOffset? EndTime { get; set; }

    /// <summary>
    /// Tần suất chạy xe, tính bằng phút — khoảng cách giữa hai chuyến liên tiếp.
    /// Từ 1 đến 1440 (một ngày). [Range] bắt luôn cả trường hợp thiếu trường: giá trị mặc định 0
    /// nằm ngoài khoảng nên request không thể lọt qua.
    /// </summary>
    [Range(1, 1440, ErrorMessage = "Tần suất phải từ 1 đến 1440 phút")]
    public int FrequencyMinutes { get; set; }
}
