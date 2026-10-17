using SmartBus.Api.Dtos.Reports;

namespace SmartBus.Api.Services;

/// <summary>
/// Hợp đồng ba báo cáo phân rã doanh thu (US 19 "Doanh thu", Sprint 4 — Nguyễn Duy Kiên):
/// theo tuyến, theo xe, theo phương thức thanh toán.
///
/// Cùng nền số liệu với <see cref="IRevenueReportService"/>: bảng <c>Payments</c>, chỉ giao dịch
/// <c>Success</c>, mốc thời gian <c>PaidAt ?? CreatedAt</c>, trục ngày theo giờ Việt Nam (UTC+7),
/// mặc định 30 ngày gần nhất, trần 366 ngày. Khác duy nhất ở hình dạng kết quả: một dòng cho mỗi
/// tuyến / xe / phương thức thay vì bucket theo thời gian. Nhờ dùng chung định nghĩa, tổng của ba
/// báo cáo này khớp <c>GET /reports/revenue</c> cùng khoảng ngày (hai bên chỉ khác thời điểm đọc
/// — báo cáo thời gian có đệm 60 giây, ba báo cáo này không).
///
/// Mọi nhánh lỗi trả qua <see cref="ServiceResult{T}"/>, không ném: khoảng ngày sai → Invalid.
/// Không có nhánh NotFound vì ba endpoint không nhận tham số khoá nào.
/// </summary>
public interface IRevenueBreakdownService
{
    /// <summary>Doanh thu theo tuyến — một dòng cho mỗi tuyến có giao dịch Success trong kỳ.</summary>
    Task<ServiceResult<RevenueByRouteResponse>> GetByRouteAsync(
        RevenueBreakdownRequest request,
        CancellationToken cancellationToken);

    /// <summary>Doanh thu theo xe — một dòng cho mỗi xe chạy chuyến có giao dịch Success trong kỳ.</summary>
    Task<ServiceResult<RevenueByBusResponse>> GetByBusAsync(
        RevenueBreakdownRequest request,
        CancellationToken cancellationToken);

    /// <summary>Doanh thu theo phương thức — một dòng cho mỗi mã phương thức có giao dịch Success trong kỳ.</summary>
    Task<ServiceResult<RevenueByPaymentMethodResponse>> GetByPaymentMethodAsync(
        RevenueBreakdownRequest request,
        CancellationToken cancellationToken);
}
