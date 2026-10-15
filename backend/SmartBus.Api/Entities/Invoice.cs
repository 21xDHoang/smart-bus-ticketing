namespace SmartBus.Api.Entities;

/// <summary>
/// Bảng <c>Invoices</c> — HOÁ ĐƠN ĐIỆN TỬ của một lượt thanh toán. Một dòng = một chứng từ đã phát
/// hành cho một <see cref="Payment"/>, kèm chỗ lưu file PDF và mốc đã gửi email.
///
/// Thuộc task *"Migrate bảng Invoices"* của bảng phân công **Sprint 4, dòng 27** (ưu tiên Cao, 3
/// điểm, Vàng Thị Dăm), phục vụ **US 7** *"Xuất hóa đơn"* — *"Là hành khách, tôi muốn nhận hóa đơn
/// điện tử qua email để lưu trữ chứng từ thanh toán."*
///
/// ⚠️ <b>Bảng này KHÔNG có trong A9</b> (20 bảng đóng băng) — #17 là <c>Payments</c>, và không có
/// dòng nào cho hoá đơn. Đây là <b>lần thứ sáu</b> việc *"A9 lệch thực tế"* được nhắc, cùng tình
/// trạng và cùng cách xử lý như <see cref="NotificationTemplate"/>: dựng theo phân công, rồi báo
/// nhóm bổ sung A9. Xem <c>docs/38-csdl-hoa-don.md</c> §1.
///
/// <b>Hoá đơn neo vào <see cref="Payment"/>, KHÔNG neo vào vé.</b> Chốt này không phải tự nghĩ ra —
/// nó đã được ghi trước trong repo: <c>docs/32-csdl-huy-doi-ve.md</c> §3.1 lấy chính hoá đơn làm lý
/// do để không sửa vé cũ khi đổi chuyến — *"<c>Payments.TicketId</c> đã trỏ vào vé đó và hoá đơn
/// (US 7) đã xuất theo nó"*. Và câu chữ của US 7 cũng nói rõ hoá đơn là *"chứng từ <b>thanh
/// toán</b>"*, không phải chứng từ đi lại. Vé nằm ở <c>Payments.TicketId</c> — theo một bước nữa là
/// tới, nên bảng này <b>cố ý không</b> có cột <c>TicketId</c> thứ hai.
///
/// ⚠️ <b>Hệ quả phải biết: hoá đơn là ảnh chụp, không phải bản sao sống của lượt thanh toán.</b>
/// Đổi chuyến làm phát hành vé MỚI (docs/32 §3.1) — hoá đơn cũ <b>vẫn trỏ vào lượt thanh toán cũ và
/// vẫn đúng</b>: nó mô tả *"khách đã trả bao nhiêu cho giao dịch nào"*, và điều đó không đổi. Đây
/// chính là lý do neo vào thanh toán thay vì vào vé: neo vào vé thì mỗi lần đổi chuyến lại có một
/// hoá đơn "không còn khớp thứ nó mô tả".
/// </summary>
public class Invoice
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Lượt thanh toán được xuất hoá đơn — FK → <c>Payments</c>, <c>RESTRICT</c>, <b>DUY NHẤT</b>.
    ///
    /// 🔴 <b>Một giao dịch chỉ có MỘT hoá đơn</b>, và ràng buộc đó phải nằm ở tầng CSDL — hai lý do
    /// đều có thật trong repo này:
    ///
    /// 1. <b>Cổng thanh toán gọi callback trùng.</b> Cả bảng <c>Payments</c> được dựng quanh đúng
    ///    giả định đó: <c>PaymentCode</c> có chỉ mục duy nhất để *"callback trùng tìm lại đúng dòng
    ///    đó để bỏ qua, không tạo dòng mới"*, và <c>xmin</c> để chặn hai luồng cùng lật một dòng.
    ///    Task dòng 29 (*"Gửi hóa đơn qua email tự động sau khi thanh toán"*) chạy đúng trên nhánh
    ///    thành công đó — nếu việc phát hành hoá đơn không có khoá, một callback gửi lại sẽ phát
    ///    hành <b>hoá đơn thứ hai</b> và khách nhận hai chứng từ cho một lần trả tiền.
    /// 2. <b>Màn Admin tra cứu theo mã giao dịch</b> (dòng 35) tra bằng <c>Payments.PaymentCode</c>.
    ///    Nếu một mã giao dịch trả về hai hoá đơn thì màn đó không có cách nào chọn đúng cái — cùng
    ///    lập luận đã dùng cho chỉ mục duy nhất <c>NotificationTemplates.Type</c>.
    ///
    /// Ba khoá ngoại của bảng đều <c>RESTRICT</c> (A5): xoá một lượt thanh toán mà cascade thì hoá
    /// đơn — chứng từ tài chính — biến mất theo, và không còn gì trong CSDL nói nó từng tồn tại.
    /// </summary>
    public Guid PaymentId { get; set; }

    public Payment? Payment { get; set; }

    /// <summary>
    /// Chủ hoá đơn — FK → <c>Users</c>, <c>RESTRICT</c>.
    ///
    /// <b>Cột này thừa về mặt logic</b> (suy ra được qua <c>Payment.UserId</c>) và <b>cố ý vẫn có</b>:
    /// nó là cột lọc của màn *"danh sách hóa đơn của tôi"* (dòng 32), nên có nó thì màn đó chạy trên
    /// một chỉ mục của chính bảng này thay vì phải nối sang <c>Payments</c>. Cùng lối đã dùng ở
    /// <c>Payments</c> — bảng đó cũng lưu <c>UserId</c>, <c>TripId</c>, <c>SeatNumbers</c> dù đều suy
    /// ra được từ vé. Dữ liệu không lệch được: một lượt thanh toán không bao giờ đổi chủ.
    /// </summary>
    public Guid UserId { get; set; }

    public User? User { get; set; }

    /// <summary>
    /// Số tiền trên hoá đơn — <c>numeric(12,2)</c> (A3), cùng độ rộng với <c>Payments.Amount</c> và
    /// <c>Fares.Price</c>.
    ///
    /// 🔴 <b>Đây là ẢNH CHỤP lúc phát hành, không phải bản sao sống của <c>Payments.Amount</c>.</b>
    /// Chép ra một cột là có chủ ý: hoá đơn là <b>chứng từ đã giao cho khách</b>, nên nó phải giữ
    /// đúng con số nó đã nói. Repo này đã lo đúng loại rủi ro đó ở chỗ khác —
    /// <c>docs/32</c> §3.1 từ chối sửa vé cũ vì *"tiền của chuyến cũ <b>đã thực thu</b>"* và
    /// *"báo cáo tháng đã chốt sẽ đổi số"*. Một cột suy ra tại chỗ đọc sẽ tự đổi theo nguồn; một
    /// chứng từ thì không được phép.
    ///
    /// Đây là số tiền khách <b>thực trả</b> (đã trừ voucher nếu có), không phải giá niêm yết trên
    /// bảng giá — chi tiết bảng kê (giá gốc, giảm giá, thuế) là việc của mẫu PDF ở dòng 28, không
    /// phải cột ở đây.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Đường dẫn file PDF đã sinh — <c>null</c> khi chưa sinh xong.
    ///
    /// Cột để kiểu <c>text</c> chứ không <c>varchar(n)</c>: nếu nơi lưu trữ trả về signed URL thì
    /// URL đó dài 300–800+ ký tự vì chứa cả token — cùng lý do đã ghi cho
    /// <see cref="DiscountRequest.EvidenceUrl"/>.
    ///
    /// ⚠️ <b>Nullable là chủ ý, không phải chỗ sót.</b> Hoá đơn được phát hành ngay ở nhánh thanh
    /// toán thành công (dòng 29 gửi mail *"sau khi thanh toán"*), còn việc vẽ PDF là task dòng 28
    /// <b>chưa ai viết</b>. Để NOT NULL thì hoặc là dòng 29 không chạy được, hoặc là phải chờ vẽ
    /// xong mới ghi dòng — mà chờ thì hỏng luôn tính idempotent ở <see cref="PaymentId"/>.
    /// </summary>
    public string? PdfUrl { get; set; }

    /// <summary>
    /// Thời điểm email hoá đơn được gửi đi — <c>null</c> nghĩa là <b>chưa gửi được</b>.
    ///
    /// Phục vụ ba task: dòng 29 (gửi tự động sau khi thanh toán), dòng 31 và dòng 34 (nút *"gửi
    /// lại"*). Cố ý <b>không</b> có cột trạng thái kiểu <c>Issued/Sent/Failed</c>: *"gửi lại"* chính
    /// là cơ chế thử lại, nên chỉ cần một mốc là đủ trả lời *"đã gửi chưa"*. Cùng lối đã chốt cho
    /// thông báo — kênh đẩy hỏng thì <b>không ném lỗi và dòng vẫn còn</b>, để còn đường gửi lại
    /// (docs/33 §11).
    ///
    /// ⚠️ Đánh đổi đã biết: *"chưa thử gửi"* và *"gửi rồi nhưng hỏng"* **trông giống nhau** ở đây
    /// (đều là <c>null</c>). Chấp nhận, vì phân biệt hai ca đó không mở ra hành động nào khác nhau —
    /// cả hai đều xử lý bằng cách bấm gửi lại. Ai cần phân biệt thật thì đó là việc của nhật ký
    /// (<c>AuditLogs</c>), không phải một cột ở bảng chứng từ.
    /// </summary>
    public DateTime? SentAt { get; set; }

    /// <summary>
    /// Thời điểm phát hành hoá đơn (UTC).
    ///
    /// <b>Cố ý không có thêm cột <c>IssuedAt</c>:</b> dòng này chỉ được tạo **lúc phát hành**, nên
    /// <c>CreatedAt</c> <i>chính là</i> mốc phát hành. Thêm một cột nữa là có hai cột nói cùng một
    /// thời điểm, và chúng sẽ lệch nhau ngay lần đầu ai đó ghi dòng rồi mới sửa <c>IssuedAt</c> —
    /// cùng lập luận đã dùng để từ chối cột <c>Code</c> ở <c>NotificationTemplates</c>.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// A4 buộc có: <see cref="PdfUrl"/> và <see cref="SentAt"/> được ghi <b>sau</b> khi dòng đã tồn
    /// tại, tức là dòng CÓ bị sửa. (Ngược lại, <c>Subscriptions</c> không có cột này vì bảng đó chỉ
    /// ghi thêm rồi xoá — hai kết luận khác nhau vì hai vòng đời khác nhau, không phải vì lệch tay.)
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
}
