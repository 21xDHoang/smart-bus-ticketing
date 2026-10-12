namespace SmartBus.Api.Entities;

/// <summary>
/// Bảng <c>TicketChanges</c> — một YÊU CẦU ĐỔI CHUYẾN của hành khách (US 5 — "Hủy &amp; Đổi vé").
/// Task Sprint 4 *"Migrate bảng TicketChanges, CancellationRequests"* (Vàng Thị Dăm).
///
/// Luồng đầy đủ, theo bảng phân công Sprint 4: khách chọn chuyến mới và ghế mới ở màn đổi vé
/// (Dương Thị Hạnh + Hoàng Văn Thịnh) → gửi yêu cầu (<c>POST /ticket-changes</c>, kèm tính chênh lệch
/// giá — Nguyễn Duy Kiên) → quản lý duyệt hoặc từ chối (<c>PATCH /ticket-changes/{id}</c> — Phùng Duy
/// Hoàng) → job ghi log và bắn thông báo in-app (Vàng Thị Dăm, bảng phân công Sprint 4 dòng 7). Tiền chênh
/// lệch đi qua <c>Payments</c> (thu thêm) hoặc <c>Refunds</c> (hoàn lại) — hai bảng đã có, KHÔNG phải
/// bảng này.
///
/// ⚠️ Vì sao là bảng riêng chứ không phải vài cột trên <c>Tickets</c>: y hệt lý do đã ghi ở
/// <see cref="CancellationRequest"/> (một vé bị xin đổi nhiều lần; dấu vết kiểm toán không được ghi
/// đè). Hai bảng cố ý dùng CHUNG tên cột cho phần chung (vé, người gửi, lý do, trạng thái, người
/// duyệt…) để màn duyệt gộp của Admin đọc được cả hai bằng một khuôn.
///
/// 🔴 <b>Duyệt một yêu cầu đổi KHÔNG sửa vé cũ</b> — nó HUỶ vé cũ và phát hành một vé MỚI
/// (<see cref="NewTicketId"/>). Chốt 12/10/2026: sửa thẳng <c>Trips</c>/<c>Seats</c> trên dòng vé cũ
/// là viết lại lịch sử — doanh thu chuyến cũ mất dòng đó khỏi báo cáo (US 19), và hoá đơn đã xuất
/// theo <c>Payments.TicketId</c> không còn khớp. Lý do đầy đủ: <c>docs/32-csdl-huy-doi-ve.md</c> §2.
/// </summary>
public class TicketChange
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Vé khách muốn đổi (vé GỐC). Chỉ một yêu cầu đang chờ cho mỗi vé — chỉ mục duy nhất điều kiện
    /// ở AppDbContext.TicketChange.cs.
    /// </summary>
    public Guid TicketId { get; set; }

    public Ticket? Ticket { get; set; }

    /// <summary>
    /// Người gửi yêu cầu — cùng lối ghi sẵn như <see cref="CancellationRequest.UserId"/>: bình
    /// thường trùng <c>Ticket.UserId</c>, nhưng ghi thành cột riêng để màn "yêu cầu huỷ - đổi của tôi"
    /// lọc được và để sau này cho phép gửi hộ mà không phải migrate lại.
    /// </summary>
    public Guid UserId { get; set; }

    public User? User { get; set; }

    /// <summary>
    /// 🔴 Vé mới phát hành khi yêu cầu được DUYỆT. Nullable vì hai trạng thái còn lại đều chưa có vé
    /// mới: <see cref="TicketChangeStatus.Pending"/> thì chưa duyệt, <see cref="TicketChangeStatus.Rejected"/>
    /// thì không bao giờ có.
    ///
    /// Đây là cột trả lời câu hỏi của màn "theo dõi trạng thái yêu cầu" (Nguyễn Đình Băng): duyệt
    /// xong thì khách xem vé mới ở đâu. Không suy ra được từ chỗ nào khác — không có cột nào trên
    /// <c>Tickets</c> trỏ ngược về lượt đổi đã sinh ra nó.
    ///
    /// ⚠️ Tên LỆCH khuôn A2 (đáng lẽ <c>&lt;TênEntity&gt;Id</c> → <c>TicketId</c>), vì bảng có HAI
    /// khoá ngoại cùng trỏ <c>Tickets</c> (<see cref="TicketId"/> là vé gốc, cột này là vé mới). Ngoại
    /// lệ có chủ ý, cùng tình huống và cùng cách đặt tên với <c>ReviewedByUserId</c> của
    /// <see cref="DiscountRequest"/>.
    /// </summary>
    public Guid? NewTicketId { get; set; }

    public Ticket? NewTicket { get; set; }

    /// <summary>
    /// Chuyến khách muốn đổi SANG. Bắt buộc (không nullable): một yêu cầu đổi mà không nói đổi sang
    /// đâu thì không ai xử lý được, và để null sẽ đẻ ra nhánh "null nghĩa là giữ nguyên chuyến cũ"
    /// — thứ vô nghĩa với một yêu cầu ĐỔI chuyến.
    /// </summary>
    public Guid NewTripId { get; set; }

    public Trip? NewTrip { get; set; }

    /// <summary>
    /// Ghế khách muốn ngồi ở chuyến mới. Bắt buộc, cùng lý do như <see cref="NewTripId"/> — và còn
    /// một lý do nữa: ghế thuộc về XE của chuyến, nên đổi chuyến là đổi luôn sơ đồ ghế; không có
    /// chuyện "giữ nguyên số ghế cũ" một cách ngầm hiểu.
    ///
    /// ⚠️ Ghế này **không được giữ chỗ** lúc khách gửi yêu cầu — xem
    /// <see cref="TicketChangeStatus.Pending"/>. Người duyệt phải kiểm lại còn trống.
    /// </summary>
    public Guid NewSeatId { get; set; }

    public Seat? NewSeat { get; set; }

    /// <summary>
    /// Giá vé của chuyến MỚI, CHỤP LẠI lúc khách gửi yêu cầu — numeric(12,2) (A3).
    ///
    /// 🔴 Cố ý chụp lại chứ không tra lại <c>Fares</c> lúc đọc: bảng giá sửa được, nên tra lại sẽ ra
    /// con số khác với con số khách đã nhìn thấy và đã đồng ý khi bấm gửi. Cùng đúng lý do đã khiến
    /// <see cref="Ticket.Price"/> chụp lại giá.
    ///
    /// Chênh lệch giá KHÔNG có cột riêng: nó là <c>NewPrice - Ticket.Price</c>. Vế phải bất biến (giá
    /// vé chụp lúc phát hành), vế trái là cột này, nên phép trừ không bao giờ lệch — thêm cột thứ ba
    /// chỉ tạo thêm một chỗ để hai con số nói khác nhau. Quy ước DẤU cho API:
    /// <b>dương = khách trả thêm, âm = nhà xe hoàn lại</b>; bằng 0 = đổi ngang giá.
    /// </summary>
    public decimal NewPrice { get; set; }

    /// <summary>Lý do khách nêu — <c>text</c>, nullable, cùng lý do như <see cref="CancellationRequest.Reason"/>.</summary>
    public string? Reason { get; set; }

    /// <summary>Trạng thái xử lý — xem <see cref="TicketChangeStatus"/>. Dòng mới luôn Pending.</summary>
    public TicketChangeStatus Status { get; set; } = TicketChangeStatus.Pending;

    /// <summary>Quản lý đã duyệt/từ chối — nullable, cùng tên và cùng lý do đặt tên như <see cref="CancellationRequest.ReviewedByUserId"/>.</summary>
    public Guid? ReviewedByUserId { get; set; }

    public User? ReviewedBy { get; set; }

    /// <summary>Mốc quản lý bấm duyệt/từ chối, tách khỏi <see cref="UpdatedAt"/>.</summary>
    public DateTime? ReviewedAt { get; set; }

    /// <summary>Lý do từ chối, <c>text</c> — chỉ có nghĩa khi <see cref="Status"/> là <see cref="TicketChangeStatus.Rejected"/>.</summary>
    public string? RejectReason { get; set; }

    /// <summary>Thời điểm khách gửi yêu cầu.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Yêu cầu CÓ bị sửa (lật trạng thái, ghi <see cref="NewTicketId"/>) nên A4 buộc có <c>UpdatedAt</c>.</summary>
    public DateTime? UpdatedAt { get; set; }
}
