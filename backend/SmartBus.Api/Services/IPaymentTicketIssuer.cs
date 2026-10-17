namespace SmartBus.Api.Services;

/// <summary>
/// Phát hành vé điện tử khi giao dịch thanh toán về <c>Success</c> (US 4/6, task *"API phát hành vé
/// điện tử sau khi thanh toán thành công"* — Trần Trung Hiếu). Không phải endpoint: người gọi duy
/// nhất là callback cổng và job đối soát — hai nơi duy nhất biết "tiền đã về".
///
/// Một lần gọi làm đúng chuỗi của mục "Phát hành vé sau khi thanh toán" (docs/api-contract.md):
/// sinh <c>Ticket</c> status <c>Paid</c> với <c>Code</c> từ <see cref="ITicketQrService"/> → lật
/// <c>SeatHolds</c> của phiên sang <c>Confirmed</c> + ghi <c>SeatHoldLogs</c> <c>Confirmed</c> →
/// ghi <c>Payment.TicketId</c>.
///
/// <b>Idempotent theo <c>paymentCode</c></b> — chốt quan trọng nhất, hai lớp:
/// <list type="bullet">
/// <item><c>Payment.TicketId</c> đã có → lượt này là bản gửi lại, không làm gì;</item>
/// <item>lượt phát hành trước đứt giữa đường (vé đã ghi nhưng TicketId chưa kịp ghi) thì lượt thử
/// lại đâm chỉ mục unique <c>(TripId, SeatId)</c> của Tickets — bắt lỗi rồi ĐỌC LẠI vé đã có và vá
/// <c>TicketId</c>, không bao giờ sinh vé thứ hai cho cùng một ghế.</item>
/// </list>
/// Nhờ vậy mọi lỗi của lượt này được phép NÉM LÊN — người gọi ghi log rồi vẫn trả 204 cho cổng
/// (tiền đã về, không có lý do gì để cổng gửi lại mãi): callback trùng sẽ cứu lượt phát hành hỏng.
/// </summary>
public interface IPaymentTicketIssuer
{
    /// <summary>
    /// Phát hành vé cho giao dịch <paramref name="paymentCode"/> nếu nó đã <c>Success</c> và chưa
    /// gắn <c>TicketId</c>. Giao dịch chưa Success / đã phát hành → trả <c>false</c> (không phải
    /// lỗi). Dữ liệu hỏng (thiếu chuyến/ghế/phiên giữ chỗ lệch) → ném lên, người gọi ghi log.
    /// </summary>
    Task<bool> IssueAsync(string paymentCode, CancellationToken cancellationToken = default);
}
