using SmartBus.Api.Dtos.Vouchers;

namespace SmartBus.Api.Services;

/// <summary>
/// Phần "áp dụng" của dòng 52 *"API kiểm tra và áp dụng voucher vào đơn hàng"* (US 18, Nguyễn Duy
/// Kiên) — TIÊU THỤ một voucher cho một giao dịch đã thu được tiền.
///
/// Không phải endpoint: người gọi duy nhất là luồng thanh toán (nhánh thành công của callback cổng
/// và của job đối soát — xem mục "Thanh toán — /payments" của docs/api-contract.md). Luồng đó gọi
/// service này SAU khi đã chốt <c>Payment.Status = Success</c>, và **hỏng ở đây không được làm hỏng
/// callback** (ghi log rồi vẫn trả 204 cho cổng): tiền đã về, không có lý do gì để cổng gửi lại
/// mãi.
///
/// Vì sao tách khỏi <see cref="IVoucherValidationService"/> mà vẫn dùng chung bộ luật: kiểm tra là
/// câu hỏi vô hại gọi bao nhiêu lần cũng được, còn tiêu thụ là một lượt GHI có thể đua nhau. Hai
/// việc khác nhau về bản chất, nhưng luật voucher chỉ được có MỘT bản — service này gọi thẳng
/// <see cref="VoucherValidationService.ReasonFor"/> và
/// <see cref="VoucherValidationService.DiscountFor"/> chứ không chép lại.
/// </summary>
public interface IVoucherRedemptionService
{
    /// <summary>
    /// Tiêu thụ voucher của một giao dịch đã thành công: ghi một dòng <c>VoucherUsage</c> và tăng
    /// <c>Vouchers.UsedCount</c> lên 1, trong CÙNG một <c>SaveChangesAsync</c>.
    ///
    /// <b>Idempotent theo <c>PaymentCode</c></b> — chốt quan trọng nhất. Gọi lại cùng một mã giao
    /// dịch trả về bản ghi cũ với <see cref="VoucherRedemptionResult.AlreadyRedeemed"/> = <c>true</c>
    /// và **không** cộng thêm lượt. Nhờ vậy callback trùng của cổng (MoMo retry tới khi nhận 204) và
    /// job đối soát chạy chồng đều vô hại.
    ///
    /// <b>Kiểm lại từ đầu</b> trên <see cref="VoucherRedemptionRequest.OrderAmount"/> chứ không tin
    /// kết quả <c>validate</c> khách đã thấy lúc bấm thanh toán: giữa hai thời điểm mã có thể đã hết
    /// lượt hoặc hết hạn. Số tiền giảm ở đây tính bằng đúng hàm của lượt kiểm tra, nên nó BẰNG số đã
    /// trừ lúc tạo giao dịch.
    ///
    /// Mã voucher rỗng = giao dịch không dùng mã: trả <c>Ok</c> ngay, không ghi gì. Nhờ vậy luồng
    /// thanh toán gọi được VÔ ĐIỀU KIỆN mà không phải tự rẽ nhánh.
    ///
    /// Kết quả:
    /// <list type="bullet">
    /// <item><c>Ok</c> — đã tiêu thụ, hoặc là bản gửi lại, hoặc không có mã để tiêu thụ.</item>
    /// <item><c>NotFound</c> — <c>PaymentCode</c> trỏ tới chuyến/voucher không tồn tại.</item>
    /// <item><c>Conflict</c> — voucher không còn dùng được nữa, hoặc hai lượt tiêu thụ đua nhau.
    /// Câu <c>Error</c> là câu tiếng Việt đọc được, nhưng người gọi chủ yếu ghi log.</item>
    /// <item><c>Invalid</c> — <c>PaymentCode</c> rỗng. Đây là lỗi lập trình ở phía gọi, không phải
    /// ca nghiệp vụ: không có mã giao dịch thì không có chốt idempotency.</item>
    /// </list>
    /// </summary>
    Task<ServiceResult<VoucherRedemptionResult>> RedeemAsync(
        VoucherRedemptionRequest request, CancellationToken cancellationToken = default);
}
