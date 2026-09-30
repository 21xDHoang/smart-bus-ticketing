using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Buses;

/// <summary>
/// Body của PUT /api/buses/{id} — sửa xe buýt. Gồm đủ các trường nghiệp vụ của
/// <see cref="CreateBusRequest"/> cộng thêm trạng thái.
///
/// Trạng thái để DẠNG CHUỖI rồi kiểm tra ở tầng nghiệp vụ, không để kiểu enum — cùng lối
/// với <c>UpdateRouteRequest.Status</c> (mục D3 của docs/03-quy-uoc.md): nếu để enum,
/// JSON gửi lên mã sai sẽ bị model binding từ chối trước khi vào tới service, và thông báo
/// lỗi của framework không theo cấu trúc { message, errors } thống nhất của dự án.
/// Bỏ trống = giữ nguyên trạng thái hiện tại.
/// </summary>
public class UpdateBusRequest
{
    /// <summary>Biển số — ràng buộc giống <see cref="CreateBusRequest.LicensePlate"/>.</summary>
    [Required(ErrorMessage = "Biển số không được để trống")]
    [StringLength(20, MinimumLength = 2, ErrorMessage = "Biển số phải từ 2 đến 20 ký tự")]
    public string LicensePlate { get; set; } = string.Empty;

    /// <summary>Loại xe — ràng buộc giống <see cref="CreateBusRequest.BusType"/>.</summary>
    [Required(ErrorMessage = "Loại xe không được để trống")]
    [StringLength(50, MinimumLength = 2, ErrorMessage = "Loại xe phải từ 2 đến 50 ký tự")]
    public string BusType { get; set; } = string.Empty;

    /// <summary>Sức chứa — ràng buộc giống <see cref="CreateBusRequest.Capacity"/>.</summary>
    [Range(1, 200, ErrorMessage = "Sức chứa phải từ 1 đến 200 chỗ")]
    public int Capacity { get; set; }

    /// <summary>
    /// Trạng thái mới: "Active" | "Maintenance" | "Inactive".
    /// Bỏ trống = giữ nguyên trạng thái hiện tại.
    /// </summary>
    [StringLength(20, ErrorMessage = "Trạng thái tối đa 20 ký tự")]
    public string? Status { get; set; }
}
