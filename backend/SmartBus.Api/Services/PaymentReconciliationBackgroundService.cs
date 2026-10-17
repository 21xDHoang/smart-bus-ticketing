namespace SmartBus.Api.Services;

/// <summary>
/// Job nền đối soát thanh toán (US 6 — task *"đối soát tự động"*, Trần Trung Hiếu). Mỗi lượt quét,
/// các giao dịch <c>Pending</c> quá 5 phút được hỏi cổng rồi chốt qua
/// <see cref="PaymentSettlementService"/>.
///
/// Cùng một lối với các job nền trước (<see cref="RefundRetryBackgroundService"/> là bản gần
/// nhất): vòng lặp chỉ canh nhịp còn ruột nằm ở <see cref="IPaymentReconciliationService"/>
/// (scoped); mọi lỗi bị nuốt trong <see cref="PaymentReconciliationService.SweepOnceAsync"/> để
/// CSDL chập chờn không kéo sập API.
///
/// Nhịp: 1 phút — khách ngồi màn chờ tối đa 60 giây rồi bỏ cuộc; đối soát chậm hơn một nhịp là
/// bắt khách bỏ cuộc oan (chốt ở mục "Thanh toán" của hợp đồng). Cố ý để hằng số chứ không mở cấu
/// hình — cùng lý do đã ghi ở <see cref="RefundRetryBackgroundService"/>.
/// </summary>
public class PaymentReconciliationBackgroundService : BackgroundService
{
    /// <summary>Nhịp quét: 1 phút (hợp đồng mục "Job đối soát tự động").</summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PaymentReconciliationBackgroundService> _logger;

    public PaymentReconciliationBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<PaymentReconciliationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Nhường luồng TRƯỚC lượt quét đầu tiên — quét CSDL ngay tại đây là bắt cả app khởi động
        // chậm theo một job phụ (cùng lý do đã ghi ở RefundRetryBackgroundService).
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
    /// Mở scope riêng cho MỘT lượt quét: hosted service là singleton còn AppDbContext là scoped,
    /// dùng thẳng là app chết ngay lúc khởi động (cùng lối mọi job nền của dự án).
    /// </summary>
    private async Task SweepOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IPaymentReconciliationService>();

            var processed = await service.SweepOnceAsync(DateTime.UtcNow, stoppingToken);

            if (processed > 0)
            {
                _logger.LogInformation("Đối soát thanh toán: đã chốt {Count} giao dịch trong lượt quét.", processed);
            }
        }
        catch (Exception ex)
        {
            // CSDL chập chờn không được kéo sập vòng lặp — lỗi ở đây chỉ là một lượt quét trượt,
            // lượt sau chạy tiếp (cùng lối RefundRetryBackgroundService).
            _logger.LogError(ex, "Đối soát thanh toán: lượt quét lỗi — bỏ qua, chờ nhịp sau.");
        }
    }
}
