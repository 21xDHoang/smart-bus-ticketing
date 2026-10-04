using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Feedbacks;

/// <summary>
/// Body của POST /api/feedbacks — gửi một phản ánh mới của chính người gọi.
/// Hợp đồng "Phản ánh — /feedbacks", phần "Gửi phản ánh" của docs/api-contract.md.
///
/// <c>type</c> để DẠNG CHUỖI rồi kiểm tra ở tầng nghiệp vụ, không để kiểu enum — cùng lối
/// <see cref="UpdateFeedbackStatusRequest.Status"/> (mục D3 của docs/03-quy-uoc.md): nếu để enum,
/// JSON gửi lên mã sai sẽ bị model binding từ chối trước khi vào tới service, và thông báo lỗi
/// của framework không theo cấu trúc { message, errors } thống nhất của dự án.
/// </summary>
public class CreateFeedbackRequest
{
    /// <summary>
    /// Chuyến bị phản ánh. Null là hợp lệ — phản ánh về tuyến/dịch vụ/ứng dụng (A9 #20).
    /// Có giá trị mà không trỏ tới chuyến nào → 404 — tham chiếu cứng, cùng lối routeId của
    /// đăng ký vé tháng.
    /// </summary>
    public Guid? TripId { get; set; }

    /// <summary>
    /// Bắt buộc. "Complaint" | "Compliment" | "Suggestion". Mã lạ → 400 errors.type — service
    /// so khớp tên enum, thông báo lỗi liệt kê đủ ba mã chấp nhận.
    /// </summary>
    [Required(ErrorMessage = "Loại phản ánh không được để trống")]
    [StringLength(20, ErrorMessage = "Mã loại phản ánh tối đa 20 ký tự")]
    public string? Type { get; set; }

    /// <summary>Bắt buộc, 1–2000 ký tự — cùng giới hạn nội dung phản hồi của quản lý.</summary>
    [Required(ErrorMessage = "Nội dung phản ánh không được để trống")]
    [StringLength(2000, MinimumLength = 1, ErrorMessage = "Nội dung phản ánh phải từ 1 đến 2000 ký tự")]
    public string Content { get; set; } = string.Empty;

    /// <summary>1–5 sao mức độ hài lòng; null khi phản ánh không chấm điểm.</summary>
    [Range(1, 5, ErrorMessage = "Mức độ hài lòng phải từ 1 đến 5 sao")]
    public int? Rating { get; set; }

    /// <summary>Đường dẫn ảnh đính kèm; null khi không đính kèm. Chuỗi trắng/rỗng lưu thành null.</summary>
    [StringLength(2000, ErrorMessage = "Đường dẫn ảnh đính kèm tối đa 2000 ký tự")]
    public string? AttachmentUrl { get; set; }
}
