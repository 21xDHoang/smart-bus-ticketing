namespace SmartBus.Api.Dtos.Payments;

/// <summary>
/// Đầu vào cho <see cref="Services.IVnPayGatewayService.CreatePaymentUrl"/> — một giao dịch thanh
/// toán VNPay sắp dựng URL chuyển khách sang cổng (US 6 "Cổng thanh toán", task *"Tích hợp VNPay:
/// tạo URL thanh toán + verify chữ ký"* — Nguyễn Duy Kiên).
///
/// Đây là DTO của TẦNG GATEWAY, chưa phải DTO của endpoint POST /payments: endpoint đó cần bảng
/// Payments (migration của Vàng Thị Dăm) mới lưu được giao dịch. Khi bảng có, service nghiệp vụ
/// thanh toán sẽ dịch từ request của endpoint sang DTO này — hình dạng chốt ở mục "Thanh toán —
/// /payments" của docs/api-contract.md.
/// </summary>
public class VnPayCreatePaymentRequest
{
    /// <summary>
    /// Mã đơn hàng gửi cho VNPay (vnp_TxnRef) — PHẢI là mã nội bộ duy nhất cho mỗi giao dịch (khoá
    /// chống trùng của bảng Payments, quy ước A9). Khi endpoint POST /payments có, đây là
    /// <c>paymentCode</c> của nó.
    /// </summary>
    public string TxnRef { get; set; } = string.Empty;

    /// <summary>Số tiền thanh toán (VND) — số nguyên; cổng nhận bản nhân 100 (vnp_Amount).</summary>
    public long Amount { get; set; }

    /// <summary>
    /// Mô tả đơn hàng hiển thị cho khách trên trang cổng — theo tài liệu VNPay phải là **chữ không
    /// dấu, không ký tự đặc biệt**, ví dụ "Thanh toan ve xe tuyen 01". Service KHÔNG tự bỏ dấu hộ —
    /// người gọi truyền đúng chuỗi hiển thị.
    /// </summary>
    public string OrderInfo { get; set; } = string.Empty;

    /// <summary>URL VNPay đưa khách quay về sau khi thanh toán xong (màn chờ kết quả của FE).</summary>
    public string ReturnUrl { get; set; } = string.Empty;

    /// <summary>IP của khách đang thanh toán — VNPay yêu cầu ghi lại (vnp_IpAddr); người gọi lấy từ HttpContext.</summary>
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>
    /// Mã ngân hàng để vào thẳng trang của ngân hàng đó (vnp_BankCode). Để trống thì khách chọn
    /// ngân hàng trên trang VNPay.
    /// </summary>
    public string? BankCode { get; set; }

    /// <summary>Ngôn ngữ trang cổng — "vn" hoặc "en"; để trống thì service dùng "vn".</summary>
    public string? Locale { get; set; }

    /// <summary>
    /// Loại đơn hàng (vnp_OrderType) — để trống thì service dùng "other". Danh mục mã loại đơn hàng
    /// nằm trong tài liệu VNPay.
    /// </summary>
    public string? OrderType { get; set; }

    /// <summary>
    /// Thời điểm tạo giao dịch (vnp_CreateDate, cổng ghi theo GMT+7) — để trống thì service lấy giờ
    /// hệ thống lúc dựng URL. Có mặt để người gọi ghim được mốc thời gian (test dùng để so với
    /// vector tính sẵn).
    /// </summary>
    public DateTimeOffset? CreateDate { get; set; }

    /// <summary>Hạn thanh toán (vnp_ExpireDate) — để trống thì cổng dùng hạn mặc định của mình.</summary>
    public DateTimeOffset? ExpireDate { get; set; }
}
