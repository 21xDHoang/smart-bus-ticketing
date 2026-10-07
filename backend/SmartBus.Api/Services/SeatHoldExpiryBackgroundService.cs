namespace SmartBus.Api.Services;

/// <summary>
/// Job nền quét hold hết hạn — nhả ghế khách đã ngừng thao tác thanh toán (US 3 "Giữ chỗ tạm thời").
/// Task *"Migrate bảng SeatHoldLogs + job quét hold hết hạn"* — Vàng Thị Dăm.
///
/// Đi cùng một lối với <see cref="MonthlyPassExpiryBackgroundService"/> (job nền đầu tiên của dự án,
/// ba quyết định ở đầu file đó): vòng lặp chỉ canh nhịp còn ruột nằm ở <see cref="ISeatHoldExpiryService"/>
/// (scoped); mọi lỗi bị nuốt trong <see cref="SweepOnceAsync"/> để CSDL chập chờn không kéo sập API;
/// và chạy NGAY một lượt lúc khởi động rồi mới theo nhịp.
///
/// Khác duy nhất ở NHỊP, và khác có lý do — xem <see cref="SweepInterval"/>.
/// </summary>
public class SeatHoldExpiryBackgroundService : BackgroundService
{
    /// <summary>
    /// Nhịp quét: 1 phút.
    ///
    /// Đừng chép 15 phút của job vé tháng sang đây — hai job canh hai thứ khác hẳn nhau về độ trễ
    /// chấp nhận được. Bên vé tháng, cột Status chỉ để lọc/thống kê và KHÔNG phải nguồn sự thật về
    /// hiệu lực, nên quét dày hơn không làm gì đúng hơn. Ở đây thì ngược lại: ghế bị chặn là ghế
    /// người khác không đặt được, và nó chỉ được nhả đúng ở lượt quét này. Nhịp 15 phút biến lượt
    /// giữ 10 phút thành chặn ghế tới 25 phút — hành khách nhìn sơ đồ thấy một ghế trống không ai
    /// ngồi mà bấm vào chỉ nhận lỗi trùng ghế.
    ///
    /// 1 phút là mức dung hoà: độ trễ thêm tối đa 1 phút trên một lượt giữ 10 phút (10%) là mức
    /// hành khách không nhận ra, còn mỗi lượt quét chỉ là một truy vấn đi thẳng vào
    /// IX_SeatHolds_ExpiresAt và gần như luôn rỗng. Cố ý để hằng số chứ không mở cấu hình — cùng lý
    /// do đã ghi ở MonthlyPassExpiryBackgroundService: đây không phải tham số của story nào, và một
    /// khoá cấu hình gõ sai sẽ làm app chết ngay lúc khởi động.
    /// </summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;

    private readonly ILogger<SeatHoldExpiryBackgroundService> _logger;

    public SeatHoldExpiryBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<SeatHoldExpiryBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Nhường luồng TRƯỚC lượt quét đầu tiên. Host gọi StartAsync rồi mới tới phần đồng bộ của
        // ExecuteAsync, mà phần đó chạy xong StartAsync mới trả về — quét CSDL ngay tại đây (nhất là
        // lúc CSDL chưa sẵn sàng, phải chờ hết timeout kết nối) là bắt cả app khởi động chậm theo một
        // job phụ. Cùng lối MonthlyPassExpiryBackgroundService.
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
    /// Một lượt quét trọn vẹn trong scope riêng của nó. Không bao giờ ném ra ngoài — mặc định của
    /// .NET là <c>BackgroundServiceExceptionBehavior.StopHost</c>, để exception vọt ra khỏi đây một
    /// lần vì CSDL chập chờn là cả API sập theo. Hỏng thì lượt sau thử lại.
    /// </summary>
    private async Task SweepOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var expiry = scope.ServiceProvider.GetRequiredService<ISeatHoldExpiryService>();

            var soLuotNha = await expiry.ExpireDueHoldsAsync(DateTime.UtcNow, cancellationToken);

            // Chỉ ghi log khi CÓ việc. Nhịp 1 phút: mỗi lượt không làm gì mà vẫn ghi là 1440 dòng log
            // rác mỗi ngày, che mất những dòng đáng đọc.
            if (soLuotNha > 0)
            {
                _logger.LogInformation(
                    "Đã nhả {SoLuotGiu} lượt giữ ghế quá hạn và ghi nhật ký SeatHoldLogs.", soLuotNha);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Tắt app giữa lượt quét — cùng lý do với nhánh bắt OperationCanceledException ở trên.
        }
        catch (Exception ex)
        {
            // Nuốt lỗi ở đây là CỐ Ý, không phải bỏ quên. Gồm cả DbUpdateException khi hai bản app
            // cùng quét một lượt: bản thua rollback rồi thử lại ở lượt sau, lúc đó không còn gì để
            // làm vì bản thắng đã lật xong.
            _logger.LogError(ex, "Lượt quét hold hết hạn thất bại; sẽ thử lại ở lượt kế tiếp.");
        }
    }
}
