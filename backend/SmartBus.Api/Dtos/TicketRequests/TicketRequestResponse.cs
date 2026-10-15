namespace SmartBus.Api.Dtos.TicketRequests;

/// <summary>
/// Một dòng yêu cầu huỷ / đổi vé — hình dạng DÙNG CHUNG cho cả <c>CancellationRequests</c> và
/// <c>TicketChanges</c>, và cũng là response của cả bốn endpoint của US 5 (hai endpoint duyệt ở đây,
/// hai endpoint gửi yêu cầu của Hiếu dòng 3 / Kiên dòng 4 — docs/api-contract.md chốt một khuôn để
/// màn duyệt của Admin và màn theo dõi của khách đọc được cả hai loại).
///
/// Hai bảng có tên cột giống nhau ở phần chung (chủ ý của Dăm — docs/32-csdl-huy-doi-ve.md §3.4) nên
/// hình dạng response cũng là một. Trường nào của riêng một loại thì <c>null</c> ở loại kia, KHÔNG
/// lược bỏ khỏi JSON — giữ đúng lối <c>boardingStopName</c> của mục Vé điện tử.
/// </summary>
public class TicketRequestResponse
{
    public Guid Id { get; set; }

    /// <summary>
    /// <c>"Cancel"</c> / <c>"Change"</c> — SUY TỪ ENDPOINT, không có cột nào trong CSDL: nó là tên
    /// bảng, không phải dữ liệu. Lưu thành cột là tạo ra một trường có thể nói dối về chính dòng chứa nó.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Vé GỐC. FE mở thẳng <c>GET /tickets/{ticketId}</c> từ đây. Với <c>"Change"</c> đã duyệt thì vé này đã là <c>Cancelled</c>.</summary>
    public Guid TicketId { get; set; }

    /// <summary>Mã vé gốc (nội dung QR).</summary>
    public string TicketCode { get; set; } = string.Empty;

    /// <summary><c>"Pending"</c> / <c>"Approved"</c> / <c>"Rejected"</c> — tên chuỗi của enum, đúng như dữ liệu lưu.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Giá vé GỐC đã thanh toán (VND) — chụp lúc phát hành vé, không tra lại <c>Fares</c>.</summary>
    public decimal Price { get; set; }

    /// <summary>Lý do khách gửi kèm. Null khi khách không nhập.</summary>
    public string? Reason { get; set; }

    /// <summary>
    /// <b>Chỉ <c>"Cancel"</c></b> — phí huỷ, chụp lúc khách gửi; <c>0</c> = huỷ miễn phí. Có giá trị
    /// ngay cả khi còn <c>Pending</c> (khách nhìn thấy trước khi bấm gửi).
    /// </summary>
    public decimal? CancellationFee { get; set; }

    /// <summary>
    /// <b>Chỉ <c>"Cancel"</c> VÀ <c>status = "Approved"</c></b> — <c>price - cancellationFee</c>.
    ///
    /// Null ở mọi ca còn lại, kể cả <c>"Rejected"</c>: từ chối thì không khoản nào di chuyển, trả
    /// <c>price - fee</c> ở đó là nói dối. Đây là trường SUY RA chứ không phải cột — xem
    /// docs/api-contract.md, mục "Vì sao refundAmount là trường SUY RA".
    /// </summary>
    public decimal? RefundAmount { get; set; }

    /// <summary><b>Chỉ <c>"Change"</c></b> — chuyến khách xin đổi sang.</summary>
    public Guid? NewTripId { get; set; }

    /// <summary><b>Chỉ <c>"Change"</c></b> — ghế khách xin ngồi ở chuyến mới. Lúc gửi KHÔNG được giữ chỗ (xem <c>TicketChangeStatus.Pending</c>).</summary>
    public Guid? NewSeatId { get; set; }

    /// <summary><b>Chỉ <c>"Change"</c></b> — giá chuyến mới, chụp lúc khách gửi.</summary>
    public decimal? NewPrice { get; set; }

    /// <summary>
    /// <b>Chỉ <c>"Change"</c></b> — <c>newPrice - price</c>, CÓ DẤU: dương = khách trả thêm,
    /// âm = nhà xe hoàn lại, <c>0</c> = ngang giá. Không có cột trong CSDL — cả hai vế đều bất biến
    /// nên phép trừ không bao giờ lệch, thêm cột là thêm một chỗ để ba con số nói khác nhau.
    /// </summary>
    public decimal? PriceDifference { get; set; }

    /// <summary>
    /// <b>Chỉ <c>"Change"</c> ĐÃ DUYỆT</b> — vé MỚI phát hành ngay lúc duyệt; null khi
    /// <c>Pending</c>/<c>Rejected</c>. Mở vé mới bằng <c>GET /tickets/{newTicketId}</c>.
    /// </summary>
    public Guid? NewTicketId { get; set; }

    /// <summary>Quản lý đã xử lý. Null khi còn <c>Pending</c>.</summary>
    public Guid? ReviewedByUserId { get; set; }

    /// <summary>Mốc bấm duyệt / từ chối — tách khỏi <see cref="UpdatedAt"/> theo A2. Null khi còn <c>Pending</c>.</summary>
    public DateTime? ReviewedAt { get; set; }

    /// <summary>Lý do từ chối do quản lý nhập — chỉ có nghĩa khi <c>"Rejected"</c>.</summary>
    public string? RejectReason { get; set; }

    /// <summary>Thời điểm khách gửi.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Lần sửa cuối (A4). Null khi còn <c>Pending</c> — dòng chưa từng bị sửa.</summary>
    public DateTime? UpdatedAt { get; set; }
}
