namespace SmartBus.Api.Dtos.Feedbacks;

/// <summary>
/// Phản ánh VỪA GỬI — response của POST /api/feedbacks (201), hợp đồng "Phản ánh — /feedbacks",
/// phần "Gửi phản ánh" của docs/api-contract.md.
///
/// Cố ý KHÁC <see cref="FeedbackResponse"/> của nhóm admin và <see cref="MyFeedbackResponse"/>
/// của "phản ánh của tôi" đúng ba chỗ, cả ba đều vì phản ánh VỪA được tạo ra: không
/// <c>userId</c>/<c>userFullName</c> (tác giả là chính người gọi), không <c>replyCount</c>/
/// <c>replies</c> (chưa có phản hồi nào), không <c>updatedAt</c> (chưa ai đổi trạng thái).
/// Màn hình thành công của frontend (feedbackSubmitApi.ts) đã khoá đúng tám trường dưới đây —
/// hợp đồng khoá từng hình dạng một, đổi một bên là đổi hình dạng API (⛔5).
/// </summary>
public class FeedbackSubmissionResponse
{
    public Guid Id { get; set; }

    /// <summary>Chuyến bị phản ánh — null là hợp lệ (A9 #20).</summary>
    public Guid? TripId { get; set; }

    /// <summary>Tên chuỗi của enum — Complaint / Compliment / Suggestion.</summary>
    public string Type { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string? AttachmentUrl { get; set; }

    /// <summary>1–5 sao, null với phản ánh không chấm điểm.</summary>
    public int? Rating { get; set; }

    /// <summary>Luôn là "New" ngay sau khi gửi — giá trị khởi tạo của entity.</summary>
    public string Status { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
