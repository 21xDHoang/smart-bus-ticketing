using SmartBus.Api.Entities;

namespace SmartBus.Api.Dtos.Refunds;

/// <summary>
/// Kết quả MỘT lượt gọi cổng hoàn tiền (US 8) — <c>IRefundGatewayCaller</c> trả về, service thử
/// lại (task dòng 30) đọc để lật trạng thái khoản hoàn và ghi <see cref="RefundLog"/>.
///
/// Dùng lại <see cref="RefundLogResult"/> làm kiểu kết quả chứ không dựng enum riêng: kết quả của
/// lượt gọi CHÍNH LÀ giá trị ghi vào nhật ký, tách làm hai bộ giá trị là mở đường cho hai bên lệch
/// nhau — đúng lối lập luận đã ghi ở doc-comment enum đó.
/// </summary>
public class RefundGatewayCallOutcome
{
    /// <summary>
    /// Cổng trả lời gì: <see cref="RefundLogResult.Succeeded"/> (đã hoàn tiền),
    /// <see cref="RefundLogResult.Failed"/> (từ chối/lỗi — được phép xếp lịch thử lại), hoặc
    /// <see cref="RefundLogResult.Unresolved"/> (không trả lời — khoản giữ nguyên
    /// <see cref="RefundStatus.Pending"/> và KHÔNG xếp lịch, xem docs/40 §4.3).
    /// </summary>
    public RefundLogResult Result { get; set; }

    /// <summary>
    /// Lời cổng trả về (hoặc mô tả lỗi) — ghi vào <see cref="RefundLog.Message"/> và, khi thất bại,
    /// vào <see cref="Refund.FailReason"/>. Null/chuỗi trắng khi cổng không trả lời.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Mã giao dịch phía cổng. Có nghĩa nhất khi <see cref="Result"/> là
    /// <see cref="RefundLogResult.Succeeded"/> — lúc đó nó vào thẳng
    /// <see cref="Refund.GatewayTransactionId"/>. Lượt thất bại vẫn có thể mang mã, nên nó được ghi
    /// vào nhật ký ở mọi lượt.
    /// </summary>
    public string? GatewayTransactionId { get; set; }
}
