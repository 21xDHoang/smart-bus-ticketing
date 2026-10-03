namespace SmartBus.Api.Entities;

/// <summary>
/// Loại phản ánh hành khách gửi về một chuyến đi — US 24 "Gửi khiếu nại hoặc đánh giá chất lượng
/// chuyến đi".
///
/// Ba giá trị đúng theo hợp đồng "Phản ánh — /feedbacks" của docs/api-contract.md. Lưu dạng chuỗi
/// trong CSDL qua HasConversion&lt;string&gt; (quy ước A3): đọc bảng thấy "Complaint" chứ không
/// thấy số.
/// </summary>
public enum FeedbackType
{
    /// <summary>Khiếu nại — chuyến đi có vấn đề: trễ giờ, thái độ phục vụ, an toàn…</summary>
    Complaint,

    /// <summary>Khen ngợi — phản hồi tích cực về chuyến đi.</summary>
    Compliment,

    /// <summary>Góp ý — đề xuất cải thiện, không phải phàn nàn.</summary>
    Suggestion,
}
