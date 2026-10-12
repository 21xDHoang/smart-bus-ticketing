namespace SmartBus.Api.Entities;

/// <summary>
/// Bảng <c>CancellationRequests</c> — một YÊU CẦU HUỶ VÉ của hành khách (US 5 — "Hủy &amp; Đổi vé").
/// Task Sprint 4 *"Migrate bảng TicketChanges, CancellationRequests"* (Vàng Thị Dăm).
///
/// Luồng đầy đủ, theo bảng phân công Sprint 4:
/// khách gửi (<c>POST /cancellation-requests</c> — Trần Trung Hiếu, kèm kiểm tra điều kiện thời gian)
/// → service tính phí huỷ theo quy định trước 24h/12h/2h (Nguyễn Duy Kiên) → quản lý duyệt hoặc từ
/// chối (<c>PATCH /cancellation-requests/{id}</c> — Phùng Duy Hoàng) → job ghi log và bắn thông báo
/// in-app cho khách (Vàng Thị Dăm, bảng phân công Sprint 4 dòng 7) → tiền hoàn do bảng <c>Refunds</c>
/// (US 8) lo, KHÔNG phải bảng này.
///
/// ⚠️ Vì sao là bảng RIÊNG chứ không phải vài cột trên <c>Tickets</c> (cùng lối lập luận đã dùng cho
/// <see cref="DiscountRequest"/> — A9 #13): một vé có thể bị xin huỷ nhiều lần theo thời gian (bị từ
/// chối rồi xin lại, hoặc khách đổi ý rồi đổi ý lần nữa), mà <c>Tickets</c> chỉ giữ được MỘT trạng
/// thái. Và câu hỏi "vì sao vé này bị huỷ, ai duyệt, phí bao nhiêu, lúc nào" là câu hỏi kiểm toán —
/// ghi đè lên vé là mất dấu vết. Vì vậy "vé này từng bị xin huỷ chưa" luôn phải suy ra từ bảng này,
/// không đọc từ <c>Tickets</c>.
///
/// Khác <see cref="TicketChange"/> ở đúng một điểm: huỷ thì ghế trả về chợ và không có chuyến mới,
/// nên bảng này không có khoá ngoại nào trỏ <c>Trips</c>/<c>Seats</c>. Phần chung của hai bảng
/// (vé, người gửi, lý do, trạng thái, người duyệt, lý do từ chối) cố ý đặt tên GIỐNG NHAU để màn
/// duyệt gộp của Admin và FE xử lý được cả hai bằng một khuôn.
/// </summary>
public class CancellationRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Vé khách xin huỷ. Chỉ một yêu cầu ĐANG CHỜ cho mỗi vé — ràng buộc ở tầng CSDL bằng chỉ mục
    /// duy nhất điều kiện (AppDbContext.CancellationRequest.cs), cùng lối A6 đang chống bán trùng ghế.
    /// </summary>
    public Guid TicketId { get; set; }

    public Ticket? Ticket { get; set; }

    /// <summary>
    /// Người gửi yêu cầu. Về luồng bình thường trùng với <c>Ticket.UserId</c> (chỉ chủ vé xin huỷ vé
    /// mình), nhưng vẫn ghi thành cột riêng: màn "yêu cầu huỷ - đổi của tôi" lọc theo cột này, và
    /// nếu sau này nhóm cho phép nhân viên gửi hộ khách thì bảng đã sẵn — thêm luật là thêm một điều
    /// kiện truy vấn, KHÔNG phải migrate lại. Cùng lối ghi sẵn đã dùng ở
    /// <see cref="VoucherUsage.UserId"/>.
    /// </summary>
    public Guid UserId { get; set; }

    public User? User { get; set; }

    /// <summary>
    /// Lý do khách nêu. Cột <c>text</c> chứ không <c>varchar(n)</c> — A3 xếp "mô tả dài" vào text.
    /// Nullable vì huỷ vé không bắt buộc phải kể lý do; bắt buộc thì hành khách gõ "abc" cho xong,
    /// còn dữ liệu nhận được chẳng tốt hơn.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Phí huỷ vé, numeric(12,2) — A3: không dùng float/double cho tiền. <c>0</c> = huỷ miễn phí
    /// (quy định "trước 24h"), nên KHÔNG dùng null để nói "chưa tính": dòng luôn có phí ngay lúc
    /// khách gửi, do service của Kiên tính (task *"Service tính phí huỷ theo quy định (trước 24h /
    /// 12h / 2h)"*).
    ///
    /// 🔴 Cố ý CHỤP LẠI con số chứ không tính lại lúc đọc — cùng lý do đã ghi cho
    /// <see cref="Ticket.Price"/>: phí là kết quả của một quy định CÓ THỂ SỬA, và của khoảng cách
    /// tới giờ chạy tại đúng thời điểm khách bấm gửi. Tính lại lúc duyệt (có thể vài giờ sau) sẽ ra
    /// con số khác với con số khách đã nhìn thấy trên màn hình, tức là thu tiền sai so với thứ khách
    /// đã đồng ý.
    ///
    /// Số tiền PHẢI HOÀN không có cột riêng: nó là <c>Ticket.Price - CancellationFee</c>. Cả hai vế
    /// đều đã bất biến (giá vé chụp lúc phát hành, phí chụp lúc gửi) nên phép trừ không bao giờ lệch;
    /// thêm một cột thứ ba chỉ tạo thêm một chỗ để ba con số nói khác nhau. Số tiền ĐÃ hoàn thật nằm
    /// ở bảng <c>Refunds</c> — xem <see cref="CancellationRequestStatus.Approved"/>.
    /// </summary>
    public decimal CancellationFee { get; set; }

    /// <summary>
    /// Giờ khởi hành của chuyến, CHỤP LẠI lúc khách gửi yêu cầu — không phải cột suy ra được.
    ///
    /// Vì sao cần: quy định phí huỷ (trước 24h / 12h / 2h) là một hàm của KHOẢNG CÁCH tới giờ chạy,
    /// mà giờ chạy thì **sửa được** — quản lý dời chuyến bằng <c>PUT /routes/{routeId}/trips/{id}</c>.
    /// Không chụp lại thì sau một lần dời chuyến, không còn cách nào giải thích vì sao hoá đơn ghi
    /// phí 30% trong khi "trước giờ chạy 20 tiếng" — câu hỏi kiểm toán đầu tiên mà nhà xe sẽ hỏi.
    /// Cùng đúng lý do đã khiến <see cref="Ticket.Price"/> chụp lại giá thay vì tra <c>Fares</c>.
    ///
    /// Cột <c>timestamptz</c> (A3) và NOT NULL: yêu cầu huỷ luôn gắn với một chuyến đã có giờ chạy —
    /// nhánh "chưa có giờ chạy" không tồn tại, vì chuyến sinh ra là đã có <c>DepartureTime</c>.
    /// </summary>
    public DateTime DepartureAt { get; set; }

    /// <summary>Trạng thái xử lý — xem <see cref="CancellationRequestStatus"/>. Dòng mới luôn Pending.</summary>
    public CancellationRequestStatus Status { get; set; } = CancellationRequestStatus.Pending;

    /// <summary>
    /// Quản lý đã duyệt/từ chối. Nullable vì dòng đang Pending chưa có người xử lý — để NOT NULL
    /// thì mọi yêu cầu mới đều không lưu được.
    ///
    /// ⚠️ Tên LỆCH khuôn A2 (<c>&lt;TênEntity&gt;Id</c>) vì bảng có HAI khoá ngoại cùng trỏ
    /// <c>Users</c> — buộc phải đặt tên phân biệt. Đây là ngoại lệ có chủ ý, và cố ý chọn ĐÚNG cái
    /// tên mà <see cref="DiscountRequest"/> đã dùng cho cùng tình huống, để hai màn duyệt của Admin
    /// đọc được bằng một khuôn.
    /// </summary>
    public Guid? ReviewedByUserId { get; set; }

    public User? ReviewedBy { get; set; }

    /// <summary>
    /// Mốc quản lý bấm duyệt/từ chối. Tách khỏi <see cref="UpdatedAt"/> vì đó là mốc nghiệp vụ (dùng
    /// để đối chiếu với hạn mức hoàn tiền), còn <c>UpdatedAt</c> chỉ là "dòng có bị sửa".
    /// </summary>
    public DateTime? ReviewedAt { get; set; }

    /// <summary>
    /// Lý do từ chối, do quản lý nhập. <c>text</c> (A3 — mô tả dài). Chỉ có nghĩa khi
    /// <see cref="Status"/> là <see cref="CancellationRequestStatus.Rejected"/>, và chính là nội dung
    /// mà job thông báo gửi cho khách.
    /// </summary>
    public string? RejectReason { get; set; }

    /// <summary>Thời điểm khách gửi yêu cầu — FE hiển thị ở màn theo dõi trạng thái.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Yêu cầu CÓ bị sửa (lật trạng thái khi duyệt/từ chối) nên A4 buộc có <c>UpdatedAt</c>.</summary>
    public DateTime? UpdatedAt { get; set; }
}
