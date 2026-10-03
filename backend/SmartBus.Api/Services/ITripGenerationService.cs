namespace SmartBus.Api.Services;

/// <summary>
/// Sinh chuyến cho những ngày sắp tới từ lịch trình đã có của mỗi tuyến (US 13).
///
/// Task *"BackgroundService sinh chuyến tự động từ lịch trình theo ngày"* — Nguyễn Duy Kiên.
/// Người gọi duy nhất trong app là <see cref="TripGenerationBackgroundService"/>; phần ruột được
/// tách ra đây để test gọi thẳng được, không phải chờ cái vòng lặp một giờ của hosted service —
/// cùng lối <see cref="IMonthlyPassExpiryService"/>.
///
/// <b>Vì sao là "nhân bản lịch trình đã có" chứ không phải "đọc bảng mẫu":</b> quy ước A8.3 chốt
/// không có bảng <c>Schedules</c>, nên tham số lịch trình (giờ bắt đầu, giờ kết thúc, tần suất)
/// chỉ sống trong một lời gọi <c>POST /routes/{routeId}/trips/generate</c> rồi biến mất — CSDL
/// không giữ chỗ nào cho job đọc. Nguồn sự thật còn lại là chính các dòng <c>Trips</c> đã sinh.
/// Xem <see cref="TripGenerationService"/> để biết chi tiết và hai giới hạn kèm theo.
/// </summary>
public interface ITripGenerationService
{
    /// <summary>
    /// Lấp chuyến cho những ngày CÒN TRỐNG trong tầm nhìn kể từ <paramref name="nowUtc"/>, lấy
    /// ngày gần nhất đã có chuyến của mỗi tuyến làm mẫu.
    ///
    /// <paramref name="nowUtc"/> là tham số chứ không đọc <c>DateTime.UtcNow</c> bên trong: job
    /// chạy theo lịch nên "bây giờ" là dữ liệu vào của nó, và test phải ghim được mốc đó mới thử
    /// được các ca biên (chuyến 05:00 giờ Việt Nam nằm ở ngày UTC hôm trước, ngày đã có chuyến,
    /// ngày bị huỷ hết) mà không phải chờ đồng hồ thật.
    ///
    /// Hàm này idempotent theo nghĩa "chạy lại không sinh trùng": ngày đã có bất kỳ dòng Trips nào
    /// (kể cả đã huỷ) đều bị bỏ qua. Chạy hai lượt liền nhau với cùng <paramref name="nowUtc"/>
    /// thì lượt thứ hai không tạo thêm gì.
    /// </summary>
    Task<TripGenerationResult> GenerateUpcomingAsync(
        DateTime nowUtc,
        CancellationToken cancellationToken = default);
}
