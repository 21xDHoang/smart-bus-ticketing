namespace SmartBus.Api.Services;

/// <summary>
/// Tính phí huỷ vé theo quy định thời gian — task *"Service tính phí huỷ theo quy định (trước 24h /
/// 12h / 2h)"* (US 5, dòng 6 Sprint 4 — Nguyễn Duy Kiên).
///
/// Quy định đang cài đặt (docs/32-csdl-huy-doi-ve.md §2.1):
///
/// <list type="table">
///   <item><term>còn ≥ 24 giờ</term><description>được huỷ, phí 0 (miễn phí)</description></item>
///   <item><term>còn ≥ 12 giờ</term><description>được huỷ, phí 30% giá vé</description></item>
///   <item><term>còn ≥ 2 giờ</term><description>được huỷ, phí 50% giá vé</description></item>
///   <item><term>còn &lt; 2 giờ</term><description>KHÔNG được huỷ (kể cả đã qua giờ chạy)</description></item>
/// </list>
///
/// Các mốc tính CẢ biên: đúng 24 giờ là miễn phí, đúng 12 giờ là 30%, đúng 2 giờ là 50%. Mốc 2 giờ
/// là <b>điều kiện được huỷ</b>, không phải một mức phí thứ ba.
///
/// <b>Dùng ở đâu:</b> Hiếu (dòng 3) gọi service này trong <c>POST /api/cancellation-requests</c> để
/// lấy cả điều kiện thời gian lẫn con số phí, rồi ghi <c>Fee</c> vào
/// <c>CancellationRequest.CancellationFee</c> (không null, <c>0</c> = miễn phí). Endpoint KHÔNG được
/// chép lại các ngưỡng — hai bản sao sẽ nói hai chuyện khác nhau sau lần sửa quy định đầu tiên.
///
/// 🔴 Phí phải <b>chụp lại</b> vào dòng yêu cầu tại lúc gửi (docs/32 §2): dời chuyến hoặc đổi quy
/// định sau đó không được làm con số khách đã nhìn thấy thay đổi.
///
/// Hàm THUẦN: không đọc CSDL, không đọc đồng hồ hệ thống. Người gọi truyền <c>now</c> — cùng lối
/// <see cref="IMonthlyPassExpiryService"/> — để test ghim được đúng các mốc biên mà không phải chờ
/// đồng hồ thật.
/// </summary>
public interface ICancellationFeeService
{
    /// <summary>
    /// Báo giá phí huỷ cho một vé tại thời điểm <paramref name="now"/>.
    /// </summary>
    /// <param name="ticketPrice">
    /// Giá vé GỐC đã chụp lúc phát hành (<c>Ticket.Price</c>) — không tra lại <c>Fares</c>. Không
    /// được âm.
    /// </param>
    /// <param name="departureAt">
    /// Giờ khởi hành của chuyến, UTC. Ở luồng huỷ, đây chính là giá trị sẽ chụp vào
    /// <c>CancellationRequest.DepartureAt</c>.
    /// </param>
    /// <param name="now">
    /// Thời điểm khách bấm gửi, UTC — thường là <c>DateTime.UtcNow</c> ở controller/service gọi.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ticketPrice"/> âm — dữ liệu hỏng, không phải một ca nghiệp vụ để trả lời.
    /// </exception>
    CancellationFeeQuote GetCancellationFee(decimal ticketPrice, DateTime departureAt, DateTime now);
}
