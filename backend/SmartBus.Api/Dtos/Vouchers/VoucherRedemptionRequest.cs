namespace SmartBus.Api.Dtos.Vouchers;

/// <summary>
/// Đầu vào của <c>IVoucherRedemptionService.RedeemAsync</c> — TIÊU THỤ một voucher cho một giao dịch
/// đã thu được tiền (US 18, phần "áp dụng" của dòng 52 — Nguyễn Duy Kiên).
///
/// ⚠️ Đây KHÔNG phải body của endpoint nào: không ai gọi thẳng từ HTTP. Người gọi duy nhất là luồng
/// thanh toán — nhánh thành công của callback cổng và của job đối soát — nên không có
/// DataAnnotations ở đây: dữ liệu đến từ bản ghi <c>Payment</c> trong CSDL, không từ người dùng.
/// Chỗ duy nhất cần chặn là <c>POST /payments</c>, và nó chặn trước khi tới đây.
/// </summary>
public class VoucherRedemptionRequest
{
    /// <summary>
    /// Mã giao dịch ĐÃ thành công — <c>Payments.PaymentCode</c>. Vừa là khoá ghi vào
    /// <c>VoucherUsage.PaymentCode</c>, vừa là **chốt idempotency**: gọi lại cùng mã này trả bản ghi
    /// cũ và KHÔNG cộng thêm lượt, nên callback trùng của cổng là vô hại.
    /// </summary>
    public string PaymentCode { get; set; } = string.Empty;

    /// <summary>Mã voucher khách đã xác nhận. Rỗng = giao dịch không dùng mã.</summary>
    public string VoucherCode { get; set; } = string.Empty;

    /// <summary>Khách đã trả tiền — ghi vào <c>VoucherUsage.UserId</c> làm dấu vết.</summary>
    public Guid UserId { get; set; }

    /// <summary>Chuyến của giao dịch — cần cho điều kiện tuyến của voucher.</summary>
    public Guid TripId { get; set; }

    /// <summary>
    /// Tổng tiền đơn TRƯỚC giảm, lấy từ <c>Payment.Amount</c> chứ không phải từ FE. Service KIỂM LẠI
    /// voucher từ đầu trên con số này — không tin kết quả <c>validate</c> mà khách đã thấy trước đó,
    /// vì giữa hai thời điểm mã có thể đã hết lượt hoặc hết hạn.
    /// </summary>
    public decimal OrderAmount { get; set; }
}
