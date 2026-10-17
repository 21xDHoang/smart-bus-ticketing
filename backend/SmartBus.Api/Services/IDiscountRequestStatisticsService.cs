using SmartBus.Api.Dtos.DiscountRequests;

namespace SmartBus.Api.Services;

/// <summary>
/// Thống kê số lượng hồ sơ ưu đãi theo trạng thái — GET /api/discount-requests/statistics
/// (Sprint 4 dòng 55, US 17). Hợp đồng đầy đủ ở mục "Hồ sơ ưu đãi — /discount-requests" của
/// docs/api-contract.md.
///
/// Không có tham số nên không có nhánh lỗi nghiệp vụ nào: đúng quyền là luôn trả về một kết quả
/// (hệ thống chưa có hồ sơ nào thì mọi con đếm bằng 0). Cùng lối
/// <see cref="IVoucherStatisticsService"/> và <see cref="IFeedbackStatisticsService"/>.
/// </summary>
public interface IDiscountRequestStatisticsService
{
    /// <summary>
    /// Đếm toàn bộ hồ sơ ưu đãi trên hệ thống theo trạng thái, kèm hai tập con còn hiệu lực / hết
    /// hạn của nhóm đã duyệt.
    /// </summary>
    Task<DiscountRequestStatisticsResponse> GetAsync(CancellationToken cancellationToken = default);
}
