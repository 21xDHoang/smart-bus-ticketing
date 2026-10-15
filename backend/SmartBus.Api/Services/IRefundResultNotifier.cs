using SmartBus.Api.Dtos.Refunds;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Báo cho khách kết quả CUỐI của một khoản hoàn tiền (US 8) — task Sprint 4 *"Thông báo kết quả hoàn
/// tiền qua email và trên web"* (dòng 34, Phùng Duy Hoàng), phục vụ màn "Hoàn tiền của tôi".
///
/// Một lượt gọi làm hai việc, theo đúng hai vế của A8.5 (cùng lối
/// <see cref="ITicketRequestResultNotifier"/>):
///   1. ghi MỘT dòng <see cref="Notification"/> — bản ghi để khách đọc lại khi offline;
///   2. đẩy một sự kiện qua <c>/hubs/notifications</c> cho phiên đang mở của khách.
///
/// <para>
/// 🔴 <b>KHÔNG BAO GIỜ NÉM LỖI</b> — cùng hợp đồng với <see cref="ITicketRequestResultNotifier"/>, và
/// vì đúng cùng một lý do: người gọi là lớp bọc <c>NotifyingRefundAttemptService</c>, đứng ở CUỐI một
/// lượt gọi cổng hoàn tiền đã thành công. Để lỗi kênh thông báo nổi lên là biến một khoản đã hoàn
/// thật thành lỗi ở mắt job thử lại — job tưởng khoản còn hỏng, còn tiền thì đã đi. Lỗi ghi/đẩy được
/// đẩy vào ILogger để còn lần ra.
/// </para>
///
/// <para>
/// 🔴 Cũng vì vậy hàm ở đây <b>KHÔNG nhận <c>CancellationToken</c></b>: token của lượt gọi cổng (job
/// tắt giữa chừng, app dừng) không được phép làm dòng thông báo biến mất — khoản đã Succeeded thì
/// khách phải biết, bất kể phiên nào đang mở hay job có bị dừng ngay sau đó.
/// </para>
///
/// <para>
/// ⚠️ "Có gì để báo hay không" do CHÍNH bản cài đặt quyết định, đọc từ dòng <c>Refunds</c> ĐÃ LƯU —
/// chỗ gọi truyền nguyên <see cref="RefundAttemptOutcome"/> của lượt vừa chạy, KHÔNG tự lọc trước.
/// Phần lớn lượt gọi kết thúc mà không sinh thông báo nào (thất bại còn lịch thử lại, cổng chưa trả
/// lời, lượt thua cuộc đua) — đó là chủ ý, không phải lỗi. Chính sách đầy đủ ở doc-comment bản cài
/// đặt và docs/41-thong-bao-ket-qua-hoan-tien.md.
/// </para>
/// </summary>
public interface IRefundResultNotifier
{
    /// <summary>
    /// Báo kết quả của MỘT lượt gọi cổng hoàn tiền. Gọi ngay sau
    /// <see cref="IRefundAttemptService.ExecuteAsync"/> với đúng kết quả nó trả về — lớp bọc
    /// <c>NotifyingRefundAttemptService</c> làm việc đó cho MỌI đường gọi cổng.
    /// </summary>
    Task NotifyAsync(RefundAttemptOutcome outcome);
}
