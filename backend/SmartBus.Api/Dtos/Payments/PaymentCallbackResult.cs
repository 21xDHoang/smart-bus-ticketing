namespace SmartBus.Api.Dtos.Payments;

/// <summary>
/// Kết quả ĐÃ CHUẨN HOÁ của một lượt cổng gọi về — endpoint xử lý giao dịch chỉ đọc DTO này,
/// không đụng tên trường riêng của cổng nào (task *"Adapter pattern thống nhất cổng thanh toán
/// (dễ thêm cổng mới)"* — Phùng Duy Hoàng).
///
/// Ranh giới trách nhiệm: adapter trả lời "chữ ký có hợp lệ không" và "cổng báo thành công hay
/// không"; endpoint vẫn phải tự đối chiếu <see cref="PaymentCode"/>/<see cref="Amount"/> với bản
/// ghi Payments trước khi lật trạng thái — tài liệu cả hai cổng đều yêu cầu bước đó và nó cần CSDL.
/// </summary>
public class PaymentCallbackResult
{
    /// <summary>
    /// Chữ ký của cổng hợp lệ với khoá bí mật của ta. False thì MỌI trường dưới đây chỉ để ghi
    /// log — tuyệt đối không dùng để lật trạng thái giao dịch (dữ liệu người ngoài gửi tới).
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Giao dịch THÀNH CÔNG theo cổng (MoMo resultCode = 0 / VNPay ResponseCode và
    /// TransactionStatus đều "00"). Luôn false khi <see cref="IsValid"/> là false — không bao giờ
    /// báo thành công trên dữ liệu chưa kiểm chữ ký.
    /// </summary>
    public bool Succeeded { get; set; }

    /// <summary>Mã giao dịch nội bộ ta gửi đi (MoMo orderId / VNPay vnp_TxnRef) — dùng tìm bản ghi Payments.</summary>
    public string PaymentCode { get; set; } = string.Empty;

    /// <summary>
    /// Số tiền khách đã trả, quy về VND (VNPay callback là ×100 — adapter chia lại). Kiểu decimal
    /// để phép chia không cắt lén phần lẻ: lệch 0,5 đồng cũng phải LỘ ra cho bước đối chiếu thấy,
    /// không được làm tròn thành khớp.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>Mã giao dịch phía cổng (MoMo transId / VNPay vnp_TransactionNo) — lưu vào gatewayTransactionId để đối soát.</summary>
    public string GatewayTransactionId { get; set; } = string.Empty;

    /// <summary>
    /// Thời điểm cổng ghi nhận thu tiền, quy về UTC (MoMo responseTime mili-giây epoch / VNPay
    /// vnp_PayDate GMT+7 — adapter tự đổi múi giờ). Null khi cổng không kèm hoặc không đọc được;
    /// bên gọi lấy giờ hệ thống thay thế. KHÔNG phải điều kiện để lật trạng thái.
    /// </summary>
    public DateTime? PaidAt { get; set; }

    /// <summary>
    /// Mã kết quả thô của cổng (MoMo resultCode / VNPay vnp_ResponseCode), giữ nguyên dạng chuỗi.
    /// VNPay không có trường thông báo — muốn biết vì sao hỏng thì tra mã này theo tài liệu cổng.
    /// </summary>
    public string ProviderResponseCode { get; set; } = string.Empty;

    /// <summary>Thông báo cổng gửi kèm (MoMo Message / VNPay vnp_Message khi cổng có gửi).</summary>
    public string Message { get; set; } = string.Empty;
}
