namespace SmartBus.Api.Services;

/// <summary>
/// Quét các lượt giữ ghế đã quá hạn và nhả ghế (US 3 "Giữ chỗ tạm thời") — task *"Migrate bảng
/// SeatHoldLogs + job quét hold hết hạn"* (Vàng Thị Dăm).
///
/// Người gọi duy nhất trong app là <see cref="SeatHoldExpiryBackgroundService"/>; phần ruột tách ra
/// đây để test gọi thẳng, không phải chờ cái vòng lặp nền — cùng lối
/// <see cref="IMonthlyPassExpiryService"/>.
/// </summary>
public interface ISeatHoldExpiryService
{
    /// <summary>
    /// Lật mọi lượt giữ còn <see cref="Entities.SeatHoldStatus.Holding"/> mà
    /// <see cref="Entities.SeatHold.ExpiresAt"/> đã qua mốc <paramref name="now"/> sang Expired, và
    /// ghi một dòng <see cref="Entities.SeatHoldLog"/> cho mỗi lượt vừa lật.
    /// Trả về số LƯỢT GIỮ vừa nhả (không phải số ghế — một lượt giữ là một dòng SeatHolds).
    ///
    /// <paramref name="now"/> là tham số chứ không đọc <c>DateTime.UtcNow</c> bên trong: job chạy
    /// theo lịch nên "bây giờ" là dữ liệu vào của nó, và test phải ghim được mốc đó mới thử được ca
    /// biên (ExpiresAt đúng bằng now) mà không phải chờ đồng hồ thật — xem
    /// <c>SeatHoldExpiryServiceTests</c>.
    /// </summary>
    Task<int> ExpireDueHoldsAsync(DateTime now, CancellationToken cancellationToken = default);
}
