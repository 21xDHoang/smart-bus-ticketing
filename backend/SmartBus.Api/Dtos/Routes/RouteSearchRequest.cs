using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Routes;

/// <summary>
/// Tham số của GET /api/routes/search — bind từ query string.
///
/// Hai tên <c>origin</c>/<c>destination</c> cố ý trùng với cách form "điểm đi - điểm đến" của
/// màn hình tra cứu đặt tên: frontend truyền thẳng giá trị người dùng gõ vào, không phải nói
/// hai thứ tiếng. Hợp đồng đầy đủ ở mục "Tra cứu tuyến — /routes/search" của docs/api-contract.md.
/// </summary>
public class RouteSearchRequest
{
    /// <summary>
    /// Bắt buộc. Tên điểm đi — khớp tên trạm dừng (<c>Stop.Name</c>), không phân biệt hoa thường.
    /// </summary>
    [Required(ErrorMessage = "Điểm đi không được để trống")]
    public string? Origin { get; set; }

    /// <summary>
    /// Bắt buộc. Tên điểm đến — cùng lối khớp với <see cref="Origin"/>.
    /// </summary>
    [Required(ErrorMessage = "Điểm đến không được để trống")]
    public string? Destination { get; set; }

    /// <summary>
    /// Ngày đi, định dạng <c>yyyy-MM-dd</c>. Có ngày thì chỉ trả tuyến có ít nhất một chuyến
    /// <c>Scheduled</c> khởi hành trong trọn ngày đó giờ Việt Nam. Bỏ trống = không lọc theo chuyến.
    ///
    /// Cố ý là <c>string</c> chứ không phải <c>DateOnly?</c>: nhận sai định dạng thì service trả
    /// 400 <c>errors.date</c> kèm thông báo hiểu được, thay vì ModelState lỗi "The value '…' is
    /// not valid for DateOnly" bằng tiếng Anh.
    /// </summary>
    public string? Date { get; set; }
}
