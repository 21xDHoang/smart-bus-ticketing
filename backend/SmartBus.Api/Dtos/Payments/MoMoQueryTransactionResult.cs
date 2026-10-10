namespace SmartBus.Api.Dtos.Payments;

/// <summary>
/// Kết quả gọi MoMo hỏi trạng thái một giao dịch (<see cref="Services.IMoMoGatewayService.QueryTransactionAsync"/>)
/// — phần "cổng" của task *"API kiểm tra trạng thái giao dịch + đối soát tự động"* (US 6, Trần
/// Trung Hiếu). Dùng cho job đối soát: với giao dịch còn Pending ở bảng Payments, hỏi cổng xem
/// thực tế đã về đâu — IPN có thể đã lọt mất (mạng, restart app), cổng mới là nguồn sự thật.
/// </summary>
public class MoMoQueryTransactionResult
{
    /// <summary>Hỏi cổng thành công và <see cref="ResultCode"/> là 0 — giao dịch đã thanh toán.</summary>
    public bool Success { get; set; }

    /// <summary>Mã giao dịch do MoMo sinh — ghi vào gatewayTransactionId của bảng Payments.</summary>
    public long TransId { get; set; }

    /// <summary>Số tiền cổng ghi nhận (VND) — đối chiếu với Amount của bảng Payments.</summary>
    public long Amount { get; set; }

    /// <summary>Mã kết quả do MoMo trả — 0 là thành công, khác 0 kèm <see cref="Message"/>.</summary>
    public int ResultCode { get; set; }

    /// <summary>Thông báo do MoMo trả.</summary>
    public string Message { get; set; } = string.Empty;
}
