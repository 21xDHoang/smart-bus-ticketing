namespace SmartBus.Api.Entities;

/// <summary>
/// Một phản hồi của quản lý trong mạch trao đổi của một phản ánh — bảng <c>FeedbackReplies</c>
/// (US 24). Một phản ánh có thể được trả lời nhiều lần.
///
/// Bảng chỉ GHI THÊM: không có API sửa/xoá phản hồi (sửa tại chỗ thì mất lịch sử đối thoại), nên
/// không có cột UpdatedAt — đúng quy ước A4 "UpdatedAt nullable ở bảng có sửa". Cũng không có trạng
/// thái riêng: trạng thái xử lý nằm ở <see cref="Feedback"/>.
/// </summary>
public class FeedbackReply
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Phản ánh mà phản hồi này thuộc về.</summary>
    public Guid FeedbackId { get; set; }

    public Feedback? Feedback { get; set; }

    /// <summary>Quản lý viết phản hồi này (Admin hoặc Manager — policy ManagerOrAbove).</summary>
    public Guid UserId { get; set; }

    public User? User { get; set; }

    /// <summary>Nội dung phản hồi của nhà xe — cột text; hợp đồng chặn 1–2000 ký tự ở tầng API.</summary>
    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
