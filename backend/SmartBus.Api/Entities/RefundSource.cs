namespace SmartBus.Api.Entities;

/// <summary>
/// Nguồn gốc một khoản hoàn tiền trong bảng <c>Refunds</c> (US 8 — "Hoàn tiền tự động"). Lưu dạng
/// chuỗi trong CSDL (quy ước A3): "TicketCancellation" đọc là hiểu, còn 1 thì phải tra bảng mã.
///
/// Đúng HAI giá trị, và hai giá trị này chép nguyên văn từ chính câu User Story: *"tự động xử lý
/// hoàn tiền theo quy định khi <b>giao dịch thanh toán thất bại</b> hoặc <b>vé bị hủy hợp lệ</b>"*.
/// Không có giá trị thứ ba vì chưa story nào sinh ra khoản hoàn theo đường khác — thêm một giá trị
/// là đổi hợp đồng với frontend, không phải chi tiết nội bộ.
///
/// ⚠️ Tập giá trị đã bị frontend chốt cứng trước khi bảng này tồn tại:
/// <c>frontend/src/api/refundAdminApi.ts</c> (type <c>RefundSource</c>, màn Admin của Nguyễn Đình
/// Băng — dòng 46) và <c>myRefundApi.ts</c> (màn hành khách của Dương Thị Hạnh — dòng 44, nhánh
/// <c>feature/8-man-hinh-theo-doi-hoan-tien</c>) dùng đúng hai chuỗi này. Đổi tên ở đây là làm hỏng
/// hai màn đã merge.
/// </summary>
public enum RefundSource
{
    /// <summary>
    /// Giao dịch thanh toán <b>thất bại nhưng tiền đã bị cổng giữ</b> — hệ thống hoàn lại.
    ///
    /// ⚠️ Khoản hoàn loại này thường <b>KHÔNG có vé</b>: vé chỉ được phát hành sau khi tiền về
    /// (docs/api-contract.md, mục "Phát hành vé sau khi thanh toán"), nên giao dịch hỏng thì
    /// <see cref="Refund.TicketId"/> để null. Vì vậy <c>TicketId</c> nullable, và
    /// <see cref="Refund.CancellationRequestId"/> cũng null ở đây.
    /// </summary>
    PaymentFailure,

    /// <summary>
    /// Yêu cầu huỷ vé đã được quản lý <b>duyệt</b> — hoàn phần còn lại sau khi trừ phí huỷ, tức
    /// <c>Ticket.Price - CancellationRequest.CancellationFee</c>.
    ///
    /// Khoản hoàn loại này LUÔN có <see cref="Refund.TicketId"/> và
    /// <see cref="Refund.CancellationRequestId"/> — đó là bất biến ở tầng nghiệp vụ đi kèm giá trị
    /// này, xem <c>docs/40-csdl-hoan-tien.md</c> §2.3.
    ///
    /// ⚠️ Chỉ yêu cầu <b>đã duyệt</b> mới sinh khoản hoàn. Yêu cầu còn <c>Pending</c> thì vé vẫn
    /// <c>Paid</c> và chưa có đồng nào phải trả lại — xem doc-comment
    /// <see cref="CancellationRequestStatus.Pending"/>.
    /// </summary>
    TicketCancellation,
}
