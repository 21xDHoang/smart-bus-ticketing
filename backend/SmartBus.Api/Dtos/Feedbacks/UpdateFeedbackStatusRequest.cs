using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Feedbacks;

/// <summary>
/// Body của PATCH /api/admin/feedbacks/{id} — đổi trạng thái xử lý.
///
/// <c>status</c> để DẠNG CHUỖI rồi kiểm tra ở tầng nghiệp vụ, không để kiểu enum — cùng lối
/// <see cref="Buses.UpdateBusRequest.Status"/> (mục D3 của docs/03-quy-uoc.md): nếu để enum, JSON
/// gửi lên mã sai sẽ bị model binding từ chối trước khi vào tới service, và thông báo lỗi của
/// framework không theo cấu trúc { message, errors } thống nhất của dự án.
/// </summary>
public class UpdateFeedbackStatusRequest
{
    /// <summary>
    /// Trạng thái mới: "New" | "InProgress" | "Resolved". Không có máy trạng thái — mọi chiều
    /// chuyển đều hợp lệ, kể cả mở lại Resolved → InProgress (hợp đồng nói rõ lý do).
    /// </summary>
    [Required(ErrorMessage = "Trạng thái không được để trống")]
    [StringLength(20, ErrorMessage = "Mã trạng thái tối đa 20 ký tự")]
    public string? Status { get; set; }
}
