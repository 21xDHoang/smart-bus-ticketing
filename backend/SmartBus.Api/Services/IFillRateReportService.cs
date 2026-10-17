using SmartBus.Api.Dtos.Reports;

namespace SmartBus.Api.Services;

/// <summary>
/// Báo cáo tỷ lệ lấp đầy theo chuyến / tuyến / khung giờ (US 20 "Tỷ lệ lấp đầy", Sprint 4, task
/// *"API thống kê tỷ lệ lấp đầy theo chuyến, tuyến, khung giờ"* — Nguyễn Duy Kiên).
///
/// Một lượt gọi trả ba cách nhìn trên CÙNG một tập chuyến (từng chuyến, gộp tuyến, gộp khung giờ
/// khởi hành) + dải tóm tắt. Nền số liệu giống <see cref="IPeakHoursReportService"/>: chuyến khởi
/// hành trong kỳ theo giờ Việt Nam (UTC+7 cố định), hành khách = vé <c>Paid</c> + <c>Used</c> (vé
/// <c>Cancelled</c> bỏ). Khác ở mẫu số: tỷ lệ lấp đầy chia cho **sức chứa ghế của xe** chạy chuyến
/// (<see cref="Entities.Bus.Capacity"/>), không chia cho số chuyến.
///
/// Mọi nhánh lỗi trả qua <see cref="ServiceResult{T}"/>, không ném: khoảng ngày sai hoặc
/// <c>timeSlot</c> lạ → Invalid (400); <c>routeId</c> không tồn tại → NotFound (404).
///
/// Hợp đồng đầy đủ: docs/api-contract.md mục "Tỷ lệ lấp đầy — /reports/fill-rate".
/// </summary>
public interface IFillRateReportService
{
    /// <summary>
    /// Tỷ lệ lấp đầy của mọi chuyến trong kỳ sau lọc, kèm bản gộp theo tuyến và theo khung giờ.
    /// </summary>
    Task<ServiceResult<FillRateReportResponse>> GetAsync(
        FillRateReportRequest request,
        CancellationToken cancellationToken);
}
