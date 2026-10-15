namespace SmartBus.Api.Dtos.Refunds;

/// <summary>
/// Dữ liệu một lượt GỌI CỔNG hoàn tiền (US 8). Người dựng là service thực hiện lượt gọi — job thử
/// lại theo lịch (task dòng 30) và nút "Thử lại" của Admin — rồi đưa cho
/// <c>IRefundGatewayCaller</c>. Đây là hợp đồng giữa phần nghiệp vụ (đã xong) và phần nối cổng
/// thật (task dòng 29 — Hiếu, chưa dựng), nên nó chỉ chứa đúng những gì một cổng hoàn tiền cần.
/// </summary>
public class RefundGatewayCallRequest
{
    /// <summary>
    /// Khoá nội bộ của khoản hoàn — dùng luôn làm mã yêu cầu gửi cổng (idempotency key) để cổng
    /// khử trùng khi lượt gọi bị gửi lại sau timeout.
    /// </summary>
    public Guid RefundId { get; set; }

    /// <summary>Mã khoản hoàn dạng <c>RF-xxxxxxxx</c> — để người đối soát sao kê cổng tra ngược về hệ thống.</summary>
    public string RefundCode { get; set; } = string.Empty;

    /// <summary>
    /// Phương thức thanh toán của GIAO DỊCH GỐC — đọc từ <c>Payments.MethodCode</c>, KHÔNG bao giờ
    /// lấy từ tham số người dùng (docs/35 §6): hoàn tiền luôn phải quay về đúng kênh đã thu tiền.
    /// Điều này cũng có nghĩa Job thử lại và nút Admin không có tham số chọn kênh hoàn.
    /// </summary>
    public string MethodCode { get; set; } = string.Empty;

    /// <summary>Số tiền hoàn — đúng bằng số tiền giao dịch gốc (US 8 không có hoàn một phần).</summary>
    public decimal Amount { get; set; }
}
