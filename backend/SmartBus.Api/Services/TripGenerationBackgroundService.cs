namespace SmartBus.Api.Services;

/// <summary>
/// Job nền sinh chuyến cho những ngày sắp tới — task *"BackgroundService sinh chuyến tự động từ
/// lịch trình theo ngày"* (US 13, Nguyễn Duy Kiên).
///
/// Cùng khuôn với <see cref="MonthlyPassExpiryBackgroundService"/> (job nền đầu tiên của dự án):
///
/// 1. Vòng lặp chỉ canh nhịp; RUỘT của job nằm ở <see cref="ITripGenerationService"/> (scoped).
///    Hosted service là singleton sống suốt vòng đời app còn AppDbContext là scoped — mỗi lượt
///    chạy mở một scope mới rồi đóng lại, không nhốt DbContext sống mãi.
/// 2. Mọi lỗi bị NUỐT trong <see cref="SweepOnceAsync"/>. Mặc định của .NET là
///    <c>BackgroundServiceExceptionBehavior.StopHost</c>: để exception vọt ra khỏi ExecuteAsync một
///    lần vì CSDL chập chờn là cả API sập theo. Hỏng thì lượt sau thử lại.
/// 3. Chạy NGAY một lượt lúc khởi động rồi mới theo nhịp — app deploy/restart lại là những ngày
///    trống được lấp ngay, không phải chờ hết một nhịp.
///
/// Khác một điểm: job này GHI THÊM dòng mới chứ không lật trạng thái dòng cũ, nên tính "chạy lại
/// không sinh trùng" không đến từ phép ghi mà từ chính logic — xem "ngày đã có chuyến thì không
/// đụng vào" ở <see cref="TripGenerationService.GenerateUpcomingAsync"/>.
/// </summary>
public class TripGenerationBackgroundService : BackgroundService
{
    /// <summary>
    /// Nhịp chạy. Một giờ một lượt là quá đủ: mỗi lượt chỉ việc lấp những ngày còn trống trong tầm
    /// 7 ngày, nên lượt thứ hai trong cùng một ngày là lượt không làm gì (mọi ngày đã có chuyến).
    ///
    /// Chạy theo nhịp đều thay vì hẹn đúng nửa đêm giờ Việt Nam: vừa không phải tính mốc hẹn bằng
    /// đồng hồ UTC, vừa tự lấp lỗ hổng ngay lúc app khởi động lại thay vì chờ tới mốc hẹn. Cố ý để
    /// hằng số chứ không mở cấu hình — cùng lý do <c>SweepInterval</c> của job vé tháng: đây không
    /// phải tham số của story nào, và một khoá cấu hình gõ sai làm app chết ngay lúc khởi động.
    /// </summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;

    private readonly ILogger<TripGenerationBackgroundService> _logger;

    public TripGenerationBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<TripGenerationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Nhường luồng TRƯỚC lượt chạy đầu tiên: host gọi StartAsync rồi mới tới phần đồng bộ của
        // ExecuteAsync, nên sinh chuyến ngay tại đây (nhất là lúc CSDL chưa sẵn sàng, phải chờ hết
        // timeout kết nối) là bắt cả app khởi động chậm theo một job phụ.
        await Task.Yield();

        using var timer = new PeriodicTimer(SweepInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            await SweepOnceAsync(stoppingToken);

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // App đang tắt — thoát êm, không phải lỗi nên không ghi log.
                break;
            }
        }
    }

    /// <summary>
    /// Một lượt sinh chuyến trọn vẹn trong scope riêng của nó. Không bao giờ ném ra ngoài — xem
    /// quyết định (2) ở đầu file.
    /// </summary>
    private async Task SweepOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var generation = scope.ServiceProvider.GetRequiredService<ITripGenerationService>();

            var ketQua = await generation.GenerateUpcomingAsync(DateTime.UtcNow, cancellationToken);

            // Chỉ ghi log khi CÓ việc: nhịp một giờ mà lượt nào cũng ghi là 24 dòng rác mỗi ngày,
            // che mất những dòng đáng đọc.
            if (ketQua.TripsCreated > 0)
            {
                _logger.LogInformation(
                    "Đã sinh {SoChuyen} chuyến cho {SoTuyen} tuyến trong tầm 7 ngày tới.",
                    ketQua.TripsCreated,
                    ketQua.RoutesFilled);
            }

            // Cảnh báo tách khỏi dòng trên: đây là dấu hiệu lịch trình đang THIẾU chuyến so với mẫu
            // vì xe đã rút khỏi đội, không phải tin vui.
            if (ketQua.SkippedInactiveBusTrips > 0)
            {
                _logger.LogWarning(
                    "Bỏ qua {SoChuyen} chuyến mẫu vì xe không còn ở trạng thái khai thác — "
                    + "lịch trình tương lai đang thiếu chừng đó chuyến, cần thay xe.",
                    ketQua.SkippedInactiveBusTrips);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Tắt app giữa lượt chạy — cùng lý do với nhánh bắt OperationCanceledException ở trên.
        }
        catch (Exception ex)
        {
            // Nuốt lỗi ở đây là CỐ Ý, không phải bỏ quên — xem quyết định (2) ở đầu file.
            _logger.LogError(ex, "Lượt sinh chuyến tự động thất bại; sẽ thử lại ở lượt kế tiếp.");
        }
    }
}
