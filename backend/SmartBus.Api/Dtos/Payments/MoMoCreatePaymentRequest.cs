namespace SmartBus.Api.Dtos.Payments;

/// <summary>
/// Đầu vào cho <see cref="Services.IMoMoGatewayService.CreatePaymentAsync"/> — một giao dịch thanh
/// toán MoMo sắp gửi lên cổng (US 6 "Cổng thanh toán", task *"Tích hợp SDK MoMo: tạo giao dịch,
/// nhận callback"* — Trần Trung Hiếu).
///
/// Đây là DTO của TẦNG GATEWAY, chưa phải DTO của endpoint POST /payments: endpoint đó cần bảng
/// Payments (migration của Vàng Thị Dăm) mới lưu được giao dịch. Khi bảng có, service nghiệp vụ
/// thanh toán sẽ dịch từ request của endpoint sang DTO này — hình dạng chốt ở mục "Thanh toán —
/// /payments" của docs/api-contract.md.
/// </summary>
public class MoMoCreatePaymentRequest
{
    /// <summary>
    /// Mã đơn hàng gửi cho MoMo — PHẢI là mã nội bộ duy nhất cho mỗi giao dịch (khoá chống trùng
    /// của bảng Payments, quy ước A9). Khi endpoint POST /payments có, đây là paymentCode của nó.
    /// </summary>
    public string OrderId { get; set; } = string.Empty;

    /// <summary>Số tiền thanh toán (VND) — số nguyên, MoMo yêu cầu từ 10.000 đến 50.000.000.</summary>
    public long Amount { get; set; }

    /// <summary>Mô tả đơn hàng hiển thị cho khách trên app MoMo — ví dụ "Vé xe buýt tuyến 01".</summary>
    public string OrderInfo { get; set; } = string.Empty;

    /// <summary>URL MoMo đưa khách quay về sau khi thanh toán xong (màn chờ kết quả của FE).</summary>
    public string RedirectUrl { get; set; } = string.Empty;

    /// <summary>URL MoMo gọi callback (IPN) về backend khi có kết quả — endpoint công khai của chúng ta.</summary>
    public string IpnUrl { get; set; } = string.Empty;

    /// <summary>
    /// Mã định danh request — MoMo dùng để chống gửi lặp. Mỗi lần gọi tạo giao dịch phải là một
    /// GUID mới; để trống thì service tự sinh.
    /// </summary>
    public string? RequestId { get; set; }

    /// <summary>Dữ liệu riêng đi kèm giao dịch — MoMo yêu cầu base64 của JSON, rỗng là hợp lệ.</summary>
    public string ExtraData { get; set; } = string.Empty;
}
