namespace SmartBus.Api.Services;

/// <summary>
/// Đối soát giao dịch thanh toán còn <c>Pending</c> quá hạn (US 6, task *"đối soát tự động"* —
/// Trần Trung Hiếu). IPN có thể lọt mất (mạng nghẽn, app restart đúng lúc callback bay tới) — đây
/// là lưới thứ hai: hỏi thẳng cổng rồi chốt qua đúng một cửa <see cref="IPaymentSettlementService"/>,
/// y hệt nhánh callback.
///
/// Chỉ hai cổng hỏi được trạng thái: MoMo (<c>QueryTransactionAsync</c>) và ZaloPay
/// (<c>QueryOrderAsync</c>) — VNPay v2.1.0 không có API hỏi trạng thái nên giao dịch VNPay bỏ qua,
/// kết quả của chúng chỉ đến qua Return URL / IPN (đã chốt ở mục "Thanh toán" của hợp đồng).
/// </summary>
public interface IPaymentReconciliationService
{
    /// <summary>
    /// Quét một lượt: mọi giao dịch <c>Pending</c> có <c>CreatedAt</c> quá
    /// <paramref name="now"/> 5 phút được hỏi cổng rồi chốt. Nuốt mọi lỗi của MỘT giao dịch (cổng
    /// không trả lời, CSDL chập chờn) để lượt sau quét tiếp — không giao dịch hỏng nào được kéo
    /// sập cả vòng quét. Trả số giao dịch đã xử lý để ghi log.
    /// </summary>
    Task<int> SweepOnceAsync(DateTime now, CancellationToken cancellationToken = default);
}
