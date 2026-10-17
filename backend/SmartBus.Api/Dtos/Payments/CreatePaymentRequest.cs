using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Payments;

/// <summary>
/// Body của POST /api/payments — tạo một giao dịch thanh toán rồi gọi cổng (US 6 "Cổng thanh toán",
/// task *"Tạo giao dịch + gọi cổng"* — Trần Trung Hiếu).
///
/// Hợp đồng đầy đủ: docs/api-contract.md mục "Thanh toán — /payments". Điểm đáng chú ý nhất:
/// <see cref="Total"/> là tổng tiền TRƯỚC giảm giá, server tự tính lại số tiền giảm từ voucher và
/// KHÔNG tin con số nào FE gửi lên — FE gửi được gì thì người ngoài cũng gửi được nấy.
/// </summary>
public class CreatePaymentRequest
{
    /// <summary>
    /// Mã phương thức thanh toán — một trong <c>'MoMo' | 'VNPay' | 'ZaloPay' | 'BankCard'</c>.
    /// Mã ngoài bốn mã này → 400 "chưa mở" kèm danh sách cổng đã mở.
    /// </summary>
    [Required(ErrorMessage = "Phương thức thanh toán là bắt buộc")]
    [StringLength(20, MinimumLength = 2, ErrorMessage = "Phương thức thanh toán phải từ 2 đến 20 ký tự")]
    public string? MethodCode { get; set; }

    /// <summary>Chuyến được trả tiền — để <c>Guid?</c> để [Required] bắt được body thiếu trường.</summary>
    [Required(ErrorMessage = "Chuyến xe là bắt buộc")]
    public Guid? TripId { get; set; }

    /// <summary>
    /// Các số ghế đang thanh toán, ví dụ ["A1", "A2"] — dùng để lưu vết và phát hành vé, không phải
    /// khoá nghiệp vụ (ghế thật sự thuộc về phiên giữ chỗ).
    /// </summary>
    [Required(ErrorMessage = "Danh sách ghế là bắt buộc")]
    [MinLength(1, ErrorMessage = "Phải chọn ít nhất một ghế")]
    public List<string>? SeatNumbers { get; set; }

    /// <summary>
    /// Tổng tiền TRƯỚC giảm giá, khớp tổng giá các ghế đã chọn. Server tính lại giá từ Fares và
    /// so khớp — lệch là 400 theo trường total. Để <c>decimal?</c> để [Required] bắt được body
    /// thiếu trường (kiểu không nullable nhận mặc định 0 và lọt qua).
    /// </summary>
    [Required(ErrorMessage = "Tổng tiền là bắt buộc")]
    [Range(typeof(decimal), "0.01", "9999999999.99", ParseLimitsInInvariantCulture = true, ErrorMessage = "Tổng tiền không hợp lệ")]
    public decimal? Total { get; set; }

    /// <summary>
    /// Mã voucher khách đã bấm xác nhận ở màn thanh toán. Vắng/null = không dùng mã. Có mà không
    /// dùng được → 409 kèm errors.voucherCode — ngược hẳn POST /vouchers/validate (ở đó "mã không
    /// dùng được" là câu trả lời, ở đây là lỗi chặn).
    /// </summary>
    [StringLength(20, MinimumLength = 2, ErrorMessage = "Mã voucher phải từ 2 đến 20 ký tự")]
    public string? VoucherCode { get; set; }
}
