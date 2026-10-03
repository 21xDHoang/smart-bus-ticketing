namespace SmartBus.Api.Services;

/// <summary>
/// Kết quả một lượt sinh chuyến tự động — đủ để job ghi log mà không phải đếm lại.
///
/// Tách thành bản ghi riêng thay vì trả <c>int</c> như <see cref="IMonthlyPassExpiryService"/>:
/// job này có ba con số cùng đáng nói, và hai trong đó là dấu hiệu hỏng cần nhìn thấy trong log
/// (tuyến không sinh được chuyến nào, chuyến mẫu bị bỏ vì xe đã rút khỏi đội).
/// </summary>
/// <param name="TripsCreated">Số chuyến vừa sinh thêm.</param>
/// <param name="RoutesFilled">Số tuyến thật sự được lấp ít nhất một ngày trống.</param>
/// <param name="SkippedInactiveBusTrips">
/// Số chuyến MẪU bị bỏ qua vì xe của chúng không còn ở trạng thái khai thác. Những chuyến này
/// không được nhân bản, nghĩa là ngày tương lai thiếu đúng chừng đó chuyến so với lịch trình —
/// người điều xe cần biết để thay xe.
/// </param>
public sealed record TripGenerationResult(
    int TripsCreated,
    int RoutesFilled,
    int SkippedInactiveBusTrips)
{
    /// <summary>Lượt chạy không có gì để làm — ca thường gặp nhất.</summary>
    public static readonly TripGenerationResult Empty = new(0, 0, 0);
}
