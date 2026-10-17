namespace SmartBus.Api.Dtos.Reports;

/// <summary>
/// Tham số query chung của ba báo cáo phân rã doanh thu (US 19 "Doanh thu", Sprint 4 — Nguyễn
/// Duy Kiên): theo tuyến, theo xe, theo phương thức thanh toán.
///
/// Chỉ có khoảng ngày — cố ý KHÔNG có <c>granularity</c> (ba báo cáo này trả một dòng cho mỗi
/// nhóm, không có trục thời gian) và KHÔNG có <c>routeId</c> (báo cáo theo tuyến đã trả mọi
/// tuyến). <c>from</c>/<c>to</c> mặc định 30 ngày gần nhất TÍNH CẢ hôm nay (giờ Việt Nam), cùng
/// quy tắc với <see cref="RevenueReportRequest"/>.
/// Hợp đồng: docs/api-contract.md mục "Báo cáo doanh thu — /reports/revenue".
/// </summary>
public class RevenueBreakdownRequest
{
    /// <summary>
    /// Ngày bắt đầu (theo lịch Việt Nam, dạng <c>yyyy-MM-dd</c>), TÍNH CẢ. Trống = 29 ngày trước
    /// <c>to</c>. Phải ≤ <c>to</c> và khoảng không quá 366 ngày — nếu không → 400 theo trường.
    /// </summary>
    public DateOnly? From { get; set; }

    /// <summary>Ngày kết thúc (theo lịch Việt Nam), TÍNH CẢ. Trống = hôm nay (giờ Việt Nam).</summary>
    public DateOnly? To { get; set; }
}
