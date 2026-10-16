using SmartBus.Api.Dtos.Refunds;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Ranh giới giữa <see cref="RefundGatewayCaller"/> và MỘT cổng hoàn tiền cụ thể (US 8 — task
/// *"API gọi cổng thanh toán để hoàn tiền (MoMo/VNPay/ZaloPay)"*, dòng 29 — Trần Trung Hiếu).
///
/// Mỗi client nói chuyện với ĐÚNG MỘT cổng, nhận trọn dữ liệu giao dịch gốc qua <see cref="Payment"/>
/// (mã giao dịch cổng, mốc thu tiền…) và dịch kết luận của cổng sang
/// <see cref="RefundGatewayCallOutcome"/> — cùng vai trò "chuẩn hoá kết luận" của
/// <see cref="IPaymentGateway"/> ở chiều thu tiền.
///
/// Ranh giới lỗi vận chuyển, khớp hợp đồng <see cref="IRefundGatewayCaller"/>: HTTP chết, timeout,
/// cổng không trả lời thì client NÉM LÊN (cùng lối <see cref="MoMoGatewayService"/>) —
/// <see cref="RefundGatewayCaller"/> bọc lại thành <see cref="RefundLogResult.Unresolved"/> vì cổng
/// không trả lời thì không ai được phép đoán. Còn cổng TRẢ LỜI từ chối thì client trả
/// <see cref="RefundLogResult.Failed"/> kèm lời cổng. Thiếu dữ liệu gốc không gọi cổng được
/// (thiếu mã giao dịch, thiếu mốc thu tiền) cũng trả <c>Failed</c> — đó là câu trả lời chắc chắn,
/// thử lại cũng không đổi.
/// </summary>
public interface IRefundGatewayClient
{
    /// <summary>
    /// Thực hiện một lượt gọi hoàn tiền cho giao dịch gốc <paramref name="payment"/>.
    /// Không ghi CSDL — mọi thay đổi trạng thái là việc của <see cref="RefundAttemptService"/>.
    /// </summary>
    Task<RefundGatewayCallOutcome> RefundAsync(
        RefundGatewayCallRequest request,
        Payment payment,
        CancellationToken cancellationToken = default);
}
