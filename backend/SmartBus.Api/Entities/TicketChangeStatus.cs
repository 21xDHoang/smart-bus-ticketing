namespace SmartBus.Api.Entities;

/// <summary>
/// Vòng đời một YÊU CẦU ĐỔI CHUYẾN (US 5 — "Hủy &amp; Đổi vé"). Lưu dạng chuỗi trong CSDL (A3).
///
/// Ba giá trị, song song với <see cref="CancellationRequestStatus"/> và **cố ý trùng tên từng giá
/// trị** — màn duyệt của Admin là MỘT màn cho cả hai loại yêu cầu (*"Màn hình Admin duyệt yêu cầu
/// huỷ - đổi vé"* — Dương Thị Hạnh), nên FE phải vẽ được hai hàng đợi bằng cùng một tập nhãn. Đây là
/// hai enum riêng chứ không phải một enum dùng chung (đúng lối "một enum một entity" của repo:
/// <see cref="TicketStatus"/>, <see cref="PaymentStatus"/>, <see cref="DiscountRequestStatus"/>…)
/// để hai nghiệp vụ còn chỗ rẽ nhánh về sau mà không kéo nhau theo.
///
/// ⚠️ Vì sao KHÔNG có trạng thái "chờ thanh toán chênh lệch". Đổi sang chuyến đắt hơn thì khách phải
/// trả thêm, rẻ hơn thì được hoàn — nhưng tiền vẫn đi qua đúng hai bảng đã có: <c>Payments</c>
/// (US 6) và <c>Refunds</c> (US 8). Nhét trạng thái tiền vào đây sẽ dựng thêm một đường thu tiền thứ
/// ba, cạnh hai đường đã có luật chống trùng — đúng thứ A5/A6 sinh ra để chặn.
/// </summary>
public enum TicketChangeStatus
{
    /// <summary>
    /// Khách đã gửi, chờ quản lý xử lý. Dòng sinh ra ở <c>POST /ticket-changes</c>.
    ///
    /// 🔴 Ở trạng thái này vé gốc VẪN là <see cref="TicketStatus.Paid"/> (khách còn nguyên chuyến cũ)
    /// và ghế mới **CHƯA bị giữ** — không có dòng <c>SeatHolds</c> nào cho nó. Hệ quả có thật, phải
    /// xử lý ở tầng service: giữa lúc khách gửi và lúc quản lý duyệt, người khác hoàn toàn có thể
    /// mua mất ghế đó. <c>PATCH /ticket-changes/{id}</c> vì vậy phải **kiểm tra lại ghế còn trống
    /// ngay lúc duyệt** rồi mới lật trạng thái, chứ không tin dữ liệu đã kiểm lúc gửi.
    /// </summary>
    Pending,

    /// <summary>
    /// Quản lý đã đồng ý đổi. Vé gốc lật sang <see cref="TicketStatus.Cancelled"/> (ghế cũ trả về chợ)
    /// và một vé MỚI được phát hành cho chuyến mới — khoá ngoại ở
    /// <see cref="TicketChange.NewTicketId"/>.
    ///
    /// Vé cũ bị HUỶ chứ không bị sửa: sửa thẳng <c>Trips</c>/<c>Seats</c> trên dòng vé cũ là viết lại
    /// lịch sử — doanh thu của chuyến cũ mất dòng đó khỏi báo cáo (US 19) và hoá đơn đã xuất theo
    /// <c>Payments.TicketId</c> không còn khớp. Chốt 12/10/2026 cùng Dăm và ghi ở
    /// <c>docs/32-csdl-huy-doi-ve.md</c> §2.
    /// </summary>
    Approved,

    /// <summary>
    /// Quản lý từ chối. Vé gốc giữ nguyên <see cref="TicketStatus.Paid"/> và vẫn gắn chuyến cũ; không
    /// vé mới nào được phát hành, nên <see cref="TicketChange.NewTicketId"/> vẫn null. Lý do nằm ở
    /// <see cref="TicketChange.RejectReason"/>.
    ///
    /// Dòng đã từ chối KHÔNG chặn khách gửi yêu cầu mới cho cùng vé — chỉ mục duy nhất điều kiện chỉ
    /// chặn yêu cầu đang <c>Pending</c>.
    /// </summary>
    Rejected
}
