using SmartBus.Api.Dtos.Refunds;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Lớp bọc quanh <see cref="RefundAttemptService"/>: chạy đúng lượt gọi cổng như cũ rồi đưa kết quả
/// cho <see cref="IRefundResultNotifier"/> báo cho khách — task Sprint 4 *"Thông báo kết quả hoàn tiền
/// qua email và trên web"* (dòng 34, Phùng Duy Hoàng). Cùng lối
/// <see cref="NotifyingSeatHoldCreateService"/>: <c>IRefundAttemptService</c> trong DI trỏ vào lớp bọc
/// này (Program.cs), nên MỌI đường gọi cổng đi qua đây — job theo lịch, nút "Thử lại" của màn quản
/// trị, và lượt hoàn tự động lúc duyệt huỷ — mà không đường nào phải tự nhớ báo tin.
///
/// <para>
/// <b>Vì sao bọc ở tầng interface chứ không gọi notifier thẳng trong
/// <see cref="RefundAttemptService"/>:</b> service đó giữ luồng giành dòng <c>xmin</c> (docs/35 §4.2)
/// — trộn thêm một việc có thể chạm I/O kênh thông báo vào cùng lớp là mở đường cho lỗi phụ trợ làm
/// hỏng lượt gọi cổng đã trả tiền. Bọc ở ngoài thì <see cref="RefundAttemptService"/> giữ nguyên
/// trọn vẹn, còn notifier nuốt lỗi theo đúng hợp đồng của nó.
/// </para>
///
/// <para>
/// <b>Vì sao lớp bọc KHÔNG tự lọc "khi nào đáng báo":</b> khác
/// <see cref="NotifyingSeatHoldCreateService"/> (lọc bằng <c>result.Success</c> ngay trong lớp bọc),
/// phán quyết "kết quả cuối hay chưa" phải đọc dòng <c>Refunds</c> ĐÃ LƯU — thứ chỉ notifier có
/// <c>AppDbContext</c> để làm, và là chính sách thuộc về nó. Lớp bọc ở đây cố ý mỏng: chuyển tiếp
/// nguyên tử, không quyết định gì.
/// </para>
///
/// <para>
/// ⚠️ <c>cancellationToken</c> vẫn chuyển tiếp nguyên vẹn cho lượt gọi cổng (hợp đồng của
/// <see cref="IRefundAttemptService"/>), nhưng <see cref="IRefundResultNotifier"/> cố ý không nhận nó
/// — xem doc-comment interface đó.
/// </para>
/// </summary>
public class NotifyingRefundAttemptService : IRefundAttemptService
{
    private readonly RefundAttemptService _inner;

    private readonly IRefundResultNotifier _notifier;

    public NotifyingRefundAttemptService(RefundAttemptService inner, IRefundResultNotifier notifier)
    {
        _inner = inner;
        _notifier = notifier;
    }

    /// <inheritdoc />
    public async Task<RefundAttemptOutcome> ExecuteAsync(
        Guid refundId,
        RefundLogTrigger trigger,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var outcome = await _inner.ExecuteAsync(refundId, trigger, now, cancellationToken);

        // Sau khi lượt gọi đã xong hẳn (kết quả đã lưu). NotifyAsync không bao giờ ném lỗi và không
        // nhận token — kể cả khi app đang tắt dở, thông báo cho khoản vừa chốt kết quả vẫn phải được
        // ghi.
        await _notifier.NotifyAsync(outcome);

        return outcome;
    }
}
