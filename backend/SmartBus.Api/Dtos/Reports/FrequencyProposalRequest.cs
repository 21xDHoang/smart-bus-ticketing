namespace SmartBus.Api.Dtos.Reports;

/// <summary>
/// Tham số của GET /api/reports/frequency-proposals — đề xuất tăng/giảm tần suất chạy xe theo dữ
/// liệu (US 20 "Tỷ lệ lấp đầy", Sprint 4, task *"API đề xuất tăng/giảm tần suất chạy xe theo dữ
/// liệu"* — Phùng Duy Hoàng). Hợp đồng đầy đủ ở mục "Đề xuất tần suất chạy xe —
/// /reports/frequency-proposals" của docs/api-contract.md.
///
/// Giữ y nguyên ba tham số của báo cáo khung giờ cao điểm — endpoint này đọc thẳng báo cáo đó,
/// nên mặc định (30 ngày gần nhất, giờ Việt Nam, trần 366 ngày) và kiểm tra đầu vào không được
/// định nghĩa lại lần thứ hai.
/// </summary>
public class FrequencyProposalRequest
{
    /// <summary>
    /// Ngày bắt đầu (theo lịch Việt Nam, dạng <c>yyyy-MM-dd</c>), TÍNH CẢ. Trống = 29 ngày trước
    /// <c>to</c>. Phải ≤ <c>to</c> và khoảng không quá 366 ngày — nếu không → 400 theo trường.
    /// </summary>
    public DateOnly? From { get; set; }

    /// <summary>Ngày kết thúc (theo lịch Việt Nam), TÍNH CẢ. Trống = hôm nay (giờ Việt Nam).</summary>
    public DateOnly? To { get; set; }

    /// <summary>
    /// Chỉ tính các chuyến thuộc tuyến này — bỏ trống = toàn công ty. Tuyến không tồn tại → 404.
    /// </summary>
    public Guid? RouteId { get; set; }
}
