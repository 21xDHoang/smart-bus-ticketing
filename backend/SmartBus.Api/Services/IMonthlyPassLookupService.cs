using SmartBus.Api.Dtos.MonthlyPasses;

namespace SmartBus.Api.Services;

/// <summary>
/// Tra cứu vé tháng đang hoạt động của chính người gọi (GET /api/monthly-passes/me) — story 16
/// "Đăng ký vé tháng", Phùng Duy Hoàng. Hợp đồng đầy đủ ở mục "Vé tháng — /monthly-passes" của
/// docs/api-contract.md.
///
/// Cố ý đứng riêng thay vì nối vào IMonthlyPassRenewalService: một bên chỉ đọc, một bên ghi —
/// cùng lối các cặp service đã tách từ Sprint 1 (IStopService / IRouteStopService).
/// </summary>
public interface IMonthlyPassLookupService
{
    /// <summary>
    /// Vé của <paramref name="userId"/> có hiệu lực NGAY LÚC NÀY: validFrom &lt;= bây giờ &lt;= validTo
    /// (tính cả hai mốc). KHÔNG đọc cột Status — cột đó có độ trễ (job quét vé hết hạn của Kiên),
    /// so nó là so sai.
    ///
    /// Sắp theo validTo tăng dần rồi theo code cho ổn định. Không có vé nào là mảng rỗng —
    /// không phải lỗi, nên không có nhánh ServiceResult.
    /// </summary>
    Task<IReadOnlyList<MonthlyPassResponse>> GetActiveAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
