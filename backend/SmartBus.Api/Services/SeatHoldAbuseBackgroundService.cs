namespace SmartBus.Api.Services;

/// <summary>
/// Job nền canh tài khoản giữ chỗ quá nhiều lần (US 3 "Giữ chỗ tạm thời") — task *"Ghi log và cảnh
/// báo khi một tài khoản giữ chỗ quá nhiều lần"* (Sprint 3, Vàng Thị Dăm).
///
/// Cùng một lối với <see cref="SeatHoldExpiryBackgroundService"/> và
/// <see cref="MonthlyPassExpiryBackgroundService"/> (ba quyết định ở đầu file đó): vòng lặp chỉ
/// canh nhịp còn ruột nằm ở <see cref="ISeatHoldAbuseService"/> (scoped); mọi lỗi bị nuốt trong
/// <see cref="SweepOnceAsync"/> để CSDL chập chờn không kéo sập API; và chạy NGAY một lượt lúc khởi
/// động rồi mới theo nhịp.
///
/// Khác ở NHỊP, và khác có lý do — xem <see cref="SweepInterval"/>.
/// </summary>
public class SeatHoldAbuseBackgroundService : BackgroundService
{
    /// <summary>
    /// Nhịp quét: 5 phút.
    ///
    /// Đừng chép 1 phút của <see cref="SeatHoldExpiryBackgroundService"/> sang đây. Ở job hết hạn,
    /// nhịp CHÍNH LÀ độ trễ nhả ghế — ghế chỉ được trả lại ở lượt quét, nên nhịp dày là bắt buộc.
    /// Ở đây nhịp chỉ là độ trễ PHÁT HIỆN, và bản thân phép đếm đã nhìn lui một tiếng: phát hiện
    /// chậm 5 phút trên một hành vi được đo bằng giờ là mức không ai nhận ra (8% cửa sổ), đổi lại
    /// là 288 lượt quét/ngày thay vì 1440 — mỗi lượt vẫn là một truy vấn đi thẳng vào
    /// IX_SeatHolds_UserId_CreatedAt.
    ///
    /// Cố ý để hằng số chứ không mở cấu hình — cùng lý do đã ghi ở các job kia: đây không phải tham
    /// số của story nào, và một khoá cấu hình gõ sai sẽ làm app chết ngay lúc khởi động.
    /// </summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;

    private readonly ILogger<SeatHoldAbuseBackgroundService> _logger;

    public SeatHoldAbuseBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<SeatHoldAbuseBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Nhường luồng TRƯỚC lượt quét đầu tiên — cùng lối hai job kia và cùng lý do: host gọi
        // StartAsync rồi mới tới phần đồng bộ của ExecuteAsync, nên quét CSDL ngay tại đây (nhất là
        // lúc CSDL chưa sẵn sàng) là bắt cả app khởi động chậm theo một job phụ.
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
            var abuse = scope.ServiceProvider.GetRequiredService<ISeatHoldAbuseService>();

            var soTaiKhoan = await abuse.ScanAndWarnAsync(DateTime.UtcNow, cancellationToken);

            // Chỉ ghi log khi CÓ việc. Phần lớn lượt quét không có gì để làm; ghi lại mỗi lượt là
            // 288 dòng "không có gì" mỗi ngày, che mất những dòng đáng đọc. Cùng lối
            // SeatHoldExpiryBackgroundService.
            //
            // Chi tiết từng tài khoản đã được SeatHoldAbuseService ghi ở mức Warning rồi — dòng này
            // chỉ là con số tổng của lượt quét.
            if (soTaiKhoan > 0)
            {
                _logger.LogInformation(
                    "Đã cảnh báo {SoTaiKhoan} tài khoản giữ chỗ quá nhiều lần và ghi vào nhật ký.",
                    soTaiKhoan);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Tắt app giữa lượt quét — cùng lý do với nhánh bắt OperationCanceledException ở trên.
        }
        catch (Exception ex)
        {
            // Nuốt lỗi ở đây là CỐ Ý, không phải bỏ quên. Cùng lối SeatHoldExpiryBackgroundService.
            _logger.LogError(ex, "Lượt quét tài khoản giữ chỗ quá nhiều lần thất bại; sẽ thử lại ở lượt kế tiếp.");
        }
    }
}
