using SmartBus.Api.Dtos.Reports;

namespace SmartBus.Api.Services;

/// <summary>
/// Hợp đồng báo cáo doanh thu theo ngày / tuần / tháng (task *"API báo cáo doanh thu theo ngày /
/// tuần / tháng"*, US 19 "Doanh thu", Sprint 4 — Trần Trung Hiếu).
///
/// Nguồn số liệu là bảng <c>Payments</c>, chỉ đếm giao dịch <c>Success</c> — doanh thu là tiền
/// ĐÃ THU THỰC, khớp với kiểm thử chéo "số liệu doanh thu khớp với giao dịch thực tế". Mốc thời
/// gian của một giao dịch là <c>PaidAt</c> (thời điểm cổng ghi nhận thu tiền), thiếu thì dùng
/// <c>CreatedAt</c>. Trục thời gian tính theo giờ Việt Nam (UTC+7 cố định) vì báo cáo đọc bởi
/// người Việt: giao dịch 00:30 giờ Việt Nam thuộc về NGÀY HÔM ĐÓ, không phải ngày hôm trước
/// theo UTC.
///
/// Báo cáo theo tuyến / xe / phương thức (Kiên) và so sánh giữa các kỳ (Hoàng) là các task khác
/// cùng story — mở rộng trên cùng bề mặt /reports/revenue.
/// </summary>
public interface IRevenueReportService
{
    /// <summary>
    /// Tổng hợp doanh thu theo độ mịn đã chọn. Ném không — mọi nhánh lỗi trả qua
    /// <see cref="ServiceResult{T}"/>: granularity lạ / khoảng ngày sai → Invalid, tuyến không
    /// tồn tại → NotFound.
    /// </summary>
    Task<ServiceResult<RevenueReportResponse>> GetAsync(
        RevenueReportRequest request,
        CancellationToken cancellationToken);
}
