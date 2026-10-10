namespace SmartBus.Api.Dtos.Payments;

/// <summary>
/// Yêu cầu KHỞI TẠO thanh toán — đầu vào của <see cref="Services.IPaymentGateway.InitiateAsync"/>,
/// DTO TRUNG TÍNH đứng giữa endpoint POST /payments và các adapter cổng (task *"Adapter pattern
/// thống nhất cổng thanh toán (dễ thêm cổng mới)"* — Phùng Duy Hoàng).
///
/// Endpoint dựng DTO này từ request của FE rồi phó mặc từng adapter tự dịch sang hình dạng cổng
/// của mình (MoMo: JSON gửi cổng; VNPay: URL ký sẵn) — thêm cổng mới không phải sửa endpoint.
/// Không phải cổng nào cũng dùng hết trường: xem ghi chú từng trường.
/// </summary>
public class PaymentInitiationRequest
{
    /// <summary>
    /// Mã giao dịch nội bộ (paymentCode — khoá chống trùng của bảng Payments, quy ước A9). Adapter
    /// gửi cổng dưới tên của cổng: orderId (MoMo) / vnp_TxnRef (VNPay).
    /// </summary>
    public string PaymentCode { get; set; } = string.Empty;

    /// <summary>
    /// Số tiền VND (đồng), KHÔNG nhân sẵn 100 — chuyện ×100 là quy ước riêng của từng cổng (VNPay
    /// nhân khi ký; MoMo nhận đồng trần), adapter lo.
    /// </summary>
    public long Amount { get; set; }

    /// <summary>Nội dung đơn hàng cổng hiển thị cho khách — viết không dấu tiếng Việt.</summary>
    public string OrderInfo { get; set; } = string.Empty;

    /// <summary>URL khách quay về sau khi trả xong (màn chờ kết quả của FE) — cả hai cổng dùng.</summary>
    public string ReturnUrl { get; set; } = string.Empty;

    /// <summary>
    /// URL cổng gọi thẳng vào backend khi có kết quả (IPN). Chỉ MoMo dùng (bắt buộc với MoMo —
    /// tài liệu yêu cầu kiểm callback hai lớp); VNPay đăng ký IPN một lần trên portal nên adapter
    /// VNPay bỏ qua trường này.
    /// </summary>
    public string IpnUrl { get; set; } = string.Empty;

    /// <summary>
    /// IP khách gọi lên. Chỉ VNPay dùng (vnp_IpAddr bắt buộc — cổng ghi log chống gian lận); MoMo
    /// bỏ qua. Endpoint lấy từ HttpContext.Connection.RemoteIpAddress.
    /// </summary>
    public string IpAddress { get; set; } = string.Empty;
}
