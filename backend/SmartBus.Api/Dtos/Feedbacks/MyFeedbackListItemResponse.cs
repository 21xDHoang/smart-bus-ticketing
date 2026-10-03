namespace SmartBus.Api.Dtos.Feedbacks;

/// <summary>
/// Một dòng trong danh sách phản ánh CỦA CHÍNH NGƯỜI GỌI (GET /api/feedbacks/me) — hợp đồng
/// "Phản ánh — /feedbacks", mục "Phản ánh của tôi" của docs/api-contract.md.
///
/// Cố ý KHÔNG kèm mảng <c>replies</c>, chỉ có <c>replyCount</c> — đúng luật của mục "Entity
/// Feedback": dòng danh sách chỉ mang con đếm, luồng đầy đủ nằm ở GET /feedbacks/me/{id} (hợp đồng
/// khoá hai hình dạng này là hai, đổi một bên là đổi hình dạng API — ⛔5).
///
/// Khác <see cref="FeedbackListItemResponse"/> của nhóm admin đúng hai chỗ, cả hai đều vì phạm vi
/// "của tôi": thêm ba trường hiển thị của chuyến (hành khách không tự tra được chuyến — xem chú
/// thích dưới), và bỏ <c>userId</c>/<c>userFullName</c> (với phản ánh của chính mình thì hai trường
/// đó là hằng số, luôn là người đang gọi).
/// </summary>
public class MyFeedbackListItemResponse
{
    public Guid Id { get; set; }

    /// <summary>Chuyến bị phản ánh — <c>null</c> là hợp lệ (A9 #20).</summary>
    public Guid? TripId { get; set; }

    /// <summary>
    /// Mã tuyến của chuyến bị phản ánh, ghép từ <c>Trips</c> → <c>Routes</c>, chỉ để hiển thị.
    /// <c>null</c> khi phản ánh không gắn chuyến hoặc không tìm thấy chuyến.
    ///
    /// Ba trường chuyến phải nằm ngay ở đây vì hành khách KHÔNG có cách nào tự tra: GET
    /// /api/trips/{id} nằm sau policy ManagerOrAbove, họ chỉ có <c>tripId</c> thô.
    /// </summary>
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

    /// <summary>Chỉ đổi khi trạng thái đổi — đây là dấu hiệu hành khách theo dõi tiến độ xử lý.</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Số phản hồi nhà xe đã trả lời — đọc nội dung thì gọi GET /feedbacks/me/{id}.</summary>
    public int ReplyCount { get; set; }
}
