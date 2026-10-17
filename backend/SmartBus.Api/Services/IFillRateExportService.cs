using SmartBus.Api.Dtos.Reports;

namespace SmartBus.Api.Services;

/// <summary>
/// Xuất báo cáo tỷ lệ lấp đầy ra Excel/PDF — <c>GET /api/reports/fill-rate/export</c>. US 20
/// "Tỷ lệ lấp đầy", Sprint 4 dòng 78 — Phùng Duy Hoàng.
///
/// Đây là nơi DUY NHẤT đang sở hữu định nghĩa tỷ lệ lấp đầy (hợp đồng đã chốt trong
/// docs/api-contract.md). Khi Kiên làm JSON <c>GET /reports/fill-rate</c> (dòng 66), rút phép
/// tính trong service này ra dùng chung — đừng chép công thức.
/// </summary>
public interface IFillRateExportService
{
    Task<ServiceResult<ReportExportFile>> ExportAsync(
        FillRateExportRequest request,
        string? exportedBy,
        CancellationToken cancellationToken);
}
