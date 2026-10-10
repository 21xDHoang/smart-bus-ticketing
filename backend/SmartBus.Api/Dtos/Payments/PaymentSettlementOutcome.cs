using SmartBus.Api.Entities;

namespace SmartBus.Api.Dtos.Payments;

/// <summary>
/// Kết quả một lượt chốt thanh toán (task *"Xử lý idempotency: chống trừ tiền 2 lần khi callback
/// trùng"* — Phùng Duy Hoàng). Người gọi là endpoint callback hoặc job đối soát — cả hai chỉ cần
/// đổi kết quả này thành HTTP (204) và quyết định có phát hành vé hay không.
/// </summary>
public class PaymentSettlementOutcome
{
    public Guid PaymentId { get; set; }

    public string PaymentCode { get; set; } = string.Empty;

    /// <summary>Chủ giao dịch — bước phát hành vé cần để gắn vé vào đúng người.</summary>
    public Guid UserId { get; set; }

    /// <summary>Chuyến được trả tiền — bước phát hành vé cần để lấy ghế từ phiên giữ chỗ.</summary>
    public Guid TripId { get; set; }

    /// <summary>Trạng thái SAU lượt chốt này (đọc lại từ CSDL, kể cả khi lượt này là bản gửi lại).</summary>
    public PaymentStatus Status { get; set; }

    /// <summary>
    /// True khi CHÍNH lượt gọi này lật trạng thái Pending → Success/Failed. False là bản gửi lại
    /// (callback trùng, job đối soát chạy trùng, hoặc lượt thua trong nhánh đua) — tiền và vé đã
    /// xử lý ở lượt trước, người gọi vẫn trả 204 như nhau.
    /// </summary>
    public bool SettledNow { get; set; }

    /// <summary>
    /// Cờ cho bước phát hành vé (chưa dựng — chờ bảng Tickets, xem mục "Vé điện tử" của hợp đồng):
    /// giao dịch đã Success mà chưa gắn <c>TicketId</c>. True ở lượt chốt thắng, và GIỮ true ở các
    /// bản gửi lại khi lần phát hành trước đó thất bại giữa đường (vé chưa ghi vào Payment) — nhờ
    /// vậy callback MoMo gửi lại vẫn cứu được vé mà không đụng tới tiền.
    /// </summary>
    public bool ShouldIssueTickets { get; set; }
}
