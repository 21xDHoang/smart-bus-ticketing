using SmartBus.Api.Dtos.Vouchers;

namespace SmartBus.Api.Services;

/// <summary>
/// Thống kê hiệu quả voucher đã phát hành — GET /api/vouchers/statistics (dòng 54, US 18). Hợp đồng
/// đầy đủ ở mục "Voucher — /vouchers", phần "GET /vouchers/statistics" của docs/api-contract.md.
///
/// Không có tham số nên không có nhánh lỗi nghiệp vụ nào: đúng quyền là luôn trả về một kết quả (hệ
/// thống chưa có voucher nào thì mọi con đếm bằng 0 và <c>items</c> rỗng). Cùng lối
/// <see cref="IFeedbackStatisticsService"/>.
/// </summary>
public interface IVoucherStatisticsService
{
    /// <summary>
    /// Dựng bảng hiệu quả trên TOÀN BỘ voucher đã phát hành, mọi trạng thái.
    /// </summary>
    Task<VoucherStatisticsResponse> GetAsync(CancellationToken cancellationToken = default);
}
