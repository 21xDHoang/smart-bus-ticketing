namespace SmartBus.Api.Entities;

/// <summary>
/// Một giao dịch thanh toán của khách (US 6 — A9 #17: "mã giao dịch cổng + khoá chống trùng").
/// Bảng thuộc dòng 27 của bảng phân công (Vàng Thị Dăm); dựng theo uỷ quyền 10/10/2026 cho task
/// dòng 33 *"Xử lý idempotency: chống trừ tiền 2 lần khi callback trùng"* — Phùng Duy Hoàng.
///
/// Vai trò trong luồng thanh toán (docs/api-contract.md mục "Thanh toán"):
/// - <see cref="PaymentCode"/> sinh một lần lúc tạo giao dịch, là khoá chống trùng — unique index
///   ở AppDbContext.Payment.cs; gửi lên cổng làm orderId (MoMo) / vnp_TxnRef (VNPay).
/// - <see cref="Status"/> chỉ lật đúng MỘT lần Pending → Success/Failed, bởi callback (IPN) hoặc
///   job đối soát — khoá lật là concurrency token xmin (xem AppDbContext.Payment.cs).
/// - <see cref="TicketId"/> nối sang vé phát hành sau khi tiền về; A9 chốt cột này có từ đầu, FK
///   nối từ 10/10/2026 khi bảng <see cref="Ticket"/> được migrate (mục "Vé điện tử" của hợp đồng).
///
/// Không có <c>UpdatedAt</c>: A4 xếp bảng giao dịch vào nhóm chỉ ghi thêm, như AuditLogs.
/// </summary>
public class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Mã giao dịch nội bộ (dạng "PM-xxxxxxxx") — duy nhất, là khoá chống trùng của cả luồng.</summary>
    public string PaymentCode { get; set; } = string.Empty;

    /// <summary>Mã phương thức đã chọn — một trong các hằng của Dtos/Payments/PaymentProviderCodes.cs.</summary>
    public string MethodCode { get; set; } = string.Empty;

    /// <summary>Số tiền phải thu (VND) — numeric(12,2), khớp tổng giá ghế đã chọn lúc tạo.</summary>
    public decimal Amount { get; set; }

    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    /// <summary>
    /// Mã giao dịch phía cổng (MoMo transId / VNPay TransactionNo) — chỉ lượt lật thắng mới ghi
    /// được; dùng để đối soát với sao kê cổng.
    /// </summary>
    public string? GatewayTransactionId { get; set; }

    /// <summary>Thời điểm cổng ghi nhận thu tiền (MoMo responseTime / VNPay vnp_PayDate) — chỉ khi Success.</summary>
    public DateTime? PaidAt { get; set; }

    /// <summary>Thông điệp cổng trả về khi thất bại — hiển thị cho khách ở màn chờ.</summary>
    public string? Message { get; set; }

    /// <summary>Chủ giao dịch — điều kiện "chỉ chủ xem được" của GET /payments/{paymentCode}.</summary>
    public Guid UserId { get; set; }

    public User? User { get; set; }

    /// <summary>Chuyến được trả tiền — có từ lúc tạo (POST /payments bắt buộc tripId).</summary>
    public Guid TripId { get; set; }

    public Trip? Trip { get; set; }

    /// <summary>
    /// Các ghế đã chọn, ngăn bằng ';' (cùng lối SeatLayouts.VipSeatPositions) — CHỈ để lưu vết
    /// hiển thị; ghế thật sự thuộc về phiên giữ chỗ, không phải khoá nghiệp vụ.
    /// </summary>
    public string SeatNumbers { get; set; } = string.Empty;

    /// <summary>
    /// Vé phát hành khi giao dịch thành công — service phát hành vé ghi ngược vào đây để tra cứu.
    /// A9 (bảng chỉ mục) chốt cột này có index.
    ///
    /// Nullable vì giao dịch còn đang Pending (hoặc đã Failed) thì chưa có vé nào — và chính cột này
    /// là điều kiện của cờ <c>ShouldIssueTickets</c> mà <c>PaymentSettlementService</c> trả về
    /// (<c>Status == Success &amp;&amp; TicketId is null</c>).
    /// </summary>
    public Guid? TicketId { get; set; }

    /// <summary>
    /// FK đã nối 10/10/2026, ngay khi bảng <c>Tickets</c> được migrate — đúng như ghi chú để lại lúc
    /// dựng bảng ("nối khi bảng vé có"). Cấu hình ở AppDbContext.Payment.cs, Restrict đúng A5: xoá
    /// một vé không được kéo theo dòng tiền đã thu.
    /// </summary>
    public Ticket? Ticket { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
