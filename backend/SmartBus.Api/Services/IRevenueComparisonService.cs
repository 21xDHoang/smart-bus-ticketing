using SmartBus.Api.Dtos.Reports;

namespace SmartBus.Api.Services;

/// <summary>
/// So sánh doanh thu kỳ hiện tại với kỳ liền trước cùng độ dài — GET
/// /api/reports/revenue/comparison (US 19 "Doanh thu", Sprint 4, task *"API so sánh doanh thu
/// giữa các kỳ (tăng/giảm %)"* — Phùng Duy Hoàng). Hợp đồng đầy đủ ở mục "Báo cáo doanh thu —
/// /reports/revenue" của docs/api-contract.md.
///
/// Service này KHÔNG tự truy vấn bảng Payments. Nó gọi <see cref="IRevenueReportService"/> hai
/// lần (kỳ hiện tại + kỳ liền trước) để hai endpoint dùng chung đúng một nguồn số: mặc định
/// 30 ngày, múi giờ Việt Nam, PaidAt ?? CreatedAt, chỉ Success, trần 366 ngày và kiểm tra tuyến
/// đều nằm ở một chỗ. Tự viết truy vấn thứ hai là mở đường cho hai báo cáo lệch nhau.
/// </summary>
public interface IRevenueComparisonService
{
    Task<ServiceResult<RevenueComparisonResponse>> GetAsync(
        RevenueComparisonRequest request,
        CancellationToken cancellationToken);
}
