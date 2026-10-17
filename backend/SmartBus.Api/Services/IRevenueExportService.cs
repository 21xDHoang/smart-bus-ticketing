using SmartBus.Api.Dtos.Reports;

namespace SmartBus.Api.Services;

/// <summary>
/// Xuất báo cáo doanh thu ra file Excel/PDF — <c>GET /api/reports/revenue/export</c>. US 24
/// "Xuất dữ liệu", Sprint 4 dòng 78 — Phùng Duy Hoàng.
///
/// Service KHÔNG tự tính doanh thu. Nó gọi <see cref="IRevenueReportService"/> (bản có đệm 60
/// giây) rồi chỉ dàn kết quả ra file — nhờ vậy số trong file luôn khớp số endpoint JSON trả về,
/// không có định nghĩa doanh thu thứ hai.
/// </summary>
public interface IRevenueExportService
{
    /// <summary>
    /// Dựng file cho khoảng thời gian trong <paramref name="request"/>. Mọi lỗi của báo cáo gốc
    /// (granularity lạ, from sau to, quá 366 ngày, tuyến không tồn tại) được chuyển nguyên sang;
    /// lỗi định dạng file do service này tự kiểm.
    /// </summary>
    Task<ServiceResult<ReportExportFile>> ExportAsync(
        RevenueExportRequest request,
        string? exportedBy,
        CancellationToken cancellationToken);
}
