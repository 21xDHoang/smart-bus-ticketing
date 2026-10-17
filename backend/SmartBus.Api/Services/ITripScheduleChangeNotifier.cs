namespace SmartBus.Api.Services;

/// <summary>
/// Giờ chạy của một chuyến NGAY TRƯỚC khi quản lý sửa — ảnh chụp do lớp bọc
/// <c>NotifyingRouteTripsService</c> lấy trước khi chuyển lượt PUT xuống
/// <see cref="RouteTripsService.UpdateAsync"/>.
///
/// <para>
/// Chỉ giữ hai mốc mà nghiệp vụ trễ/đổi lịch trình nói tới. Không giữ cả entity <c>Trip</c> vì bản
/// ghi đó sẽ bị chính lượt PUT sửa (cùng <c>AppDbContext</c>, cùng change tracker) — ảnh chụp phải là
/// dữ liệu BẤT BIẾN mới so được với trạng thái sau khi lưu.
/// </para>
/// </summary>
public sealed record TripScheduleSnapshot(DateTime DepartureTime, DateTime? ArrivalTime);

/// <summary>
/// Báo cho hành khách đã mua vé của một chuyến khi quản lý dời giờ chạy — task Sprint 4
/// *"Service thông báo trễ chuyến / thay đổi lịch trình"* (dòng 44, Phùng Duy Hoàng), thuộc US 10.
///
/// <para>
/// Một lượt gọi làm hai việc, đúng hai vế của A8.5 (cùng lối <see cref="IRefundResultNotifier"/> và
/// <see cref="ITicketRequestResultNotifier"/>):
///   1. ghi MỘT dòng <c>Notifications</c> cho MỖI hành khách có vé <c>Paid</c> của chuyến — bản ghi
///      để khách đọc lại khi offline;
///   2. đẩy một sự kiện <c>NotificationReceived</c> qua <c>/hubs/notifications</c> cho phiên đang mở
///      của từng người.
/// </para>
///
/// <para>
/// 🔴 <b>KHÔNG BAO GIỜ NÉM LỖI</b> — cùng hợp đồng với hai notifier kia, và vì đúng cùng một lý do:
/// người gọi là lớp bọc đứng SAU một lượt PUT đã lưu thành công. Để lỗi kênh thông báo nổi lên là
/// quản lý thấy lượt sửa chuyến báo hỏng dù dữ liệu đã đổi thật, và có thể sửa lại lần nữa. Lỗi
/// ghi/đẩy được đẩy vào ILogger để còn lần ra.
/// </para>
///
/// <para>
/// 🔴 Cũng vì vậy hàm ở đây <b>KHÔNG nhận <c>CancellationToken</c></b>: token của lượt PUT (client
/// đóng kết nối, app dừng) không được phép làm dòng thông báo biến mất — chuyến đã dời giờ thì khách
/// phải biết.
/// </para>
///
/// <para>
/// ⚠️ <b>"Có gì để báo hay không" do CHÍNH bản cài đặt quyết định</b>, bằng cách so <c>previous</c>
/// với giờ ĐÃ LƯU — chỗ gọi không tự lọc trước. Lớp bọc chỉ truyền ảnh chụp cũ và id chuyến; mọi
/// phán quyết (chuyến đã huỷ/chạy xong chưa, giờ có thực sự đổi không, đổi theo hướng trễ hay đổi
/// lịch trình) nằm trong bản cài đặt. Chính sách đầy đủ ở doc-comment lớp đó và
/// docs/44-thong-bao-tre-chuyen-doi-lich-trinh.md.
/// </para>
/// </summary>
public interface ITripScheduleChangeNotifier
{
    /// <summary>
    /// Báo thay đổi lịch trình của MỘT chuyến. Gọi ngay sau khi
    /// <see cref="IRouteTripsService.UpdateAsync"/> trả về thành công — lớp bọc
    /// <c>NotifyingRouteTripsService</c> làm việc đó cho mọi lượt PUT sửa chuyến.
    /// </summary>
    /// <param name="tripId">Chuyến vừa được sửa.</param>
    /// <param name="previous">
    /// Giờ chạy trước lượt PUT. Null khi lớp bọc không chụp được (CSDL lỗi tạm thời) hoặc khi chuyến
    /// không tồn tại trước lượt sửa — cả hai nghĩa là không có mốc cũ để so, nên bản cài đặt bỏ qua
    /// thay vì đoán.
    /// </param>
    Task NotifyAsync(Guid tripId, TripScheduleSnapshot? previous);
}
