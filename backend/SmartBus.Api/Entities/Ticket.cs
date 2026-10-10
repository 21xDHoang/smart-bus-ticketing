namespace SmartBus.Api.Entities;

/// <summary>
/// Bảng Tickets — một VÉ ĐIỆN TỬ đã phát hành cho một ghế của một chuyến (US 4 — A9 #14).
/// Task Sprint 3 *"Migrate bảng Tickets, TicketQRCodes"* (Vàng Thị Dăm).
///
/// Vé là mảnh cuối của chuỗi đặt chỗ: <c>SeatHolds</c> giữ ghế tạm → <c>Payments</c> thu tiền →
/// <b>Tickets</b> là chứng từ khách cầm. Vé chỉ sinh ra SAU khi tiền về, bởi một service nội bộ gọi
/// từ callback cổng (IPN) hoặc job đối soát — không phải endpoint (docs/api-contract.md, mục "Phát
/// hành vé sau khi thanh toán"). Vì vậy <see cref="Status"/> lúc sinh LUÔN là
/// <see cref="TicketStatus.Paid"/>, không có bước trung gian.
///
/// ⚠️ <b>Vì sao KHÔNG có bảng <c>TicketQRCodes</c></b> dù tên task ghi hai bảng: mã QR là một CỘT
/// của bảng này (<see cref="Code"/>), không phải bảng riêng. Ba nguồn đã chốt như vậy — A6 đặt khoá
/// duy nhất <c>QrCode</c> ngay trên <c>Tickets</c>, bảng trường của api-contract chỉ có <c>code</c>
/// nằm trên vé, và A9 (danh sách 20 bảng đóng băng) không có bảng nào tên <c>TicketQRCodes</c>.
/// Tách một bảng chỉ để chứa một cột vừa thêm một phép join cho mọi lượt soát vé, vừa là thêm bảng
/// ngoài A9 — việc mà ranh giới vai trò của chủ CSDL không cho tự làm. Xem
/// <c>docs/28-csdl-ve-dien-tu.md</c> §2.
///
/// Ghế gắn với vé qua <see cref="SeatId"/> + <see cref="TripId"/>, có chỉ mục duy nhất điều kiện
/// chống bán trùng (AppDbContext.Ticket.cs). <see cref="BoardingStopId"/> và
/// <see cref="AlightingStopId"/> chỉ để HIỂN THỊ — vé đặt theo cả chuyến (A8.1), trạm lên/xuống
/// không phải khoá nghiệp vụ.
/// </summary>
public class Ticket
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Mã vé = đúng nội dung mã QR in trên vé, và là thứ cổng soát vé đọc được.
    /// DUY NHẤT toàn hệ thống (A6) — không phải "duy nhất trong một chuyến": mã QR bị đoán ra rồi
    /// dùng cho vé khác là làm giả vé, nên khoá duy nhất phải ở phạm vi toàn bảng.
    ///
    /// Độ dài 200 ký tự là mức chừa cho mã QR CÓ KÝ SỐ của task *"Service sinh mã QR duy nhất + ký
    /// số chống làm giả"* (Nguyễn Duy Kiên): một payload ký HMAC cỡ
    /// <c>SBT1:{ticketId}.{chuỗiBase64}</c> rơi vào khoảng 90–130 ký tự, nên 200 còn dư mà vẫn chặn
    /// được chuỗi rác dài. Trong lúc service đó chưa có, bên phát hành tạm dùng <c>Guid</c>
    /// (36 ký tự) để luồng đứng vững — docs/api-contract.md bước 2 đã chốt cách tạm này.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Chuyến xe mà vé có hiệu lực — cũng là cột dựng nên phần "hết hạn" của vé (chuyến đã đi mà chưa soát).</summary>
    public Guid TripId { get; set; }

    public Trip? Trip { get; set; }

    /// <summary>
    /// Ghế khách đã trả tiền. Cặp <c>(TripId, SeatId)</c> là chỗ chống bán trùng ghế ở tầng CSDL —
    /// xem chỉ mục duy nhất điều kiện trong AppDbContext.Ticket.cs.
    /// </summary>
    public Guid SeatId { get; set; }

    public Seat? Seat { get; set; }

    /// <summary>Chủ vé — dùng cho "vé của tôi" (GET /tickets/me) và cho điều kiện chỉ chủ xem được vé mình.</summary>
    public Guid UserId { get; set; }

    public User? User { get; set; }

    /// <summary>
    /// Trạm khách lên xe. Nullable vì vé mua ở màn chọn ghế có thể không chọn trạm cụ thể (lên ở
    /// bến đầu). CHỈ để hiển thị — A8.1: vé đặt theo cả chuyến, cặp trạm này không phải khoá
    /// nghiệp vụ, nên hai vé cùng chuyến khác trạm vẫn hợp lệ và không ràng buộc gì nhau.
    /// </summary>
    public Guid? BoardingStopId { get; set; }

    public Stop? BoardingStop { get; set; }

    /// <summary>Trạm khách xuống xe — cùng tính chất hiển thị như <see cref="BoardingStopId"/>.</summary>
    public Guid? AlightingStopId { get; set; }

    public Stop? AlightingStop { get; set; }

    /// <summary>
    /// Giá đã thu, numeric(12,2) (A3 — không dùng float/double cho tiền).
    ///
    /// Cố ý CHỤP LẠI giá lúc phát hành chứ không tra <c>Fares</c> lúc đọc: bảng giá là thứ sửa
    /// được, nên tra lại sẽ khiến vé cũ hiển thị theo giá mới và hoá đơn đã xuất không còn khớp
    /// doanh thu. <c>Entities/MonthlyPass.cs</c> đã ghi trước dự định này cho vé lượt.
    /// </summary>
    public decimal Price { get; set; }

    public TicketStatus Status { get; set; } = TicketStatus.Paid;

    /// <summary>Thời điểm phát hành (tiền đã về) — FE hiển thị "ngày mua".</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Thời điểm soát vé — chỉ khác null khi <see cref="Status"/> = <see cref="TicketStatus.Used"/>.</summary>
    public DateTime? UsedAt { get; set; }

    /// <summary>
    /// Vé CÓ bị sửa sau khi sinh (Paid → Used / Cancelled) nên A4 buộc có <c>UpdatedAt</c> — khác
    /// <c>Payments</c> và <c>AuditLogs</c>, hai bảng chỉ ghi thêm.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
}
