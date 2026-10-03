using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Feedbacks;

/// <summary>
/// Body của POST /api/admin/feedbacks/{id}/replies — quản lý phản hồi hành khách.
/// Hợp đồng "Phản ánh — /feedbacks" của docs/api-contract.md: <c>content</c> bắt buộc, 1–2000 ký tự.
/// </summary>
public class CreateFeedbackReplyRequest
{
    /// <summary>Nội dung phản hồi của nhà xe — lưu nguyên văn sau khi cắt khoảng trắng hai đầu.</summary>
    [Required(ErrorMessage = "Nội dung phản hồi không được để trống")]
    [StringLength(2000, MinimumLength = 1, ErrorMessage = "Nội dung phản hồi phải từ 1 đến 2000 ký tự")]
    public string Content { get; set; } = string.Empty;
}
