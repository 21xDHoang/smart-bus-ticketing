namespace SmartBus.Api.Dtos.Feedbacks;

/// <summary>
/// Một dòng của bảng đếm theo LOẠI trong GET /api/admin/feedbacks/statistics — hợp đồng "Phản ánh —
/// /feedbacks", mục "Thống kê phản ánh" của docs/api-contract.md.
///
/// Không có trường nào ngoài cặp mã/đếm: bảng này trả lời đúng một câu hỏi "mỗi loại có bao nhiêu
/// phản ánh". Thêm chỉ số khác (tỉ lệ đã xử lý, điểm sao trung bình…) là đổi hình dạng API — bàn ở
/// nhóm trước (⛔5).
/// </summary>
public class FeedbackTypeCountResponse
{
    /// <summary>Tên chuỗi của enum — Complaint / Compliment / Suggestion.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Số phản ánh của loại này. <b>0 là giá trị bình thường</b>: hợp đồng bắt bảng đếm theo loại
    /// luôn đủ ba dòng, kể cả loại chưa có phản ánh nào — biểu đồ có trục cố định ba giá trị, thiếu
    /// một cột thì màn hình phải tự đoán.
    /// </summary>
    public int Count { get; set; }
}
