namespace SmartBus.Api.Entities;

/// <summary>
/// Phản ánh của hành khách về một chuyến đi — bảng <c>Feedbacks</c>, mục A9 #20 của
/// docs/03-quy-uoc.md (US 24 "Gửi khiếu nại hoặc đánh giá chất lượng chuyến đi").
///
/// <see cref="TripId"/> cho phép null đúng theo A9 #20: khách khiếu nại về tuyến, giá vé hay ứng
/// dụng thì không có chuyến để trỏ vào, bắt buộc có chuyến là chặn luôn các ca đó.
///
/// ⚠️ Bảng CHƯA có migration. Phùng Duy Hoàng viết entity + cấu hình (Data/AppDbContext.Feedback.cs)
/// chỉ để API của task "API Admin phản hồi và đổi trạng thái phản ánh" (Sprint 2) biên dịch và test
/// được — test tích hợp chạy trên InMemory nên không chờ CSDL. Việc chạy
/// <c>dotnet ef migrations add</c> là của Vàng Thị Dăm:
/// xem docs/24-huong-dan-migrate-feedbacks.md.
/// </summary>
public class Feedback
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Hành khách gửi phản ánh.</summary>
    public Guid UserId { get; set; }

    public User? User { get; set; }

    /// <summary>Chuyến bị phản ánh — nullable (A9 #20): phản ánh chung không gắn chuyến vẫn hợp lệ.</summary>
    public Guid? TripId { get; set; }

    public Trip? Trip { get; set; }

    /// <summary>Loại phản ánh — Complaint / Compliment / Suggestion.</summary>
    public FeedbackType Type { get; set; }

    /// <summary>Nội dung hành khách viết — cột text, không giới hạn độ dài; hợp đồng chặn ở tầng API.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Ảnh/đường dẫn đính kèm — nullable, cùng lối cột text.</summary>
    public string? AttachmentUrl { get; set; }

    /// <summary>
    /// Số sao 1–5 khi phản ánh là đánh giá chất lượng — nullable: khiếu nại/góp ý không nhất thiết
    /// có điểm. Ràng buộc 1–5 thuộc tầng API của task "API gửi phản ánh" (chưa làm), CSDL không có
    /// check constraint — cùng lối các cột nghiệp vụ khác của dự án.
    /// </summary>
    public int? Rating { get; set; }

    /// <summary>Trạng thái xử lý — mọi phản ánh mới bắt đầu ở <see cref="FeedbackStatus.New"/>.</summary>
    public FeedbackStatus Status { get; set; } = FeedbackStatus.New;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Chỉ đổi khi quản lý đổi trạng thái. Thêm phản hồi KHÔNG chạm vào dòng này — dòng Feedbacks
    /// là bản ghi của hành khách, phản hồi nằm ở bảng riêng (hợp đồng nói rõ).
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Phản hồi của quản lý — bảng <c>FeedbackReplies</c>, chỉ ghi thêm, sắp cũ → mới khi đọc.</summary>
    public ICollection<FeedbackReply> Replies { get; set; } = [];
}
