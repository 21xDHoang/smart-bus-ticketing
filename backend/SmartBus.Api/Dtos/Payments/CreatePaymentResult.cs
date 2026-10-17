using SmartBus.Api.Entities;

namespace SmartBus.Api.Dtos.Payments;

/// <summary>
/// Kết quả của POST /api/payments — FE dùng <see cref="PayUrl"/> để chuyển khách sang trang cổng,
/// rồi poll GET /payments/{paymentCode} ở màn chờ (US 6, task *"Tạo giao dịch + gọi cổng"* — Trần
/// Trung Hiếu).
///
/// <see cref="Amount"/> là số tiền PHẢI TRẢ, đã trừ giảm giá — chính là con số gửi lên cổng và là
/// con số khách thấy ở màn chờ. Không dùng mã thì <see cref="OriginalAmount"/> == <see cref="Amount"/>
/// và <see cref="DiscountAmount"/> = 0, <see cref="VoucherCode"/> = null — hình dạng trả về không đổi.
/// </summary>
public class CreatePaymentResult
{
    /// <summary>Mã giao dịch nội bộ (dạng "PM-xxxxxxxx") — khoá chống trùng, và là khoá poll trạng thái.</summary>
    public string PaymentCode { get; set; } = string.Empty;

    /// <summary>Tổng tiền TRƯỚC giảm giá — bằng đúng total khách gửi lên (đã được server so khớp giá ghế).</summary>
    public decimal OriginalAmount { get; set; }

    /// <summary>Số tiền được giảm từ voucher; 0 khi không dùng mã.</summary>
    public decimal DiscountAmount { get; set; }

    /// <summary>Mã voucher đã dùng; null khi không dùng mã.</summary>
    public string? VoucherCode { get; set; }

    /// <summary>Số tiền phải trả — đã trừ giảm giá, là con số gửi lên cổng.</summary>
    public decimal Amount { get; set; }

    /// <summary>Mã phương thức thanh toán — chép lại từ request (đã chuẩn hoá theo PaymentProviderCodes).</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// Trạng thái lúc vừa tạo — luôn "Pending"; Success/Failed chỉ lật qua callback hoặc job đối soát.
    /// Khai <c>string</c> chứ không phải enum: JSON mặc định của ASP.NET Core in enum thành SỐ, còn
    /// hợp đồng và bản nháp FE chốt chuỗi — cùng lối Status của TicketResponse/RefundResponse.
    /// </summary>
    public string Status { get; set; } = PaymentStatus.Pending.ToString();

    /// <summary>URL chuyển khách sang trang trả tiền của cổng (MoMo payUrl / VNPay URL đã ký / ZaloPay order_url).</summary>
    public string PayUrl { get; set; } = string.Empty;
}
