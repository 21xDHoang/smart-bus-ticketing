using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Routes;

/// <summary>
/// Body của PUT /api/routes/{id} — sửa tuyến đường. Gồm đủ các trường nghiệp vụ của
/// <see cref="CreateRouteRequest"/> cộng thêm trạng thái.
///
/// Trạng thái để DẠNG CHUỖI rồi kiểm tra ở tầng nghiệp vụ, không để kiểu enum — cùng lối
/// với <c>CreateFareRequest.PassengerType</c> (mục D3 của docs/03-quy-uoc.md): nếu để enum,
/// JSON gửi lên mã sai sẽ bị model binding từ chối trước khi vào tới service, và thông báo
/// lỗi của framework không theo cấu trúc { message, errors } thống nhất của dự án.
/// Bỏ trống = giữ nguyên trạng thái hiện tại.
/// </summary>
public class UpdateRouteRequest
{
    /// <summary>Mã tuyến — ràng buộc giống <see cref="CreateRouteRequest.Code"/>.</summary>
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

    /// <summary>Chiều dài tuyến (km) — ràng buộc giống <see cref="CreateRouteRequest.DistanceKm"/>.</summary>
    public decimal DistanceKm { get; set; }

    /// <summary>Trạng thái mới: "Active" | "Inactive". Bỏ trống = giữ nguyên trạng thái hiện tại.</summary>
    [StringLength(20, ErrorMessage = "Trạng thái tối đa 20 ký tự")]
    public string? Status { get; set; }
}
