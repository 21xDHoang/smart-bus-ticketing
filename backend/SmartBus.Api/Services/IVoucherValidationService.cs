using SmartBus.Api.Dtos.Vouchers;

namespace SmartBus.Api.Services;

/// <summary>
/// Kiểm tra một mã voucher có dùng được cho một đơn hàng không, và nếu được thì giảm bao nhiêu
/// (US 18, task *"API kiểm tra và áp dụng voucher vào đơn hàng"* — Nguyễn Duy Kiên, dòng 52 + 53).
/// Hợp đồng đầy đủ ở mục "Voucher — /vouchers" của docs/api-contract.md.
///
/// Tách interface để endpoint HTTP và <see cref="IVoucherRedemptionService"/> (phần "áp dụng", chạy
/// lúc tiền về) dùng chung đúng MỘT bộ luật — chép lại luật sang nhánh tiêu thụ là hai bản sẽ lệch
/// nhau, và bản lệch sẽ là bản quyết định tiền.
/// </summary>
public interface IVoucherValidationService
{
    /// <summary>
    /// Kiểm tra mã và tính số tiền giảm — KHÔNG ghi gì xuống CSDL, gọi bao nhiêu lần cũng vô hại.
    ///
    /// Kết quả: luôn <c>Ok</c> cho một câu hỏi hợp lệ, kể cả khi mã KHÔNG dùng được — lúc đó
    /// <see cref="VoucherValidationResponse.Valid"/> là <c>false</c> kèm
    /// <see cref="VoucherValidationResponse.ReasonCode"/>. Đây là endpoint xem trước mà màn thanh
    /// toán gọi mỗi lần khách gõ mã, nên "mã không dùng được" là câu trả lời chứ không phải lỗi.
    ///
    /// <c>NotFound</c> chỉ dành cho <c>tripId</c> không tồn tại — đó là lỗi phía GỌI, khác hẳn
    /// "voucher bị từ chối".
    /// </summary>
    Task<ServiceResult<VoucherValidationResponse>> ValidateAsync(
        ValidateVoucherRequest request, CancellationToken cancellationToken = default);
}
