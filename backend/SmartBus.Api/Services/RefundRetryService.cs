using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Refunds;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Ruột của job thử lại hoàn tiền theo lịch (US 8 — task *"Xử lý hoàn tiền thất bại + retry theo
/// lịch"*, Phùng Duy Hoàng). Chạy trong scope do <c>RefundRetryBackgroundService</c> mở, nhịp 1
/// phút.
///
/// Một lượt quét gồm hai chặng:
/// 1. Đọc ID các khoản <see cref="RefundStatus.Failed"/> có lịch và đã tới hạn — một truy vấn chỉ
///    lấy ID, đi thẳng vào <c>IX_Refunds_Status_NextRetryAt</c> (đúng đường quét mà migration của
///    Dăm dựng ra cho job này);
/// 2. Với TỪNG khoản: mở scope riêng, lấy <see cref="IRefundAttemptService"/> và chạy một lượt
///    gọi cổng.
///
/// Vì sao scope riêng cho từng khoản chứ không dùng chung context của lượt quét: lượt gọi có thể
/// vấp <c>DbUpdateConcurrencyException</c> khi thua cuộc đua giành dòng, và EF để nguyên entity dở
/// dang trong context — dùng chung là lượt gọi của khoản sau có nguy cơ ghi nhờ luôn thay đổi của
/// khoản trước. Cùng lối lập luận đã ghi ở <c>AuditLogService</c>: scope riêng thì việc của khoản
/// này không thể chạm vào việc của khoản kia.
/// </summary>
public class RefundRetryService : IRefundRetryService
{
    private readonly AppDbContext _db;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RefundRetryService> _logger;

    public RefundRetryService(
        AppDbContext db,
        IServiceScopeFactory scopeFactory,
        ILogger<RefundRetryService> logger)
    {
        _db = db;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> SweepAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        // Chỉ lấy ID: mỗi lượt gọi tự đọc lại khoản trong scope của nó (kèm giao dịch gốc), nên nạp
        // sẵn entity ở đây là công vô ích — và tệ hơn, là giữ một bản sao cũ trong lúc lượt gọi
        // đọc bản mới. OrderBy để khoản chờ lâu nhất được gọi trước nếu hàng đợi dài.
        var dueRefundIds = await _db.Refunds
            .Where(r => r.Status == RefundStatus.Failed
                        && r.NextRetryAt != null
                        && r.NextRetryAt <= now)
            .OrderBy(r => r.NextRetryAt)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        var soLuotGoi = 0;

        foreach (var refundId in dueRefundIds)
        {
            using var scope = _scopeFactory.CreateScope();
            var attemptService = scope.ServiceProvider.GetRequiredService<IRefundAttemptService>();

            // Một mốc `now` cho cả lượt quét (job truyền vào): các khoản của cùng một đợt mang cùng
            // dấu thời gian. Lệch thêm tối đa bằng độ dài lượt quét — không đáng kể so với nấc
            // thang ngắn nhất (5 phút), và lượt quét bình thường chỉ vài phần nghìn giây.
            var outcome = await attemptService.ExecuteAsync(
                refundId, RefundLogTrigger.ScheduledRetry, now, cancellationToken);

            if (outcome.Claimed)
            {
                soLuotGoi++;
                LogOutcome(outcome);
            }
            else
            {
                // Không giành được dòng: khoản vừa bị lượt khác đổi trạng thái (thường là quản trị
                // vừa bấm thử tay xong) hoặc đã biến mất giữa lúc quét và lúc chạy. Information vì
                // đây là chuyện lành — lượt quét sau đọc lại theo trạng thái mới.
                _logger.LogInformation(
                    "Khoản {RefundCode}: bỏ qua ở lượt quét thử lại (không còn đủ điều kiện hoặc thua cuộc đua).",
                    outcome.RefundCode);
            }
        }

        return soLuotGoi;
    }

    /// <summary>
    /// Ghi log kết quả MỘT lượt gọi cổng vừa thực hiện. Mức log theo kết quả: thành công là tin
    /// thường; thất bại và không-rõ-kết-quả đều là Warning vì cả hai đều cần người đọc — một cái để
    /// biết lịch thử lại kế tiếp, một cái để biết bước đối soát sắp có việc.
    /// </summary>
    private void LogOutcome(RefundAttemptOutcome outcome)
    {
        switch (outcome.Result)
        {
            case RefundLogResult.Succeeded:
                _logger.LogInformation(
                    "Khoản {RefundCode}: hoàn tiền THÀNH CÔNG.", outcome.RefundCode);
                break;

            case RefundLogResult.Failed:
                var keHoach = outcome.NextRetryAt is { } mocHen
                    ? $"Sẽ thử lại lúc {mocHen:u}."
                    : "Đã hết lịch thử lại; khoản chờ quản trị bấm thử tay.";
                _logger.LogWarning(
                    "Khoản {RefundCode}: lượt gọi cổng thất bại — {LyDo}. {KeHoach}",
                    outcome.RefundCode, outcome.Message, keHoach);
                break;

            default:
                _logger.LogWarning(
                    "Khoản {RefundCode}: cổng không trả lời (Unresolved) — khoản giữ nguyên Pending, " +
                    "bước đối soát sẽ tra với sao kê cổng.",
                    outcome.RefundCode);
                break;
        }
    }
}
