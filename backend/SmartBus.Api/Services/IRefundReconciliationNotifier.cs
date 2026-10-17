using SmartBus.Api.Dtos.Refunds;

namespace SmartBus.Api.Services;

/// <summary>
/// Báo cho quản trị khi lượt đối soát hoàn tiền tìm thấy lệch số liệu (US 8 — task *"Đối soát hoàn
/// tiền hằng ngày + cảnh báo khi lệch số liệu"*, dòng 32 Sprint 4, Nguyễn Duy Kiên).
///
/// <para>
/// Một lượt báo làm hai việc, đúng hai vế của A8.5 (cùng lối <see cref="IRefundResultNotifier"/>):
/// ghi MỘT dòng <see cref="Entities.Notification"/> cho mỗi quản trị viên đang hoạt động, rồi đẩy sự
/// kiện qua <c>/hubs/notifications</c>. Bản ghi là thứ đọc lại được; sự kiện chỉ là lời nhắc tức thời.
/// </para>
///
/// <para>
/// 🔴 <b>KHÔNG BAO GIỜ NÉM LỖI.</b> Người gọi là job đối soát — nếu lỗi kênh thông báo nổi lên, lượt
/// đối soát đã tìm ra lệch sẽ bị coi là hỏng và lần chạy sau (24 giờ nữa) mới thử lại. Kết quả đối
/// soát phải được giữ nguyên vẹn dù kênh báo có hỏng; lỗi được đẩy vào ILogger để còn lần ra.
/// </para>
///
/// <para>
/// 🔴 Cũng vì vậy hàm ở đây <b>KHÔNG nhận <c>CancellationToken</c></b>: app tắt ngay sau khi lượt đối
/// soát chạy xong không được làm cảnh báo biến mất — nó là kết quả của một việc đã hoàn thành, đúng
/// lối đã chốt ở <see cref="IRefundResultNotifier"/>.
/// </para>
///
/// <para>
/// <b>Chính sách:</b> chỉ báo khi <see cref="RefundReconciliationReport.HasMismatch"/>; không báo
/// "hôm nay không lệch" (một thông báo mỗi ngày không nói gì sẽ làm quản trị bỏ qua chuông). Chống
/// báo trùng theo (người nhận, loại, NGÀY chạy) — job chạy lại trong cùng ngày không dội thêm dòng.
/// </para>
/// </summary>
public interface IRefundReconciliationNotifier
{
    Task<RefundReconciliationNotifyResult> NotifyAsync(RefundReconciliationReport report);
}

/// <summary>
/// Kết quả một lượt báo — chỉ để job ghi log và để test đếm. Không endpoint nào trả ra ngoài.
/// </summary>
public sealed class RefundReconciliationNotifyResult
{
    /// <summary>Số quản trị viên đang hoạt động tìm được.</summary>
    public int RecipientCount { get; init; }

    /// <summary>Số dòng thông báo thực sự được ghi trong lượt này.</summary>
    public int CreatedCount { get; init; }

    /// <summary>Số người bị bỏ qua vì hôm đó đã nhận cảnh báo đối soát rồi.</summary>
    public int SkippedCount { get; init; }
}
