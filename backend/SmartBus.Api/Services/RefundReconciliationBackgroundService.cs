namespace SmartBus.Api.Services;

/// <summary>
/// Job nền đối soát hoàn tiền hằng ngày (US 8 — task *"Đối soát hoàn tiền hằng ngày + cảnh báo khi
/// lệch số liệu"*, dòng 32 Sprint 4, Nguyễn Duy Kiên). Mỗi ngày một lần, vào
/// <see cref="RunAtUtc"/> (00:05 UTC = 07:05 giờ Việt Nam), job mở một scope rồi giao việc cho
/// <see cref="IRefundReconciliationService"/>; nếu có lệch thì <see cref="IRefundReconciliationNotifier"/>
/// ghi cảnh báo cho quản trị.
///
/// <para>
/// <b>Vòng lặp canh GIỜ CỐ ĐỊNH, không dùng <c>PeriodicTimer</c> như các job khác:</b> các job kia
/// quét theo nhịp ngắn (1–5 phút) nên mốc chạy không quan trọng; job này chạy mỗi 24 giờ, và một
/// <c>PeriodicTimer</c> khởi động cùng app sẽ neo lượt chạy vào lúc app bật — deploy lại lúc 15:00 là
/// cả lịch trôi sang 15:00. Tính lại độ trễ trước MỖI lượt còn tự sửa luôn ca app ngủ/máy chậm: mốc
/// kế tiếp luôn là 00:05 UTC gần nhất chưa tới.
/// </para>
///
/// <para>
/// ⚠️ Job KHÔNG chạy ngay lúc khởi động. Đây là chủ ý: một lượt đối soát ngay sau deploy sẽ chạy
/// chồng lên lượt của bản cũ nếu hai bản cùng sống trong lúc chuyển, và cảnh báo "đêm nay" có thể
/// được gửi hai lần. Bản chạy lại trong cùng ngày vẫn có chốt chống trùng ở notifier, nhưng không
/// cần tạo ra chính cuộc chạy chồng đó.
/// </para>
///
/// <para>
/// Mọi lỗi trong một lượt bị nuốt (cùng lối <see cref="RefundRetryBackgroundService"/>): mặc định
/// của .NET là <c>BackgroundServiceExceptionBehavior.StopHost</c>, để một lần CSDL chập chờn làm sập
/// cả API là cái giá không đáng trả cho một job phụ.
/// </para>
/// </summary>
public class RefundReconciliationBackgroundService : BackgroundService
{
    /// <summary>
    /// Mốc chạy hằng ngày theo UTC. 00:05 UTC = 07:05 giờ Việt Nam — đầu ngày làm việc, sau khi các
    /// giao dịch của hôm trước đã chốt. Cố ý để hằng số chứ không mở cấu hình: đây không phải tham
    /// số của story nào, và một khoá cấu hình gõ sai làm app chết ngay lúc khởi động (cùng lý do đã
    /// ghi ở <see cref="RefundRetryBackgroundService"/>).
    /// </summary>
    public static readonly TimeOnly RunAtUtc = new(0, 5);

    private readonly IServiceScopeFactory _scopeFactory;

    private readonly ILogger<RefundReconciliationBackgroundService> _logger;

    public RefundReconciliationBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<RefundReconciliationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Nhường luồng trước khi làm bất cứ việc gì — cùng lý do đã ghi ở các job khác: không để
        // một job phụ làm chậm lúc app khởi động.
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(DelayUntilNextRun(DateTime.UtcNow), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // App đang tắt — thoát êm, không phải lỗi nên không ghi log.
                return;
            }

            await RunOnceAsync(stoppingToken);
        }
    }

    /// <summary>
    /// Khoảng chờ từ <paramref name="utcNow"/> tới mốc <see cref="RunAtUtc"/> gần nhất CHƯA tới.
    /// Hàm thuần, public để test đếm được mà không phải chờ đồng hồ thật.
    ///
    /// <para>
    /// Đúng bằng 0 chỉ xảy ra khi <paramref name="utcNow"/> trùng chính xác mốc chạy — khi đó trả về
    /// trọn một ngày, không trả 0: một vòng lặp nhận 0 sẽ chạy hai lượt trong cùng một giây.
    /// </para>
    /// </summary>
    public static TimeSpan DelayUntilNextRun(DateTime utcNow)
    {
        var todayRun = utcNow.Date.Add(RunAtUtc.ToTimeSpan());
        var nextRun = todayRun > utcNow ? todayRun : todayRun.AddDays(1);

        return nextRun - utcNow;
    }

    /// <summary>
    /// Một lượt đối soát trọn vẹn trong scope riêng. Không bao giờ ném ra ngoài.
    /// </summary>
    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();

            var reconciliation = scope.ServiceProvider
                .GetRequiredService<IRefundReconciliationService>();
            var notifier = scope.ServiceProvider
                .GetRequiredService<IRefundReconciliationNotifier>();

            // Đồng hồ thật ở đúng một chỗ: service nhận now từ người gọi để test ghim được mốc.
            var now = DateTime.UtcNow;

            var report = await reconciliation.RunAsync(now, cancellationToken);

            if (!report.HasMismatch)
            {
                _logger.LogInformation(
                    "Đối soát hoàn tiền {RunAt:yyyy-MM-dd}: không có lệch số liệu trong {Count} khoản đã kiểm "
                    + "(sao kê cổng: {Statement}).",
                    report.RunAtUtc,
                    report.ScannedRefundCount,
                    report.StatementAvailable ? "đã đối chiếu" : "không khả dụng");
                return;
            }

            var notifyResult = await notifier.NotifyAsync(report);

            _logger.LogWarning(
                "Đối soát hoàn tiền {RunAt:yyyy-MM-dd}: {IssueCount} điểm lệch ({CriticalCount} nghiêm "
                + "trọng) trong {Count} khoản đã kiểm; đã ghi {Created} cảnh báo cho {Recipients} quản trị.",
                report.RunAtUtc,
                report.Issues.Count,
                report.CriticalIssueCount,
                report.ScannedRefundCount,
                notifyResult.CreatedCount,
                notifyResult.RecipientCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Tắt app giữa lượt đối soát. Không có gì bị sửa dở (service chỉ đọc), lượt sau chạy lại
            // từ đầu.
        }
        catch (Exception ex)
        {
            // Nuốt lỗi là CỐ Ý — xem doc-comment lớp. Lượt kế tiếp là 24 giờ sau, nên log phải đủ
            // chi tiết để người trực biết đêm đó không có kết quả đối soát.
            _logger.LogError(
                ex,
                "Lượt đối soát hoàn tiền thất bại; sẽ thử lại ở mốc {NextRun:O}.",
                DateTime.UtcNow.Add(DelayUntilNextRun(DateTime.UtcNow)));
        }
    }
}
