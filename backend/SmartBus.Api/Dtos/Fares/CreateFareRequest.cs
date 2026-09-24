using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Fares;

/// <summary>
/// Body của POST /api/routes/{routeId}/fares — đặt giá cho một đối tượng hành khách trên tuyến.
/// </summary>
public class CreateFareRequest
{
    /// <summary>
    /// Mã đối tượng: Standard | Student | Senior | Child | Disabled.
    ///
    /// Để dạng CHUỖI rồi kiểm tra ở tầng nghiệp vụ, không để kiểu enum — cùng lối với
    /// <c>CreateAdminUserRequest.RoleCode</c>. Lý do: nếu để enum, JSON gửi lên mã sai sẽ bị
    /// model binding từ chối trước khi vào tới service, và thông báo lỗi của framework không
    /// theo cấu trúc { message, errors } thống nhất của dự án (mục D3).
    /// </summary>
    [Required(ErrorMessage = "Đối tượng hành khách không được để trống")]
    [StringLength(20, ErrorMessage = "Mã đối tượng tối đa 20 ký tự")]
    public string PassengerType { get; set; } = string.Empty;

    /// <summary>
    /// Giá vé (VND), phải lớn hơn 0 và không vượt quá 9 999 999 999.99 — trần của cột
    /// numeric(12,2), tức 12 chữ số tổng đó có 2 chữ số thập phân.
    ///
    /// Cố ý KHÔNG gắn [Range] ở đây: [Range] nhận biên dưới dạng chuỗi rồi tự chuyển kiểu,
    /// còn biên trên của tiền phải là decimal chính xác. Kiểm tra ở <c>FareService</c> để biên
    /// chỉ khai báo một lần bằng decimal, và để thông báo lỗi đi qua đúng cấu trúc D3.
    /// Bỏ trống trường này thì giá mặc định là 0 và bị chặn với thông báo "phải lớn hơn 0".
    /// </summary>
    public decimal Price { get; set; }
}
