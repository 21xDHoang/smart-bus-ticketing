namespace SmartBus.Api.Dtos.Feedbacks;

/// <summary>
/// Một phản ánh ở dạng ĐẦY ĐỦ — chi tiết (GET /admin/feedbacks/{id}) và response của hai endpoint
/// ghi (POST .../replies, PATCH .../{id}): hợp đồng "Phản ánh — /feedbacks" của docs/api-contract.md.
///
/// Khác <see cref="FeedbackListItemResponse"/> đúng một chỗ: bản đầy đủ mang MẢNG <c>replies</c>
/// thay vì con đếm <c>replyCount</c> — hai hình dạng khác nhau nên là hai lớp, không nhồi chung
/// rồi để trống một bên (bên trống vẫn hiện ra thành trường thừa trong JSON, sai hợp đồng).
/// </summary>
public class FeedbackResponse
{
    public Guid Id { get; set; }

    /// <summary>Hành khách gửi phản ánh.</summary>
    public Guid UserId { get; set; }

    /// <summary>Chỉ để hiển thị, ghép từ <c>Users</c> — null khi không tìm thấy tài khoản.</summary>
    public string? UserFullName { get; set; }

    /// <summary>Chuyến bị phản ánh — <c>null</c> là hợp lệ (phản ánh về tuyến/dịch vụ/ứng dụng, A9 #20).</summary>
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

    /// <summary>Chỉ đổi khi trạng thái đổi — thêm phản hồi không chạm vào dòng Feedbacks.</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Toàn bộ luồng phản hồi, sắp cũ → mới.</summary>
    public IReadOnlyList<FeedbackReplyResponse> Replies { get; set; } = [];
}
