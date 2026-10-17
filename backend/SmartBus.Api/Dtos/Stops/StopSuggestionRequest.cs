using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Stops;

/// <summary>
/// Tham số của GET /api/stops/suggestions — bind từ query string. Hợp đồng đầy đủ ở mục
/// "Gợi ý trạm cho tra cứu tuyến — /stops/suggestions" của docs/api-contract.md.
///
/// Khác <see cref="StopSearchRequest"/> ở chỗ endpoint này CHỈ trả trạm thuộc tuyến
/// <c>Active</c>, và lọc được theo cặp trạm đã chọn để ô điểm đến không gợi ý trạm ngược chiều.
/// </summary>
public class StopSuggestionRequest
{
    /// <summary>Số gợi ý mặc định khi client không gửi <c>limit</c>.</summary>
    public const int DefaultLimit = 10;

    /// <summary>Cận dưới của <c>limit</c> — gửi 0 là lỗi gọi, không phải "không lấy gì".</summary>
    public const int MinLimit = 1;

    /// <summary>Cận trên của <c>limit</c> — dropdown gợi ý chỉ đọc được chừng này dòng.</summary>
    public const int MaxLimit = 50;

    /// <summary>
    /// Từ khoá — khớp <b>tên trạm hoặc địa chỉ</b>, không phân biệt hoa thường và không phân
    /// biệt dấu. Bỏ trống hoặc toàn khoảng trắng = lấy đầu danh sách theo tên.
    /// </summary>
    public string? Keyword { get; set; }

    /// <summary>
    /// Số gợi ý tối đa, 1..50. Bỏ trống = <see cref="DefaultLimit"/>. <c>int?</c> để phân biệt
    /// "không gửi" với "gửi số sai" — cùng lối <see cref="StopSearchRequest.Limit"/>.
    /// </summary>
    [Range(MinLimit, MaxLimit, ErrorMessage = "Số gợi ý phải từ 1 đến 50")]
    public int? Limit { get; set; }

    /// <summary>
    /// Điểm đi đã chọn. Có tham số này thì chỉ gợi ý trạm đứng <b>sau</b> điểm đi trên cùng
    /// tuyến — dùng cho ô điểm đến.
    /// </summary>
    public Guid? OriginStopId { get; set; }

    /// <summary>
    /// Điểm đến đã chọn. Có tham số này thì chỉ gợi ý trạm đứng <b>trước</b> điểm đến trên cùng
    /// tuyến — dùng cho ô điểm đi.
    /// </summary>
    public Guid? DestinationStopId { get; set; }
}
