using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Trips;

/// <summary>
/// Body của <c>POST /api/routes/{routeId}/trips/generate-fleet</c> — sinh chuyến hàng loạt rồi
/// tự chia vòng tròn cho NHIỀU xe của tuyến (nợ của đợt mở rộng đội xe 17/10/2026).
///
/// <para>
/// Khác <see cref="GenerateTripsRequest"/> ở chỗ nhận DANH SÁCH xe thay vì một xe: hệ thống không có
/// bảng gán xe ↔ tuyến (quan hệ suy ra từ các chuyến), nên người điều hành tự chọn đội xe, backend chỉ
/// chia giờ. Thứ tự trong <see cref="BusIds"/> là thứ tự ưu tiên khi chia vòng tròn.
/// </para>
///
/// <para>
/// Hợp đồng đầy đủ ở mục "POST /routes/{routeId}/trips/generate-fleet" của docs/api-contract.md.
/// </para>
/// </summary>
public class GenerateFleetTripsRequest
{
    /// <summary>
    /// Xe được chia chuyến — 1 tới 100 ID, không trùng nhau, tất cả phải đang <c>Active</c>.
    /// Không kiểm tra "xe thuộc tuyến nào" vì hệ thống không có bảng phân công xe ↔ tuyến.
    /// </summary>
    [Required(ErrorMessage = "Danh sách xe không được để trống")]
    public List<Guid>? BusIds { get; set; }

    /// <summary>Mốc bắt đầu — chuyến đầu xuất phát đúng mốc này.</summary>
    [Required(ErrorMessage = "Thời điểm bắt đầu không được để trống")]
    public DateTimeOffset? StartTime { get; set; }

    /// <summary>Mốc kết thúc — chuyến cuối không vượt quá mốc này.</summary>
    [Required(ErrorMessage = "Thời điểm kết thúc không được để trống")]
    public DateTimeOffset? EndTime { get; set; }

    /// <summary>Khoảng cách giữa hai chuyến liên tiếp, tính bằng phút.</summary>
    [Range(1, 1440, ErrorMessage = "Tần suất phải từ 1 đến 1440 phút")]
    public int FrequencyMinutes { get; set; }
}
