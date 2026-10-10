using SmartBus.Api.Dtos.Payments;

namespace SmartBus.Api.Services;

/// <summary>
/// Hợp đồng chốt kết quả thanh toán — tách interface để endpoint callback (MoMo IPN, VNPay
/// Return/IPN — chưa dựng) và job đối soát dùng chung một đường, đồng thời test không phải dựng
/// HTTP. Task *"Xử lý idempotency: chống trừ tiền 2 lần khi callback trùng"* — Phùng Duy Hoàng.
/// </summary>
public interface IPaymentSettlementService
{
    /// <summary>
    /// Chốt một lượt cổng báo kết quả cho giao dịch <paramref name="callback"/>.PaymentCode.
    /// <paramref name="providerCode"/> là cổng đang gọi (hằng của PaymentProviderCodes — dùng đối
    /// chiếu với cột MethodCode đã lưu lúc tạo giao dịch).
    ///
    /// Kết quả: <c>Invalid</c> khi chữ ký không hợp lệ (endpoint trả 400) · <c>NotFound</c> khi
    /// không có bản ghi khớp mã đơn + cổng + số tiền (endpoint trả 404) · <c>Ok</c> khi đã chốt
    /// xong — kể cả bản gửi lại, phân biệt bằng
    /// <see cref="PaymentSettlementOutcome.SettledNow"/> (endpoint luôn trả 204).
    /// </summary>
    Task<ServiceResult<PaymentSettlementOutcome>> SettleAsync(
        string providerCode, PaymentCallbackResult callback, CancellationToken cancellationToken = default);
}
