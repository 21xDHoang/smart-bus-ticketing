using SmartBus.Api.Dtos.Refunds;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Thực hiện MỘT lượt gọi cổng hoàn tiền cho một khoản (US 8 — task *"Xử lý hoàn tiền thất bại +
/// retry theo lịch"*, Phùng Duy Hoàng). Ba người gọi, phân biệt bằng <c>trigger</c>:
/// <list type="bullet">
/// <item>Job thử lại theo lịch (<c>RefundRetryService</c> — đã dựng) → <c>ScheduledRetry</c>;</item>
/// <item>Nút "Thử lại" của màn Admin (chưa dựng — nối vào ĐÚNG hàm này với <c>ManualRetry</c>,
/// không tự viết luồng gọi cổng riêng);</item>
/// <item>Service hoàn tiền tự động khi duyệt huỷ vé (dòng 28 — Kiên, chưa dựng) →
/// <c>Initial</c>.</item>
/// </list>
///
/// Bảo đảm xuyên suốt: mỗi khoản có ĐÚNG MỘT lượt gọi cổng tại một thời điểm — lượt gọi giành dòng
/// bằng <c>xmin</c> TRƯỚC khi gọi cổng (docs/40 §4.2); lượt thua trả <c>Claimed = false</c> mà
/// không gọi gì và không ghi gì.
///
/// Vì sao <c>now</c> là tham số: mọi mốc thời gian của lượt gọi (<c>NextRetryAt</c>,
/// <c>ProcessedAt</c>, mốc dòng <see cref="RefundLog"/>) lấy từ đúng MỘT giá trị do người gọi
/// truyền vào — test ghim được mốc, job truyền <c>DateTime.UtcNow</c> một lần cho cả lượt.
/// </summary>
public interface IRefundAttemptService
{
    /// <summary>
    /// Một lượt đầy đủ: kiểm điều kiện theo <paramref name="trigger"/> → giành dòng → gọi cổng →
    /// ghi kết quả + đúng một dòng <see cref="RefundLog"/>. Lỗi phía cổng không bao giờ ném ra
    /// (thành <c>Unresolved</c>); lỗi CSDL ngoài dự kiến và <c>OperationCanceledException</c> lúc
    /// app tắt thì ném ra cho người gọi xử lý.
    /// </summary>
    /// <param name="now">
    /// Mốc thời gian của lượt gọi (UTC) — dùng cho cả lúc giành dòng lẫn lúc ghi kết quả; trong
    /// hàm KHÔNG gọi <c>DateTime.UtcNow</c> lần nữa.
    /// </param>
    Task<RefundAttemptOutcome> ExecuteAsync(
        Guid refundId,
        RefundLogTrigger trigger,
        DateTime now,
        CancellationToken cancellationToken = default);
}
