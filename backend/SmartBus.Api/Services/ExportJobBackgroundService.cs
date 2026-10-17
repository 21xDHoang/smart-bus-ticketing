namespace SmartBus.Api.Services;

/// <summary>
/// Job nền của hàng đợi xuất báo cáo — task *"Xử lý xuất file lớn bất đồng bộ + thông báo khi
/// xong"* (Sprint 5 dòng 64, Vàng Thị Dăm).
///
/// Cùng lối ba job nền đã có của dự án (xem <see cref="MonthlyPassExpiryBackgroundService"/>):
/// vòng lặp là singleton chỉ canh nhịp, ruột là service scoped
/// (<see cref="IExportJobProcessor"/> / <see cref="IExportJobService"/>) và mỗi lượt mở một scope
/// mới. Mọi lỗi hạ tầng bị nuốt để CSDL chập chờn không kéo sập API.
///
/// <para>
/// <b>Nhịp 2 giây</b> — khác hẳn 15 phút của job vé tháng. Ở đây độ trễ là thứ người dùng NHÌN
/// THẤY: họ vừa bấm "Xuất báo cáo" và đang chờ trạng thái đổi. Hai giây là đủ nhanh để cảm giác
/// "xử lý ngay", còn mỗi lượt không có job chỉ là một truy vấn đi qua
/// <c>IX_ExportJobs_Status_CreatedAt</c> và gần như luôn rỗng. Mỗi lượt xử lý tối đa
/// <see cref="MaxJobsPerTick"/> job để một hàng đợi dài không giữ vòng lặp trong một tick mãi mãi.
/// </para>
///
/// <para>
/// <b>Hai việc bảo trì chạy theo nhịp riêng:</b> đưa job <c>Processing</c> bị bỏ quên về
/// <c>Pending</c> mỗi 5 phút (app tắt giữa lúc dựng file), và xoá job quá <c>ExpiresAt</c> mỗi
/// giờ. Cả hai đều là truy vấn theo chỉ mục sẵn có.
/// </para>
/// </summary>
public class ExportJobBackgroundService : BackgroundService
{
    /// <summary>Nhịp quét hàng đợi — xem doc-comment đầu file.</summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(2);

    /// <summary>Nhịp thu hồi job <c>Processing</c> bị bỏ quên.</summary>
    private static readonly TimeSpan RecoveryInterval = TimeSpan.FromMinutes(5);

    /// <summary>Nhịp dọn job hết hạn (file trong cột <c>bytea</c> nên dọn là việc bắt buộc).</summary>
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

    /// <summary>
    /// Một job <c>Processing</c> lâu hơn mốc này bị coi là bỏ quên. Chọn 10 phút: lớn hơn hẳn thời
    /// gian dựng một file 50.000 dòng trên hosting nhỏ, nhưng đủ ngắn để một lần deploy không để
    /// người dùng chờ tới hết hạn 24 giờ.
    /// </summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    /// <summary>Số job tối đa xử lý liền trong một tick trước khi nhường lại cho timer.</summary>
    private const int MaxJobsPerTick = 10;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExportJobBackgroundService> _logger;

    public ExportJobBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<ExportJobBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Nhường luồng trước lượt quét đầu tiên — cùng lý do ba job nền khác: không bắt cả app
        // khởi động chậm theo một job phụ.
        await Task.Yield();

        var lastRecovery = DateTime.MinValue;
        var lastCleanup = DateTime.MinValue;

        using var timer = new PeriodicTimer(SweepInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;

            if (now - lastRecovery >= RecoveryInterval)
            {
                lastRecovery = now;
                await RunMaintenanceAsync(
                    service => service.RecoverStaleAsync(now, StaleAfter, stoppingToken),
                    "thu hồi job xuất bị bỏ quên",
                    stoppingToken);
            }

            if (now - lastCleanup >= CleanupInterval)
            {
                lastCleanup = now;
                await RunMaintenanceAsync(
                    service => service.CleanupExpiredAsync(now, stoppingToken),
                    "dọn job xuất hết hạn",
                    stoppingToken);
            }

            for (var i = 0; i < MaxJobsPerTick; i++)
            {
                if (!await ProcessOneAsync(stoppingToken))
                {
                    break;
                }
            }

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
    /// Một job trong một scope riêng. Trả <see langword="false"/> khi hàng đợi rỗng hoặc lượt này
    /// hỏng (để vòng lặp dừng tick và thử lại sau, không quay cuồng trên cùng một lỗi).
    /// </summary>
    private async Task<bool> ProcessOneAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<IExportJobProcessor>();

            return await processor.ProcessNextAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            // Nuốt lỗi là CỐ Ý — xem quyết định (2) ở MonthlyPassExpiryBackgroundService. Job còn
            // Pending/Processing sẽ được lượt sau nhặt lại (Processing quá 10 phút được thu hồi).
            _logger.LogError(ex, "Lượt xử lý hàng đợi xuất báo cáo thất bại; sẽ thử lại sau.");
            return false;
        }
    }

    /// <summary>Chạy một việc bảo trì trong scope riêng, nuốt mọi lỗi sau khi ghi log.</summary>
    private async Task RunMaintenanceAsync(
        Func<IExportJobService, Task<int>> action,
        string description,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IExportJobService>();

            var affected = await action(service);

            if (affected > 0)
            {
                _logger.LogInformation("Đã {Description}: {Count} job.", description, affected);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Tắt app — không phải lỗi.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không {Description}; sẽ thử lại ở lượt kế tiếp.", description);
        }
    }
}
