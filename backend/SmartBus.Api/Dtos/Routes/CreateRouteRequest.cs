using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Routes;

/// <summary>
/// Body của POST /api/routes — thêm tuyến đường mới.
/// Không nhận trạng thái: tuyến mới luôn bắt đầu ở <c>Active</c>, muốn đổi trạng thái
/// thì dùng PUT — cùng lối Fare không cho đổi đối tượng ngay lúc tạo.
/// </summary>
public class CreateRouteRequest
{
    /// <summary>Mã tuyến hiển thị cho hành khách — "01", "B10"… Duy nhất toàn hệ thống.</summary>
    [Required(ErrorMessage = "Mã tuyến không được để trống")]
    [StringLength(20, MinimumLength = 2, ErrorMessage = "Mã tuyến phải từ 2 đến 20 ký tự")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Tên tuyến không được để trống")]
    [StringLength(200, MinimumLength = 2, ErrorMessage = "Tên tuyến phải từ 2 đến 200 ký tự")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Điểm đầu không được để trống")]
    [StringLength(200, MinimumLength = 2, ErrorMessage = "Điểm đầu phải từ 2 đến 200 ký tự")]
    public string Origin { get; set; } = string.Empty;

    [Required(ErrorMessage = "Điểm cuối không được để trống")]
    [StringLength(200, MinimumLength = 2, ErrorMessage = "Điểm cuối phải từ 2 đến 200 ký tự")]
    public string Destination { get; set; } = string.Empty;

    /// <summary>
    /// Tổng chiều dài tuyến (km). Cố ý KHÔNG gắn [Range]: biên trên phải là decimal chính xác
    /// (trần của cột numeric(6,2) là 9999.99), mà [Range] chỉ nhận biên dưới dạng chuỗi —
    /// cùng lý do FareService không gắn [Range] cho giá tiền. Kiểm tra ở RouteService.
    /// Bỏ trống trường này thì giá trị mặc định là 0 và được coi là hợp lệ (tuyến chưa đo).
    /// </summary>
    public decimal DistanceKm { get; set; }
}
