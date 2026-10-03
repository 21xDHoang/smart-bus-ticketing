namespace SmartBus.Api.Dtos.Feedbacks;

/// <summary>
/// Một dòng trong danh sách phản ánh (GET /admin/feedbacks) — hợp đồng "Phản ánh — /feedbacks"
/// của docs/api-contract.md.
///
/// Cố ý KHÔNG kèm mảng <c>replies</c>: một trang 10 dòng có thể kèm hàng trăm phản hồi mà bảng
/// danh sách không hiển thị — luồng đầy đủ nằm ở GET /admin/feedbacks/{id}.
/// </summary>
public class FeedbackListItemResponse
{
    public Guid Id { get; set; }

    /// <summary>Hành khách gửi phản ánh.</summary>
    public Guid UserId { get; set; }

    /// <summary>Chỉ để hiển thị, ghép từ <c>Users</c> — null khi không tìm thấy tài khoản.</summary>
    public string? UserFullName { get; set; }

    /// <summary>Chuyến bị phản ánh — <c>null</c> là hợp lệ (A9 #20).</summary>
    public Guid? TripId { get; set; }

    /// <summary>Tên chuỗi của enum — Complaint / Compliment / Suggestion.</summary>
    public string Type { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string? AttachmentUrl { get; set; }

    /// <summary>1–5 sao, null với phản ánh không chấm điểm.</summary>
    public int? Rating { get; set; }

    /// <summary>Tên chuỗi của enum — New / InProgress / Resolved.</summary>
    public string Status { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    /// <summary>Chỉ đổi khi trạng thái đổi.</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Số phản hồi quản lý đã trả lời — danh sách chỉ cần con đếm, không cần nội dung.</summary>
    public int ReplyCount { get; set; }
}
