using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Ghi nhận và đẩy thông báo KẾT QUẢ XỬ LÝ của một yêu cầu huỷ / đổi vé cho hành khách — task Sprint 4
/// *"Ghi log và thông báo kết quả xử lý cho hành khách"* (Vàng Thị Dăm, dòng 7), phục vụ US 5.
///
/// Một lượt gọi làm hai việc, theo đúng hai vế của A8.5:
///   1. ghi MỘT dòng <see cref="Notification"/> — bản ghi để khách đọc lại khi offline;
///   2. đẩy một sự kiện qua <c>/hubs/notifications</c> cho phiên đang mở của khách.
///
/// <para>
/// 🔴 <b>KHÔNG BAO GIỜ NÉM LỖI</b> — cùng hợp đồng với <see cref="IAuditLogService.RecordAsync"/>, và
/// vì đúng cùng một lý do: người gọi đang ở CUỐI một thao tác nghiệp vụ đã thành công (quản lý vừa
/// duyệt xong yêu cầu, trạng thái đã commit). Để lỗi kênh thông báo nổi lên là biến một lượt duyệt
/// thành công thành 500 — quản lý tưởng chưa duyệt được và bấm lại, còn khách thì mất cả kết quả lẫn
/// thông báo. Thông báo là thứ PHỤ TRỢ; lỗi ghi/đẩy được đẩy vào ILogger để còn lần ra.
/// </para>
///
/// <para>
/// 🔴 Cũng vì vậy hai hàm ở đây <b>KHÔNG nhận <c>CancellationToken</c></b>: tuyệt đối không được để
/// dòng thông báo biến mất chỉ vì khách đóng tab ngay sau khi quản lý bấm duyệt — mà lúc này
/// <c>RequestAborted</c> của lượt PATCH đã bị huỷ. Truyền token của request vào đây là tự tay tạo ra
/// đúng cái lỗi "duyệt xong mà khách không bao giờ biết".
/// </para>
///
/// <para>
/// ⚠️ Gọi SAU khi trạng thái yêu cầu đã được commit: hàm đọc <see cref="CancellationRequest.Status"/>
/// / <see cref="TicketChange.Status"/> để biết phải báo kết quả gì, nên gọi trước khi lưu là báo sai.
/// Không có ràng buộc thứ tự nào khác — hàm tự mở một lượt lưu riêng, KHÔNG tham gia giao dịch của
/// người gọi, và cũng không được dùng để bảo đảm tính nguyên tử cho nghiệp vụ.
/// </para>
/// </summary>
public interface ITicketRequestResultNotifier
{
    /// <summary>
    /// Báo cho khách kết quả xử lý yêu cầu HUỶ VÉ của họ. Gọi sau khi
    /// <see cref="CancellationRequest.Status"/> đã được lưu và khác <c>Pending</c>.
    ///
    /// Không làm gì (chỉ ghi log cảnh báo) nếu yêu cầu còn <c>Pending</c> — đó là lỗi ở chỗ gọi, không
    /// phải một kết quả để báo cho khách.
    /// </summary>
    Task NotifyCancellationReviewedAsync(CancellationRequest request);

    /// <summary>
    /// Báo cho khách kết quả xử lý yêu cầu ĐỔI CHUYẾN của họ. Gọi sau khi
    /// <see cref="TicketChange.Status"/> đã được lưu và khác <c>Pending</c>.
    ///
    /// Duyệt xong thì vé MỚI đã được phát hành — thông báo nói đúng điều đó, nhưng KHÔNG kèm số tiền
    /// chênh lệch: xem ghi chú ở bản cài đặt.
    /// </summary>
    Task NotifyTicketChangeReviewedAsync(TicketChange request);
}
