namespace SmartBus.Api.Entities;

/// <summary>
/// Vòng đời một khoản hoàn tiền trong bảng <c>Refunds</c> (US 8 — "Hoàn tiền tự động"). Lưu dạng
/// chuỗi trong CSDL (quy ước A3).
///
/// Ba giá trị, đúng vòng đời mà US 8 mô tả: hệ thống tạo khoản hoàn (<c>Pending</c>) → gọi cổng
/// thanh toán (<c>Succeeded</c> hoặc <c>Failed</c>) → khoản thất bại được <b>thử lại theo lịch</b>
/// nên quay về <c>Pending</c>. Đây là lý do trạng thái này KHÔNG phải một chiều như
/// <see cref="PaymentStatus"/>: một khoản hoàn có thể đi <c>Failed → Pending → Failed</c> nhiều lượt
/// trước khi chốt.
///
/// ⚠️ Vì sao KHÔNG có trạng thái "đã huỷ khoản hoàn" / "bỏ thử lại": dòng hết lịch thử lại vẫn là
/// <c>Failed</c> và <see cref="Refund.NextRetryAt"/> để null — xem doc-comment cột đó. Thêm một giá
/// trị enum là đổi hợp đồng với frontend (cùng lối lập luận đã ghi ở <see cref="TicketStatus"/> về
/// giá trị <c>Held</c>).
///
/// ⚠️ Tập giá trị đã bị frontend chốt cứng trước khi bảng này tồn tại:
/// <c>frontend/src/api/refundAdminApi.ts</c> (<c>REFUND_STATUS_META</c> — nhãn "Chờ xử lý" / "Đã
/// hoàn" / "Thất bại" và màu thẻ) và <c>myRefundApi.ts</c> dùng đúng ba chuỗi này. Đổi tên ở đây là
/// làm hỏng hai màn đã merge.
///
/// ⚠️ Vì sao KHÔNG có trạng thái "chưa rõ kết quả" cho ca cổng không trả lời (timeout, mất kết
/// nối): ca đó <b>giữ nguyên <c>Pending</c></b> — hệ thống chưa biết tiền có đi hay không thì không
/// được nói là đã hoàn, mà cũng không được nói là thất bại (nói thất bại là mở đường cho job thử
/// lại một khoản cổng đã trả — tức hoàn hai lần). Bằng chứng của riêng lượt gọi đó nằm ở nhật ký
/// <see cref="RefundLog"/> với <see cref="RefundLogResult.Unresolved"/>, và việc tra cho ra nhẽ là
/// task *"Đối soát hoàn tiền hằng ngày + cảnh báo khi lệch số liệu"* (dòng 43).
/// </summary>
public enum RefundStatus
{
    /// <summary>
    /// Vừa tạo, chưa biết cổng trả tiền được hay không. Đây là trạng thái dòng sinh ra ở service
    /// hoàn tiền tự động (task dòng 39), nên có giá trị mặc định.
    ///
    /// Trạng thái này bao gồm CẢ ca "đã gọi cổng nhưng cổng chưa trả lời" — xem ⚠️ thứ ba ở
    /// doc-comment enum.
    /// </summary>
    Pending,

    /// <summary>
    /// Cổng xác nhận <b>đã trả tiền lại cho khách</b>. Mã giao dịch phía cổng nằm ở
    /// <see cref="Refund.GatewayTransactionId"/>, mốc thời gian ở <see cref="Refund.ProcessedAt"/>.
    ///
    /// Dòng đã ở trạng thái này thì KHÔNG nằm trong lịch thử lại nữa
    /// (<see cref="Refund.NextRetryAt"/> null) và không được gọi cổng thêm lần nào — gọi nữa là
    /// hoàn hai lần. Chốt ở tầng CSDL: chỉ mục duy nhất điều kiện
    /// <c>IX_Refunds_PaymentId_Active</c> ở AppDbContext.Refund.cs.
    /// </summary>
    Succeeded,

    /// <summary>
    /// Cổng từ chối hoặc báo lỗi — lý do nằm ở <see cref="Refund.FailReason"/>. Khoản ở trạng thái
    /// này là đối tượng của job thử lại theo lịch (task dòng 41) khi
    /// <see cref="Refund.NextRetryAt"/> đã tới hạn.
    ///
    /// ⚠️ <c>Failed</c> <b>không</b> có nghĩa "sẽ không bao giờ trả được": hết lịch thử lại thì dòng
    /// vẫn là <c>Failed</c> mà <see cref="Refund.NextRetryAt"/> null, và màn Admin (dòng 46) có nút
    /// "Thử lại" gọi tay được — chính vì vậy mới cần <c>NextRetryAt</c> tách khỏi <c>Status</c>.
    /// </summary>
    Failed,
}
