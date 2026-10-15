namespace SmartBus.Api.Services;

/// <summary>
/// Job nền thử lại hoàn tiền theo lịch (US 8 — task *"Xử lý hoàn tiền thất bại + retry theo lịch"*,
/// Phùng Duy Hoàng). Mỗi lượt quét, các khoản hoàn đang <c>Failed</c> và tới mốc <c>NextRetryAt</c>
/// được gọi lại cổng theo nấc thang ở <see cref="RefundRetryPolicy"/>.
///
/// Cùng một lối với các job nền trước (<see cref="SeatHoldExpiryBackgroundService"/> là bản gần
/// nhất): vòng lặp chỉ canh nhịp còn ruột nằm ở <see cref="IRefundRetryService"/> (scoped); mọi lỗi
/// bị nuốt trong <see cref="SweepOnceAsync"/> để CSDL chập chờn không kéo sập API; và chạy NGAY một
/// lượt lúc khởi động rồi mới theo nhịp.
///
/// Nhịp: 1 phút — xem <see cref="SweepInterval"/>.
/// </summary>
public class RefundRetryBackgroundService : BackgroundService
{
    /// <summary>
    /// Nhịp quét: 1 phút.
    ///
    /// Nấc thang thử lại ngắn nhất là 5 phút, nên nhịp này thừa sức bám: trễ thêm tối đa 1 phút
    /// trên mốc hẹn là mức không ai nhận ra, còn mỗi lượt quét gần như luôn rỗng — một truy vấn đi
    /// thẳng vào IX_Refunds_Status_NextRetryAt. Cố ý để hằng số chứ không mở cấu hình — cùng lý do
    /// đã ghi ở SeatHoldExpiryBackgroundService: đây không phải tham số của story nào, và một khoá
    /// cấu hình gõ sai sẽ làm app chết ngay lúc khởi động.
    /// </summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;

    private readonly ILogger<RefundRetryBackgroundService> _logger;

    public RefundRetryBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<RefundRetryBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Nhường luồng TRƯỚC lượt quét đầu tiên — cùng lý do đã ghi ở SeatHoldExpiryBackgroundService:
        // quét CSDL ngay tại đây là bắt cả app khởi động chậm theo một job phụ.
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
            var retry = scope.ServiceProvider.GetRequiredService<IRefundRetryService>();

            var soLuotGoi = await retry.SweepAsync(DateTime.UtcNow, cancellationToken);

            // Chỉ ghi log khi CÓ việc. Nhịp 1 phút: mỗi lượt không làm gì mà vẫn ghi là 1440 dòng
            // log rác mỗi ngày, che mất những dòng đáng đọc.
            if (soLuotGoi > 0)
            {
                _logger.LogInformation(
                    "Đã thực hiện {SoLuotGoi} lượt gọi cổng cho các khoản hoàn tới hạn thử lại.", soLuotGoi);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Tắt app giữa lượt quét — cùng lý do với nhánh bắt OperationCanceledException ở trên.
        }
        catch (Exception ex)
        {
            // Nuốt lỗi ở đây là CỐ Ý, không phải bỏ quên. Gồm cả lỗi CSDL chập chờn giữa lượt quét:
            // các khoản đã xử lý xong đã ở trạng thái mới nên lượt sau chỉ quét phần còn lại.
            _logger.LogError(ex, "Lượt quét thử lại hoàn tiền thất bại; sẽ thử lại ở lượt kế tiếp.");
        }
    }
}
