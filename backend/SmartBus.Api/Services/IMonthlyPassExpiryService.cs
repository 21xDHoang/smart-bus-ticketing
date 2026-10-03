namespace SmartBus.Api.Services;

/// <summary>
/// Quét vé tháng đã quá hạn và lật trạng thái LƯU sang Expired (US 16).
///
/// Task *"BackgroundService tự động chuyển vé tháng hết hạn sang trạng thái Expired"* —
/// Nguyễn Duy Kiên. Người gọi duy nhất trong app là
/// <see cref="MonthlyPassExpiryBackgroundService"/>; phần ruột được tách ra đây để test gọi thẳng
/// được, không phải chờ cái vòng lặp 15 phút của hosted service.
/// </summary>
public interface IMonthlyPassExpiryService
{
    /// <summary>
    /// Lật mọi vé còn <see cref="Entities.MonthlyPassStatus.Active"/> mà ValidTo đã qua mốc
    /// <paramref name="now"/> sang Expired. Trả về số vé vừa lật.
    ///
    /// <paramref name="now"/> là tham số chứ không đọc <c>DateTime.UtcNow</c> bên trong: job chạy
    /// theo lịch nên "bây giờ" là dữ liệu vào của nó, và test phải ghim được mốc đó mới thử được
    /// các ca biên (ValidTo đúng bằng now, vé đăng ký trước cho kỳ sau) mà không phải chờ đồng hồ
    /// thật — xem <c>MonthlyPassExpiryServiceTests</c>.
    /// </summary>
    Task<int> ExpireDuePassesAsync(DateTime now, CancellationToken cancellationToken = default);
}
