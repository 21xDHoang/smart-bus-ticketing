namespace SmartBus.Api.Dtos.Reports;

/// <summary>
/// Tham số của GET /api/reports/revenue/comparison — so sánh doanh thu kỳ hiện tại với kỳ liền
/// trước cùng độ dài (US 19 "Doanh thu", Sprint 4, task *"API so sánh doanh thu giữa các kỳ
/// (tăng/giảm %)"* — Phùng Duy Hoàng). Hợp đồng đầy đủ ở mục "Báo cáo doanh thu — /reports/revenue"
/// của docs/api-contract.md.
///
/// Cố ý KHÔNG có <c>Granularity</c>: "kỳ" ở đây là khoảng from → to do người gọi chọn, kỳ trước
/// luôn cùng số ngày. Muốn so tuần/tháng thì truyền khoảng ngày tương ứng — không định nghĩa lại
/// tuần/tháng lần thứ hai (xem doc <see cref="Services.RevenueComparisonService"/>).
/// </summary>
public class RevenueComparisonRequest
{
    /// <summary>
    /// Ngày bắt đầu KỲ HIỆN TẠI (lịch Việt Nam), TÍNH CẢ. Bỏ trống thì lấy 29 ngày trước
    /// <see cref="To"/> — cùng mặc định 30 ngày của báo cáo gốc.
    /// </summary>
    public DateOnly? From { get; set; }

    /// <summary>Ngày kết thúc kỳ hiện tại (lịch Việt Nam), TÍNH CẢ. Bỏ trống thì lấy hôm nay (giờ VN).</summary>
    public DateOnly? To { get; set; }

    /// <summary>Chỉ tính các chuyến thuộc tuyến này — áp dụng cho CẢ HAI kỳ; bỏ trống = toàn công ty.</summary>
    public Guid? RouteId { get; set; }
}
