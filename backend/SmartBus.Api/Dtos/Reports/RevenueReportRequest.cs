namespace SmartBus.Api.Dtos.Reports;

/// <summary>
/// Tham số query của GET /api/reports/revenue (US 19 "Doanh thu", Sprint 4 — Trần Trung Hiếu).
///
/// Tất cả đều bỏ trống được: <c>granularity</c> mặc định <c>day</c>, <c>from</c>/<c>to</c> mặc
/// định 30 ngày gần nhất TÍNH CẢ hôm nay (giờ Việt Nam), <c>routeId</c> bỏ trống = toàn công ty.
/// Hợp đồng: docs/api-contract.md mục "Báo cáo doanh thu — /reports/revenue".
/// </summary>
public class RevenueReportRequest
{
    /// <summary>
    /// Độ mịn: <c>day</c> / <c>week</c> / <c>month</c> (không phân biệt hoa/thường). Trống hoặc
    /// bỏ hẳn = <c>day</c>. Giá trị lạ → 400 theo trường <c>granularity</c> (kiểm ở service bằng
    /// <see cref="RevenueGranularityCodes.TryParse"/>, không gắn [RegularExpression] — lỗi trả ra
    /// cùng cấu trúc errors thống nhất và test được qua API).
    /// </summary>
    public string? Granularity { get; set; }

    /// <summary>
    /// Ngày bắt đầu (theo lịch Việt Nam, dạng <c>yyyy-MM-dd</c>), TÍNH CẢ. Trống = 29 ngày trước
    /// <c>to</c>. Phải ≤ <c>to</c> và khoảng không quá 366 ngày — nếu không → 400 theo trường.
    /// </summary>
    public DateOnly? From { get; set; }

    /// <summary>Ngày kết thúc (theo lịch Việt Nam), TÍNH CẢ. Trống = hôm nay (giờ Việt Nam).</summary>
    public DateOnly? To { get; set; }

    /// <summary>
    /// Chỉ tính doanh thu các chuyến thuộc tuyến này — bỏ trống = toàn công ty. Tuyến không tồn
    /// tại → 404.
    /// </summary>
    public Guid? RouteId { get; set; }
}
