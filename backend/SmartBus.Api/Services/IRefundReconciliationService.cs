using SmartBus.Api.Dtos.Refunds;

namespace SmartBus.Api.Services;

/// <summary>
/// Đối soát hoàn tiền hằng ngày (US 8 — task *"Đối soát hoàn tiền hằng ngày + cảnh báo khi lệch số
/// liệu"*, dòng 32 Sprint 4, Nguyễn Duy Kiên).
///
/// <para>
/// Một lượt chạy trả về <see cref="RefundReconciliationReport"/> — danh sách điểm lệch tìm được
/// trong sổ nội bộ, cộng phần đối chiếu sao kê cổng KHI có nguồn sao kê. Service này chỉ ĐỌC: nó
/// tuyệt đối không lật trạng thái khoản hoàn, không gọi cổng, không xoá gì. Việc báo cho quản trị là
/// của <see cref="IRefundReconciliationNotifier"/>, việc canh nhịp là của
/// <see cref="RefundReconciliationBackgroundService"/>.
/// </para>
///
/// <para>
/// 🔴 <b>Vì sao tuyệt đối không tự sửa dữ liệu:</b> mọi điểm lệch ở đây đều nằm trên dòng tiền.
/// Một khoản <c>Pending</c> có nhật ký báo thành công rất có thể ĐÃ được cổng trả — tự lật nó sang
/// <c>Succeeded</c> là chốt một kết luận chưa đối chiếu; tự gọi lại cổng còn tệ hơn: hoàn hai lần.
/// Bước này chỉ được trả lời "tôi thấy gì", người trực quyết định bằng sao kê.
/// </para>
/// </summary>
public interface IRefundReconciliationService
{
    /// <summary>
    /// Chạy một lượt đối soát cho khoảng 24 giờ kết thúc tại <paramref name="utcNow"/>.
    ///
    /// <paramref name="utcNow"/> do người gọi truyền vào (không tự gọi <c>DateTime.UtcNow</c>) để
    /// test ghim được mốc thời gian — cùng lối <see cref="IRefundRetryService.SweepAsync"/>.
    /// </summary>
    Task<RefundReconciliationReport> RunAsync(
        DateTime utcNow,
        CancellationToken cancellationToken = default);
}
