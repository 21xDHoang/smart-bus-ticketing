using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Buses;

/// <summary>
/// Body của POST /api/buses — thêm xe buýt mới vào đội xe.
/// Không nhận trạng thái: xe mới luôn bắt đầu ở <c>Active</c>, muốn chuyển sang bảo dưỡng
/// hay ngừng khai thác thì dùng PUT — cùng lối CreateRouteRequest không nhận trạng thái.
/// </summary>
public class CreateBusRequest
{
    /// <summary>Biển số xe — "29B-123.45". Duy nhất toàn hệ thống (khoá nghiệp vụ).</summary>
    [Required(ErrorMessage = "Biển số không được để trống")]
    [StringLength(20, MinimumLength = 2, ErrorMessage = "Biển số phải từ 2 đến 20 ký tự")]
    public string LicensePlate { get; set; } = string.Empty;

    [Required(ErrorMessage = "Loại xe không được để trống")]
    [StringLength(50, MinimumLength = 2, ErrorMessage = "Loại xe phải từ 2 đến 50 ký tự")]
    public string BusType { get; set; } = string.Empty;

    /// <summary>
    /// Sức chứa theo số ghế. Trần 200 đủ cho mọi cỡ xe buýt thành phố và xe khách hai tầng;
    /// trần này là quy ước của API, cột CSDL chỉ giới hạn kiểu int nên phải chặn ở đây.
    /// </summary>
    [Range(1, 200, ErrorMessage = "Sức chứa phải từ 1 đến 200 chỗ")]
    public int Capacity { get; set; }
}
