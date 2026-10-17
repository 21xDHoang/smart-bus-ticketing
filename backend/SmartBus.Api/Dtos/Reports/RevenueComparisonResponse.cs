namespace SmartBus.Api.Dtos.Reports;

/// <summary>
/// Hình dạng trả về của GET /api/reports/revenue/comparison (US 19 "Doanh thu", Sprint 4 —
/// Phùng Duy Hoàng). Hợp đồng đầy đủ ở mục "Báo cáo doanh thu — /reports/revenue" của
/// docs/api-contract.md.
///
/// Một phản hồi là một phép TRỪ: kỳ hiện tại (from → to) so với kỳ liền trước CÙNG SỐ NGÀY
/// (previousFrom → previousTo, kết thúc ngay trước from). Số tuyệt đối và số % đi cùng nhau để
/// thẻ số liệu hiển thị được cả hai mà không phải tự tính lại.
/// </summary>
public class RevenueComparisonResponse
{
    /// <summary>Ngày bắt đầu kỳ hiện tại (lịch Việt Nam) — lặp lại tham số sau khi áp dụng mặc định.</summary>
    public DateOnly From { get; set; }

    /// <summary>Ngày kết thúc kỳ hiện tại (lịch Việt Nam), TÍNH CẢ.</summary>
    public DateOnly To { get; set; }

    /// <summary>Ngày bắt đầu kỳ liền trước — cùng số ngày với kỳ hiện tại.</summary>
    public DateOnly PreviousFrom { get; set; }

    /// <summary>Ngày kết thúc kỳ liền trước (TÍNH CẢ) — luôn là <see cref="From"/> trừ một ngày.</summary>
    public DateOnly PreviousTo { get; set; }

    /// <summary>Doanh thu kỳ hiện tại (VND) — chỉ giao dịch Success, cùng nền với báo cáo gốc.</summary>
    public decimal CurrentRevenue { get; set; }

    /// <summary>Doanh thu kỳ liền trước (VND).</summary>
    public decimal PreviousRevenue { get; set; }

    /// <summary>Chênh lệch doanh thu tuyệt đối (VND): currentRevenue − previousRevenue. Âm = giảm.</summary>
    public decimal RevenueChange { get; set; }

    /// <summary>
    /// % thay đổi doanh thu, làm tròn 2 chữ số thập phân. <c>null</c> khi kỳ trước bằng 0 —
    /// không có nền để chia, và 0% sẽ là lời nói dối (FE hiển thị "—").
    /// </summary>
    public decimal? RevenueChangePercent { get; set; }

    /// <summary>Số giao dịch Success của kỳ hiện tại.</summary>
    public int CurrentPayments { get; set; }

    /// <summary>Số giao dịch Success của kỳ liền trước.</summary>
    public int PreviousPayments { get; set; }

    /// <summary>Chênh lệch số giao dịch: currentPayments − previousPayments.</summary>
    public int PaymentsChange { get; set; }

    /// <summary>% thay đổi số giao dịch — cùng quy tắc <c>null</c> khi kỳ trước bằng 0.</summary>
    public decimal? PaymentsChangePercent { get; set; }
}
