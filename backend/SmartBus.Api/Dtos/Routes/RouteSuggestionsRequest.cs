using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Routes;

/// <summary>
/// Tham số của GET /api/routes/suggestions — bind từ query string. Hợp đồng đầy đủ ở mục
/// "Gợi ý tuyến cho ngày đã chọn — /routes/suggestions" của docs/api-contract.md.
///
/// Hai đầu nhận <b>id hoặc tên</b>: màn tra cứu mới chọn trạm từ /stops/suggestions nên gửi id
/// (không lệ thuộc dấu); giữ đường tên để client cũ và lời gọi tay vẫn dùng được.
/// </summary>
public class RouteSuggestionsRequest
{
    /// <summary>Số ngày nhìn tới mặc định — trùng <c>TripGenerationService.HorizonDays</c>.</summary>
    public const int DefaultWindowDays = 7;

    public const int MinWindowDays = 1;

    public const int MaxWindowDays = 14;

    /// <summary>Điểm đi — khoá trạm. Ưu tiên hơn <see cref="Origin"/> khi cùng có mặt.</summary>
    public Guid? OriginStopId { get; set; }

    /// <summary>Điểm đến — khoá trạm. Ưu tiên hơn <see cref="Destination"/> khi cùng có mặt.</summary>
    public Guid? DestinationStopId { get; set; }

    /// <summary>Tên điểm đi — chỉ dùng khi không có <see cref="OriginStopId"/>; khớp không dấu.</summary>
    public string? Origin { get; set; }

    /// <summary>Tên điểm đến — cùng lối khớp với <see cref="Origin"/>.</summary>
    public string? Destination { get; set; }

    /// <summary>
    /// Ngày hành khách muốn đi, định dạng <c>yyyy-MM-dd</c>. Bỏ trống = hôm nay (giờ Việt Nam).
    /// Ngày <b>không loại tuyến khỏi kết quả</b> — chỉ quyết định lịch chạy quanh ngày đó.
    ///
    /// Cố ý là <c>string</c> chứ không phải <c>DateOnly?</c> — cùng lý do
    /// <see cref="RouteSearchRequest.Date"/>: sai định dạng phải trả 400 tiếng Việt gắn đúng
    /// trường, không phải lỗi bind mặc định bằng tiếng Anh.
    /// </summary>
    public string? Date { get; set; }

    /// <summary>
    /// Số ngày nhìn tới để tính lịch chạy, 1..14. Bỏ trống = <see cref="DefaultWindowDays"/>.
    /// Ngày cuối của cửa sổ là <c>bookingHorizonEnd</c> trong response.
    /// </summary>
    [Range(MinWindowDays, MaxWindowDays, ErrorMessage = "Số ngày xem lịch phải từ 1 đến 14")]
    public int? WindowDays { get; set; }
}
