namespace SmartBus.Api.Entities;

/// <summary>
/// Bảng <c>TicketValidations</c> — NHẬT KÝ SOÁT VÉ: một dòng cho MỖI lượt quét mã QR, kể cả lượt bị
/// từ chối. Task Sprint 4 *"Migrate bảng TicketValidations"* (Vàng Thị Dăm, dòng 14), thuộc US 15
/// *"Là phụ xe/tài xế, tôi muốn quét mã QR trên vé của khách bằng ứng dụng mobile để xác thực vé hợp
/// lệ"*.
///
/// Luồng đầy đủ theo bảng phân công Sprint 4: phụ xe quét QR (màn của Nguyễn Đình Băng, dòng 20) →
/// endpoint soát vé xác thực chữ ký rồi tra vé (Trần Trung Hiếu, dòng 15) → chặn soát trùng + đánh
/// dấu vé đã dùng (Nguyễn Duy Kiên, dòng 16) → ghi lượt lên xe theo trạm (Kiên, dòng 17) → hàng đợi
/// ngoại tuyến đồng bộ lại (Phùng Duy Hoàng, dòng 18) → màn thống kê lượt soát theo chuyến (Dương Thị
/// Hạnh, dòng 24). Bảng này là chỗ DUY NHẤT trả lời được ba câu cuối.
///
/// ⚠️ <b>Bảng này KHÔNG nằm trong A9</b> — danh sách 20 bảng đóng băng không có
/// <c>TicketValidations</c>. Vẫn dựng, vì bảng phân công Sprint 4 dòng 14 uỷ quyền trực tiếp. Đây là
/// lần thứ BA việc "A9 lệch thực tế" được nhắc (lần đầu <c>docs/28</c> §7.4, lần hai <c>docs/32</c>
/// §1) và nhóm cần bổ sung A9 — lý do đầy đủ ở <c>docs/34-csdl-soat-ve-qr.md</c> §1.
///
/// Bảng CHỈ GHI THÊM: không có <c>UpdatedAt</c> (A4 xếp nhóm này gồm <c>AuditLogs</c>,
/// <c>Payments</c>, <c>Notifications</c>) và dòng đã ghi không sửa. Cùng lối <see cref="Notification"/>.
/// Một lượt soát đã xảy ra thì không có gì để sửa — sửa được nghĩa là nhật ký kiểm toán vô giá trị.
/// </summary>
public class TicketValidation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Vé bị soát. <b>Nullable</b> — và đây là điểm khác biệt lớn nhất so với mọi bảng còn lại của dự
    /// án: lượt soát <see cref="TicketValidationResult.NotFound"/> KHÔNG có vé nào để trỏ vào (mã rác,
    /// hoặc mã có chữ ký sai — docs/30 §3.2 gộp cả hai vào một kết quả). Bắt NOT NULL sẽ buộc service
    /// phải BỎ luôn dòng nhật ký cho lượt không tra ra vé, tức là bỏ đúng loại lượt mà nhật ký sinh ra
    /// để ghi lại.
    ///
    /// Hệ quả kèm theo, phải đọc trước khi viết truy vấn: <c>TicketId IS NULL</c> ⟺
    /// <c>Result = 'NotFound'</c> (và chiều ngược lại). Đây là bất biến ở tầng nghiệp vụ, KHÔNG diễn
    /// đạt được bằng CHECK constraint vì <see cref="Result"/> lưu dạng chuỗi — xem
    /// <c>docs/34-csdl-soat-ve-qr.md</c> §4.3 (mục §4.4 của <c>docs/32</c>, số thứ tự khác vì
    /// <c>docs/32</c> có thêm tiểu mục <c>xmin</c> ở §4.3).
    /// </summary>
    public Guid? TicketId { get; set; }

    public Ticket? Ticket { get; set; }

    /// <summary>
    /// Chuyến xe mà phụ xe ĐANG CHẠY lúc quét — không phải chuyến ghi trên vé.
    ///
    /// 🔴 Phải là cột riêng chứ không suy ra từ <c>Ticket.TripId</c>: hai giá trị khác nhau chính là
    /// nội dung của <see cref="TicketValidationResult.WrongTrip"/>. Suy từ vé thì lượt soát nhầm
    /// chuyến luôn tự thấy mình đúng, và lý do <c>WrongTrip</c> không bao giờ xuất hiện.
    ///
    /// Nullable vì hợp đồng endpoint để <c>tripId</c> tuỳ chọn (docs/29 §3: *"Chuyến đang chạy của phụ
    /// xe"*), và vì lượt <c>NotFound</c> không có chuyến nào để đối chiếu. Không có giá trị này thì
    /// service không kết luận được <c>WrongTrip</c> — đúng như doc-comment của enum đã ghi.
    ///
    /// Cũng là cột gom nhóm của màn thống kê lượt soát theo chuyến (dòng 24).
    /// </summary>
    public Guid? TripId { get; set; }

    public Trip? Trip { get; set; }

    /// <summary>
    /// Tài xế/phụ xe bấm quét — task dòng 19 *"Phân quyền: chỉ tài xế/phụ xe được soát vé"* (Trần
    /// Trung Hiếu) là chỗ kiểm quyền; cột này ghi lại NGƯỜI ĐÃ QUÉT, để tra được *"ai soát lượt này"*
    /// và *"tài khoản này soát bao nhiêu lượt"*. Bắt buộc: một lượt soát không có người thực hiện thì
    /// không truy được trách nhiệm, mà đây là bảng kiểm toán.
    ///
    /// ⚠️ Tên LỆCH khuôn A2 (đáng lẽ <c>UserId</c>). Hai lý do:
    /// (1) Trên bảng <c>Tickets</c> — bảng mà lượt soát này trỏ tới — <c>UserId</c> là HÀNH KHÁCH chủ
    /// vé; ở đây lại là NGƯỜI SOÁT. Hai vai trò ngược nhau mang cùng một tên cột là đúng loại nhầm lẫn
    /// mà doc-comment của <see cref="Notification.UserId"/> đã cảnh báo, và ở đây hai bảng còn nằm sát
    /// nhau trong cùng một tính năng nên hậu quả là thật.
    /// (2) Đây là tiền lệ đã có: <c>DiscountRequest.ReviewedByUserId</c> và
    /// <c>TicketChange.ReviewedByUserId</c> đặt tên theo VAI TRÒ khi cột không còn là "chủ sở hữu" của
    /// bản ghi.
    /// </summary>
    public Guid ValidatedByUserId { get; set; }

    public User? ValidatedBy { get; set; }

    /// <summary>
    /// Trạm khách thực sự LÊN XE — task dòng 17 *"API ghi nhận lượt lên xe theo trạm"* (Nguyễn Duy
    /// Kiên).
    ///
    /// 🔴 Khác <c>Ticket.BoardingStopId</c>: cột bên vé là trạm khách CHỌN LÚC MUA (thông tin dự kiến,
    /// A8.1 ghi rõ nó *"là thông tin, không phải khoá nghiệp vụ"*), còn cột này là trạm phụ xe GHI
    /// NHẬN LÚC KHÁCH BƯỚC LÊN. Hai giá trị lệch nhau là chuyện bình thường và chính là thứ cần đối
    /// chiếu (US 10 *"biết khách xuống trạm nào"*, US 20 *"mật độ theo trạm"*).
    ///
    /// Nullable: lượt bị từ chối thì không có ai lên xe, và phụ xe có thể soát mà không ghi trạm.
    /// </summary>
    public Guid? BoardingStopId { get; set; }

    public Stop? BoardingStop { get; set; }

    /// <summary>
    /// NGUYÊN VĂN chuỗi mã QR máy quét đọc được, <c>varchar(200)</c> — cùng độ dài với
    /// <see cref="Ticket.Code"/> vì đây đúng là chuỗi đó.
    ///
    /// 🔴 Cố ý CHỤP LẠI chứ không chỉ trỏ <see cref="TicketId"/>: (1) lượt <c>NotFound</c> không có vé
    /// để trỏ vào, nên không có cột này thì lượt đó không còn dấu vết nào ngoài một chữ "NotFound";
    /// (2) docs/30 §3.2 chốt *"log nguyên văn mã để điều tra sau"* — mã QR có nonce, nên hai lượt quét
    /// cùng một vé mang hai chuỗi khác nhau, và chuỗi của kẻ làm giả chỉ nằm ở đây.
    ///
    /// ⚠️ Không dùng cột này để TRA VÉ — đường tra là <c>Tickets.Code</c> (có chỉ mục UNIQUE, A6) sau
    /// khi chữ ký đã được xác thực. Cột này là bản ghi, không phải khoá tra cứu.
    /// </summary>
    public string ScannedCode { get; set; } = string.Empty;

    /// <summary>
    /// Kết quả lượt soát — xem <see cref="TicketValidationResult"/>. Quyết định luôn cả nhãn hiển thị
    /// ở màn quét (xanh/đỏ, dòng 21).
    ///
    /// ⚠️ Cố ý KHÔNG khởi tạo giá trị mặc định, dù <see cref="Ticket.Status"/> và
    /// <see cref="TicketChange.Status"/> đều có. Ở hai bảng kia giá trị khởi tạo là trạng thái đầu
    /// tiên CÓ NGHĨA của vòng đời (<c>Paid</c>, <c>Pending</c>); ở đây mọi lượt soát đều đã có kết quả
    /// ngay lúc ghi, nên một giá trị mặc định chỉ che mất chỗ service quên gán. Xem ⚠️ thứ hai ở
    /// doc-comment của enum: giá trị 0 là <c>NotFound</c>, nên lượt ghi thiếu sẽ lộ ra chứ không im
    /// lặng thành "hợp lệ".
    /// </summary>
    public TicketValidationResult Result { get; set; }

    /// <summary>
    /// Thời điểm MÁY QUÉT ghi nhận lượt soát — task dòng 18 *"Xử lý ngoại tuyến: lưu tạm lượt soát khi
    /// mất mạng, đồng bộ lại sau"* (Phùng Duy Hoàng).
    ///
    /// 🔴 Khác <see cref="CreatedAt"/> (giờ dòng dữ liệu tới được server). Máy mất mạng thì hàng đợi
    /// ngoại tuyến có thể đồng bộ lại SAU HÀNG GIỜ, thậm chí hôm sau; lấy giờ đồng bộ làm giờ soát sẽ
    /// dồn cả một buổi chạy của phụ xe vào một mốc duy nhất, và màn thống kê theo chuyến (dòng 24) đọc
    /// ra một biểu đồ sai hẳn. Giờ nghiệp vụ là giờ quét, nên phải là cột riêng.
    ///
    /// ⚠️ Đây là mốc thời gian do THIẾT BỊ khai, không phải do server — máy phụ xe sai giờ thì cột này
    /// sai. Vì vậy khuyến nghị cho dòng 18: service nên kiểm <c>ScannedAt</c> nằm trong khoảng hợp lý
    /// (không ở tương lai, không quá xa <see cref="CreatedAt"/>) trước khi nhận, chứ đừng so sánh
    /// <c>ScannedAt</c> giữa các thiết bị khác nhau — xem <c>docs/34-csdl-soat-ve-qr.md</c> §4.2.
    /// </summary>
    public DateTime ScannedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Mã lượt soát do CHÍNH MÁY QUÉT sinh, dùng làm khoá CHỐNG TRÙNG khi hàng đợi ngoại tuyến đồng bộ
    /// lại (dòng 18). Có chỉ mục UNIQUE, nullable.
    ///
    /// 🔴 Vì sao cần: hàng đợi ngoại tuyến gửi lại là chuyện bình thường — mất mạng giữa chừng, app bị
    /// kill, người dùng bấm "đồng bộ" hai lần. Không có khoá chống trùng thì mỗi lượt gửi lại đẻ thêm
    /// một dòng nhật ký, và màn thống kê (dòng 24) đếm khống. Tệ hơn: một lượt soát gửi lại có thể
    /// trông như một lượt soát TRÙNG, tức là tự sinh ra bằng chứng gian lận cho một phụ xe vô can.
    ///
    /// Nullable vì lượt soát ONLINE không cần khoá này — máy có mạng gửi thẳng một lần, và bắt mọi
    /// client phải sinh mã sẽ là bắt cả những client không hề ngoại tuyến làm thêm một việc vô ích.
    /// PostgreSQL cho phép nhiều NULL trên chỉ mục UNIQUE, nên các lượt online không chặn nhau.
    /// </summary>
    public Guid? ClientScanId { get; set; }

    /// <summary>
    /// Thời điểm dòng nhật ký tới được server (UTC) — cột audit bắt buộc ở MỌI bảng (A4). Khác
    /// <see cref="ScannedAt"/>: xem doc-comment ở đó.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
