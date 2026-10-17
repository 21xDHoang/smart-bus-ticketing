namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="ICancellationFeeService"/> — xem interface để có bảng quy định đầy đủ.
///
/// Bốn con số dưới đây là TOÀN BỘ quy định huỷ vé của hệ thống (cửa sổ tự duyệt 10 phút + ba mốc
/// phí). Chúng cố ý nằm ở đúng một file: endpoint gửi yêu cầu (Hiếu, dòng 3) gọi service thay vì tự
/// so ngưỡng, nên đổi quy định chỉ phải sửa ở đây, docs/32 §2.1 và test — không có bản sao thứ hai
/// trong controller.
/// </summary>
public sealed class CancellationFeeService : ICancellationFeeService
{
    /// <summary>
    /// Vé tạo chưa quá mốc này thì được huỷ TỰ ĐỘNG, miễn phí, không cần quản lý duyệt. Cửa sổ này
    /// thắng mốc 2 giờ nhưng không thắng trạng thái "đã khởi hành".
    /// </summary>
    private static readonly TimeSpan AutoApproveWindow = TimeSpan.FromMinutes(10);

    /// <summary>Còn đúng hoặc hơn mốc này thì huỷ miễn phí.</summary>
    private static readonly TimeSpan FreeFrom = TimeSpan.FromHours(24);

    /// <summary>Còn đúng hoặc hơn mốc này (nhưng dưới <see cref="FreeFrom"/>) thì phí 30%.</summary>
    private static readonly TimeSpan ReducedFrom = TimeSpan.FromHours(12);

    /// <summary>Dưới mốc này thì không được huỷ — mốc 2 giờ là điều kiện, không phải mức phí.</summary>
    private static readonly TimeSpan MinimumNotice = TimeSpan.FromHours(2);

    private const decimal ThirtyPercent = 0.30m;
    private const decimal FiftyPercent = 0.50m;

    private const string DepartedMessage = "Chuyến xe đã khởi hành, không thể huỷ vé.";

    private const string TooLateMessage =
        "Chuyến khởi hành dưới 2 giờ nên không đủ điều kiện huỷ.";

    public CancellationFeeQuote GetCancellationFee(
        decimal ticketPrice,
        DateTime departureAt,
        DateTime now,
        DateTime ticketBookedAt)
    {
        // Giá âm là dữ liệu hỏng, không phải một ca nghiệp vụ có câu trả lời. Ném ra để nó lộ ở log
        // thay vì lặng lẽ tính ra một khoản phí âm rồi ghi vào CancellationFee.
        ArgumentOutOfRangeException.ThrowIfNegative(ticketPrice);

        // So bằng TimeSpan, KHÔNG đổi sang "số giờ nguyên": 11 giờ 59 phút 59 giây vẫn là dưới 12
        // giờ, còn làm tròn giờ sẽ biến nó thành 12 và thu thiếu 20% giá vé. Mock frontend dùng
        // diff(..., 'hour', true) nên hai bên khớp ở mọi mốc lẻ.
        var remaining = departureAt - now;

        if (remaining <= TimeSpan.Zero)
        {
            // Đúng mốc khởi hành cũng đã là "đã chạy" — cửa sổ huỷ đóng tại đó, không phải sau đó.
            // Kiểm TRƯỚC cửa sổ 10 phút: xe đã chạy thì vé vừa đặt xong cũng không còn gì để huỷ.
            return new CancellationFeeQuote(
                CanCancel: false, Fee: 0m, AutoApprove: false, RefusalReason: DepartedMessage);
        }

        // Cửa sổ 10 phút kể từ lúc TẠO VÉ: khách đổi ý ngay sau khi đặt thì được huỷ tự động, miễn
        // phí, không cần quản lý duyệt. Đặt TRƯỚC mốc 2 giờ vì cửa sổ này thắng quy định "sát giờ
        // chạy không được huỷ" (chốt 17/10/2026, docs/32 §2.1).
        // Mốc tạo vé ở tương lai (lệch đồng hồ giữa các máy) cũng rơi vào cửa sổ: coi như vừa tạo,
        // không chặn khách vì một con số đồng hồ không đáng tin.
        if (now - ticketBookedAt <= AutoApproveWindow)
        {
            return new CancellationFeeQuote(
                CanCancel: true, Fee: 0m, AutoApprove: true, RefusalReason: null);
        }

        if (remaining < MinimumNotice)
        {
            return new CancellationFeeQuote(
                CanCancel: false, Fee: 0m, AutoApprove: false, RefusalReason: TooLateMessage);
        }

        if (remaining >= FreeFrom)
        {
            return new CancellationFeeQuote(
                CanCancel: true, Fee: 0m, AutoApprove: false, RefusalReason: null);
        }

        var rate = remaining >= ReducedFrom ? ThirtyPercent : FiftyPercent;

        // Làm tròn tới ĐỒNG NGUYÊN, cùng lối VoucherValidationService: VND không dùng đơn vị lẻ
        // trong thực tế và mọi giá vé của hệ thống đều là số nguyên đồng. Cột vẫn là numeric(12,2)
        // theo A3 nên con số này được lưu với .00. AwayFromZero để 0,5 đồng không bị làm tròn xuống
        // — nhà xe không thu thiếu một đồng chỉ vì kiểu làm tròn mặc định (banker's rounding).
        var fee = decimal.Round(ticketPrice * rate, 0, MidpointRounding.AwayFromZero);

        return new CancellationFeeQuote(CanCancel: true, Fee: fee, AutoApprove: false, RefusalReason: null);
    }
}
