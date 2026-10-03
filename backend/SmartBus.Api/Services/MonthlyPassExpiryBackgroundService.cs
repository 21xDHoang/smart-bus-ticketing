namespace SmartBus.Api.Services;

/// <summary>
/// Job nền quét vé tháng hết hạn — task *"BackgroundService tự động chuyển vé tháng hết hạn sang
/// trạng thái Expired"* (US 16, Nguyễn Duy Kiên).
///
/// Đây là BackgroundService ĐẦU TIÊN của dự án, nên ba quyết định dưới đây ghi lại để các job sau
/// (job sinh chuyến của story 13, nhắc hẹn của Sprint 3) đi cùng một lối:
///
/// 1. Vòng lặp chỉ canh nhịp; RUỘT của job nằm ở <see cref="IMonthlyPassExpiryService"/> (scoped).
///    Hosted service là singleton sống suốt vòng đời app, còn AppDbContext là scoped — nhốt DbContext
///    thẳng vào đây là giữ một DbContext sống mãi, change tracker phình dần và chết cứng khi kết nối
///    CSDL đứt. Mỗi lượt quét mở một scope mới rồi đóng lại.
/// 2. Mọi lỗi bị NUỐT trong <see cref="SweepOnceAsync"/>. Mặc định của .NET là
///    <c>BackgroundServiceExceptionBehavior.StopHost</c>: để exception vọt ra khỏi ExecuteAsync một
///    lần vì CSDL chập chờn là cả API sập theo — đổi một tính năng phụ lấy cả dịch vụ. Hỏng thì lượt
///    sau thử lại.
/// 3. Chạy NGAY một lượt lúc khởi động rồi mới theo nhịp. App deploy/restart lại sau khi đã qua
///    ValidTo của một loạt vé; chờ hết một nhịp mới quét thì khoảng "vé hết hạn vẫn còn Status =
///    Active" — đúng khoảng mà hợp đồng API phải cảnh báo — dài thêm bằng cả nhịp đó. Đây cũng là
///    đường demo job tại chỗ: khởi động app với một vé đã quá hạn là thấy nó lật ngay.
/// </summary>
public class MonthlyPassExpiryBackgroundService : BackgroundService
{
    /// <summary>
    /// Nhịp quét. 15 phút là mức dung hoà: cột Status vốn đã có độ trễ và không phải nguồn sự thật về
    /// hiệu lực (xem <see cref="MonthlyPassStatus"/>), nên quét dày hơn không làm gì đúng hơn mà chỉ
    /// tốn thêm một truy vấn CSDL mỗi lượt. Cố ý để hằng số chứ không mở cấu hình: đây không phải
    /// tham số của story nào, và một khoá cấu hình gõ sai sẽ làm app chết ngay lúc khởi động — đổi
    /// lấy một thứ không ai cần chỉnh.
    /// </summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(15);

    private readonly IServiceScopeFactory _scopeFactory;

    private readonly ILogger<MonthlyPassExpiryBackgroundService> _logger;

    public MonthlyPassExpiryBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<MonthlyPassExpiryBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Nhường luồng TRƯỚC lượt quét đầu tiên. Host gọi StartAsync rồi mới tới phần đồng bộ của
        // ExecuteAsync, mà phần đó chạy xong StartAsync mới trả về — quét CSDL ngay tại đây (nhất là
        // lúc CSDL chưa sẵn sàng, phải chờ hết timeout kết nối) là bắt cả app khởi động chậm theo một
        // job phụ.
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
    /// Một lượt quét trọn vẹn trong scope riêng của nó. Không bao giờ ném ra ngoài — xem quyết định
    /// (2) ở đầu file.
    /// </summary>
    private async Task SweepOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var expiry = scope.ServiceProvider.GetRequiredService<IMonthlyPassExpiryService>();

            var flipped = await expiry.ExpireDuePassesAsync(DateTime.UtcNow, cancellationToken);

            // Chỉ ghi log khi CÓ việc. Nhịp 15 phút: mỗi lượt không làm gì mà vẫn ghi là 96 dòng log
            // rác mỗi ngày, che mất những dòng đáng đọc.
            if (flipped > 0)
            {
                _logger.LogInformation(
                    "Đã chuyển {SoVeThang} vé tháng hết hạn sang trạng thái Expired.", flipped);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Tắt app giữa lượt quét — cùng lý do với nhánh bắt OperationCanceledException ở trên.
        }
        catch (Exception ex)
        {
            // Nuốt lỗi ở đây là CỐ Ý, không phải bỏ quên — xem quyết định (2) ở đầu file.
            _logger.LogError(ex, "Lượt quét vé tháng hết hạn thất bại; sẽ thử lại ở lượt kế tiếp.");
        }
    }
}
