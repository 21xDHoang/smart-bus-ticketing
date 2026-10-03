namespace SmartBus.Api.Dtos.Feedbacks;

/// <summary>
/// Một phản ánh của chính người gọi ở dạng ĐẦY ĐỦ — GET /api/feedbacks/me/{id} — hợp đồng
/// "Phản ánh — /feedbacks", mục "Phản ánh của tôi" của docs/api-contract.md.
///
/// Khác <see cref="MyFeedbackListItemResponse"/> đúng một chỗ: mang MẢNG <c>replies</c> thay vì con
/// đếm <c>replyCount</c> — hai hình dạng khác nhau nên là hai lớp, không nhồi chung rồi để trống
/// một bên (bên trống vẫn hiện thành trường thừa trong JSON, sai hợp đồng). Cùng lối cặp
/// <see cref="FeedbackResponse"/> / <see cref="FeedbackListItemResponse"/> của nhóm admin.
/// </summary>
public class MyFeedbackResponse
{
    public Guid Id { get; set; }

    /// <summary>Chuyến bị phản ánh — <c>null</c> là hợp lệ (phản ánh về tuyến/dịch vụ/ứng dụng, A9 #20).</summary>
    public Guid? TripId { get; set; }

    /// <summary>Mã tuyến của chuyến bị phản ánh — xem chú thích ở <see cref="MyFeedbackListItemResponse"/>.</summary>
    public string? RouteCode { get; set; }

    /// <summary>Tên tuyến của chuyến bị phản ánh — cùng nguồn và cùng luật null với <see cref="RouteCode"/>.</summary>
    public string? RouteName { get; set; }

    /// <summary>Giờ khởi hành của chuyến bị phản ánh (UTC) — cùng nguồn và cùng luật null với <see cref="RouteCode"/>.</summary>
    public DateTime? DepartureTime { get; set; }

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

    /// <summary>Toàn bộ luồng phản hồi của nhà xe, sắp cũ → mới.</summary>
    public IReadOnlyList<FeedbackReplyResponse> Replies { get; set; } = [];
}
