namespace SmartBus.Api.Dtos.Reports;

/// <summary>
/// Tham số query của GET /api/reports/peak-hours (US 20 "Tỷ lệ lấp đầy", Sprint 4 — Trần Trung
/// Hiếu).
///
/// Tất cả đều bỏ trống được: <c>from</c>/<c>to</c> mặc định 30 ngày gần nhất TÍNH CẢ hôm nay
/// (giờ Việt Nam), <c>routeId</c> bỏ trống = toàn công ty. Cùng quy tắc khoảng thời gian của
/// GET /reports/revenue (US 19): ngày lịch Việt Nam, trần 366 ngày.
/// Hợp đồng: docs/api-contract.md mục "Khung giờ cao điểm — /reports/peak-hours".
/// </summary>
public class PeakHoursReportRequest
{
    /// <summary>
    /// Ngày bắt đầu (theo lịch Việt Nam, dạng <c>yyyy-MM-dd</c>), TÍNH CẢ. Trống = 29 ngày trước
    /// <c>to</c>. Phải ≤ <c>to</c> và khoảng không quá 366 ngày — nếu không → 400 theo trường.
    /// </summary>
    public DateOnly? From { get; set; }

    /// <summary>Ngày kết thúc (theo lịch Việt Nam), TÍNH CẢ. Trống = hôm nay (giờ Việt Nam).</summary>
    public DateOnly? To { get; set; }

    /// <summary>
    /// Chỉ tính khách của các chuyến thuộc tuyến này — bỏ trống = toàn công ty. Tuyến không tồn
    /// tại → 404.
    /// </summary>
    public Guid? RouteId { get; set; }
}
