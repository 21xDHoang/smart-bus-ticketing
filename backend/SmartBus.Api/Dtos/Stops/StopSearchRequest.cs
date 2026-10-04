using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Stops;

/// <summary>
/// Tham số của GET /api/stops/search — bind từ query string. Hợp đồng đầy đủ ở mục
/// "Tra cứu trạm dừng — /stops/search" của docs/api-contract.md.
///
/// Cùng khuôn <see cref="Routes.RouteSearchRequest"/> (cũng là DTO của một API công khai
/// story 1), chỉ khác hai điểm mà hợp đồng đã chốt: từ khoá KHÔNG bắt buộc, và có thêm
/// <c>limit</c>.
/// </summary>
public class StopSearchRequest
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
    ///
    /// Cố ý KHÔNG có <c>[Required]</c> (khác <see cref="Routes.RouteSearchRequest"/>): đây là ô
    /// CHỌN trạm, hành khách vừa bấm vào phải thấy ngay vài trạm để chọn. Không giới hạn độ dài
    /// vì chuỗi dài chỉ đơn giản là không khớp trạm nào — trả mảng rỗng, không phải lỗi gọi.
    /// </summary>
    public string? Keyword { get; set; }

    /// <summary>
    /// Số gợi ý tối đa, 1..50. Bỏ trống = <see cref="DefaultLimit"/>.
    ///
    /// <c>int?</c> chứ không phải <c>int</c> — cùng lối <c>ListBusesRequest.PageSize</c>: để phân
    /// biệt "không gửi" (lấy mặc định) với "gửi số sai" (400). Gửi <c>?limit=</c> rỗng cũng vào
    /// nhánh "không gửi" thay vì đổ lỗi bind, nên client nối query string cẩu thả vẫn chạy đúng.
    ///
    /// Hai biên là hằng số <c>int</c> nên không dính bẫy ép kiểu của <c>RangeAttribute</c> đã ghi
    /// ở <see cref="StopRequest.Latitude"/> — bẫy đó chỉ xảy ra với thuộc tính kiểu số thực.
    /// </summary>
    [Range(MinLimit, MaxLimit, ErrorMessage = "Số gợi ý phải từ 1 đến 50")]
    public int? Limit { get; set; }
}
