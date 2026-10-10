namespace SmartBus.Api.Dtos.Payments;

/// <summary>
/// Kết quả gọi ZaloPay hỏi trạng thái một đơn (<see cref="Services.IZaloPayGatewayService.QueryOrderAsync"/>)
/// — phần "cổng" cho job đối soát: với giao dịch còn Pending ở bảng Payments, hỏi cổng xem thực tế
/// đã về đâu; callback có thể đã lọt mất (mạng, restart app), cổng mới là nguồn sự thật.
///
/// ⚠️ Giống MoMo ở chỗ đây KHÔNG nằm trong <see cref="Services.IPaymentGateway"/>: VNPay v2.1.0
/// không có API hỏi trạng thái, nhét vào interface chung là bắt nó cài một hàm ném lỗi.
///
/// ⚠️ <see cref="IsProcessing"/> là trạng thái THỨ BA mà MoMo không có: ZaloPay trả
/// <c>return_code = 3</c> nghĩa là đơn còn đang xử lý, chưa phải thành công mà cũng chưa phải hỏng.
/// Job đối soát phải để yên giao dịch ở lượt đó rồi quét lại — lật thành Failed là giết oan một đơn
/// khách còn đang trả tiền.
/// </summary>
public class ZaloPayQueryResult
{
    /// <summary>Cổng trả lời được và đơn ĐÃ thanh toán thành công (<c>return_code = 1</c>).</summary>
    public bool Success { get; set; }

    /// <summary>Đơn đang xử lý (<c>return_code = 3</c>) — chưa kết luận được, lượt sau hỏi lại.</summary>
    public bool IsProcessing { get; set; }

    /// <summary>Mã kết quả thô của cổng — <c>1</c> thành công · <c>2</c> thất bại · <c>3</c> đang xử lý.</summary>
    public int ReturnCode { get; set; }

    /// <summary>Thông báo của cổng (<c>return_message</c>).</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Số tiền cổng ghi nhận (VND) — đối chiếu với Amount của bảng Payments.</summary>
    public long Amount { get; set; }

    /// <summary>Mã giao dịch do ZaloPay sinh — ghi vào gatewayTransactionId của bảng Payments.</summary>
    public long ZpTransId { get; set; }
}
