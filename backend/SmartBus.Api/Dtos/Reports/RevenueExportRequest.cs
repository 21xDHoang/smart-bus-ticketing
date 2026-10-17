namespace SmartBus.Api.Dtos.Reports;

/// <summary>
/// Tham số của <c>GET /api/reports/revenue/export</c> — dùng lại đúng bộ lọc của báo cáo doanh
/// thu JSON, thêm một tham số chọn định dạng file.
///
/// Kế thừa <see cref="RevenueReportRequest"/> là cố ý: hai endpoint phải nhận cùng tham số với
/// cùng mặc định, nên chỉ có một nơi khai báo <c>from</c>/<c>to</c>/<c>granularity</c>/<c>routeId</c>.
/// Thêm field vào báo cáo gốc thì bản xuất tự nhận theo — không có danh sách tham số thứ hai để
/// lệch.
/// </summary>
public class RevenueExportRequest : RevenueReportRequest
{
    /// <summary>
    /// Định dạng file: <c>xlsx</c> (mặc định) hoặc <c>pdf</c>, không phân biệt hoa/thường.
    /// Giá trị khác → 400 gắn vào <c>errors.format</c>.
    /// </summary>
    public string? Format { get; set; }
}
