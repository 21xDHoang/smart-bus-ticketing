using SmartBus.Api.Dtos.Reports;

namespace SmartBus.Api.Services;

/// <summary>
/// Phân tích khung giờ cao điểm theo mật độ hành khách — GET /api/reports/peak-hours (US 20
/// "Tỷ lệ lấp đầy", Sprint 4, task *"API phân tích khung giờ cao điểm theo mật độ hành khách"* —
/// Trần Trung Hiếu). Hợp đồng: docs/api-contract.md mục "Khung giờ cao điểm — /reports/peak-hours".
/// </summary>
public interface IPeakHoursReportService
{
    Task<ServiceResult<PeakHoursReportResponse>> GetAsync(
        PeakHoursReportRequest request,
        CancellationToken cancellationToken);
}
