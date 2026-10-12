namespace SmartBus.Api.Entities;

/// <summary>
/// Vòng đời một YÊU CẦU HUỶ VÉ (US 5 — "Hủy &amp; Đổi vé"). Lưu dạng chuỗi trong CSDL (quy ước A3):
/// "Approved" đọc là hiểu, còn 1 thì phải tra bảng mã.
///
/// Ba giá trị, đúng luồng của ba task Sprint 4: khách gửi yêu cầu (*"API gửi yêu cầu huỷ vé + kiểm
/// tra điều kiện thời gian"* — Trần Trung Hiếu) sinh ra dòng ở <c>Pending</c>; quản lý xử lý
/// (*"API duyệt / từ chối yêu cầu huỷ - đổi vé"* — Phùng Duy Hoàng) lật sang <c>Approved</c> hoặc
/// <c>Rejected</c>. Màn "theo dõi trạng thái yêu cầu" của khách và màn duyệt của Admin chỉ đọc ba
/// giá trị này, nên tập giá trị phải khớp đúng chúng — thêm một giá trị là đổi hợp đồng với
/// frontend, không phải chi tiết nội bộ.
///
/// ⚠️ Vì sao KHÔNG có trạng thái "đã hoàn tiền": tiền chỉ rời hệ thống ở bảng <c>Refunds</c>
/// (US 8 — "Hoàn tiền tự động", một bảng riêng với vòng đời riêng: chờ → thành công → thất bại,
/// kèm retry theo lịch). Gộp nó vào đây sẽ buộc hai nghiệp vụ dùng chung một cột trạng thái, và khi
/// lượt hoàn tiền thất bại thì yêu cầu huỷ không còn chỗ nào để nói "đã cho huỷ nhưng chưa trả được
/// tiền". Bảng này trả lời *"có cho huỷ không"*; <c>Refunds</c> trả lời *"đã trả được tiền chưa"*.
/// Hai câu hỏi khác nhau, hai bảng khác nhau.
///
/// ⚠️ Cũng KHÔNG có trạng thái "khách tự rút yêu cầu": chưa task nào của Sprint 4 làm nút đó, và
/// thêm một giá trị enum là tự mở rộng tập giá trị đã chốt với frontend (cùng lối lập luận đã ghi ở
/// <see cref="TicketStatus"/> về giá trị <c>Held</c>).
/// </summary>
public enum CancellationRequestStatus
{
    /// <summary>
    /// Khách đã gửi, chờ quản lý xử lý. Đây là trạng thái dòng sinh ra ở
    /// <c>POST /cancellation-requests</c>, nên có giá trị mặc định.
    ///
    /// 🔴 Vé CHƯA bị huỷ ở trạng thái này — <see cref="Ticket.Status"/> vẫn là
    /// <see cref="TicketStatus.Paid"/> và ghế vẫn thuộc về khách. Vé chỉ lật sang
    /// <see cref="TicketStatus.Cancelled"/> lúc quản lý duyệt. Bảng này là một YÊU CẦU, không phải
    /// một hành động: khách gửi yêu cầu không làm ghế rời khỏi tay khách.
    /// </summary>
    Pending,

    /// <summary>
    /// Quản lý đã đồng ý cho huỷ. Vé gốc đã được lật sang <see cref="TicketStatus.Cancelled"/> và ghế
    /// đã trả về chợ — chỉ mục duy nhất điều kiện của A6 (<c>WHERE Status IN ('Held', 'Paid')</c>)
    /// không còn thấy vé này nên khách khác đặt lại được chính ghế đó.
    ///
    /// Tiền hoàn KHÔNG được ghi ở đây: số phải hoàn là <c>Ticket.Price - CancellationFee</c> (suy
    /// ra được, xem doc-comment <see cref="CancellationRequest.CancellationFee"/>), còn số ĐÃ hoàn
    /// nằm ở bảng <c>Refunds</c>. Duyệt xong mà lượt hoàn tiền còn đang chạy là chuyện bình thường.
    /// </summary>
    Approved,

    /// <summary>
    /// Quản lý từ chối. Vé giữ nguyên <see cref="TicketStatus.Paid"/>, ghế vẫn của khách, không có
    /// khoản tiền nào di chuyển. Lý do nằm ở <see cref="CancellationRequest.RejectReason"/>.
    ///
    /// Vé ở trạng thái này được gửi yêu cầu LẠI (lần hai, lý do khác) — đúng lý do bảng là một bảng
    /// riêng chứ không phải một cột trên <c>Tickets</c>. Chỉ mục duy nhất điều kiện
    /// <c>WHERE Status = 'Pending'</c> chỉ chặn yêu cầu ĐANG CHỜ, không chặn dòng đã từ chối.
    /// </summary>
    Rejected
}
