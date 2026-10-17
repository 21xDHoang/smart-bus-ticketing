using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;
using SmartBus.Api.Hubs;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IExportJobNotifier"/> — task *"Xử lý xuất file lớn bất đồng bộ + thông báo khi
/// xong"* (Sprint 5 dòng 64, Vàng Thị Dăm).
///
/// Cùng khuôn <see cref="TicketRequestResultNotifier"/>: ghi dòng <c>Notifications</c> (bền, đọc lại
/// được khi offline) rồi mới đẩy <c>NotificationReceived(id)</c> qua <see cref="NotificationHub"/>.
/// Hai khối <c>try</c> riêng vì hai thất bại có hậu quả khác nhau; đẩy hỏng không đáng làm hỏng kết
/// quả của job.
///
/// <para>
/// <b>Nội dung không có số tiền và không có đường dẫn tải tuyệt đối.</b> File báo cáo doanh thu có
/// số tiền, nhưng chép số tiền vào thông báo là dựng bản sao thứ hai có thể lệch khỏi file; người
/// dùng mở file để đọc số. Đường dẫn tải lấy từ response API, không nhét URL vào CSDL — đổi domain
/// triển khai mà quên thông báo cũ là gửi người dùng một liên kết chết.
/// </para>
/// </summary>
public sealed class ExportJobNotifier : IExportJobNotifier
{
    private const string CompletedTitle = "Báo cáo đã sẵn sàng";

    private const string FailedTitle = "Xuất báo cáo thất bại";

    private readonly AppDbContext _db;

    private readonly IHubContext<NotificationHub, INotificationClient> _hub;

    private readonly ILogger<ExportJobNotifier> _logger;

    public ExportJobNotifier(
        AppDbContext db,
        IHubContext<NotificationHub, INotificationClient> hub,
        ILogger<ExportJobNotifier> logger)
    {
        _db = db;
        _hub = hub;
        _logger = logger;
    }

    public Task NotifyCompletedAsync(ExportJob job)
    {
        var reportLabel = job.ReportType.ToLabel();
        var rowCountText = job.RowCount is { } rowCount ? $" ({rowCount:N0} dòng)" : string.Empty;

        // Giờ hết hạn ghi bằng UTC kèm nhãn — thông báo là chỗ duy nhất người dùng biết file sẽ
        // biến mất sau 24 giờ, nên phải nói rõ thay vì để họ phát hiện khi bấm tải.
        var expiresText = job.ExpiresAt is { } expiresAt
            ? $" File sẽ hết hạn lúc {expiresAt:yyyy-MM-dd HH:mm} UTC."
            : string.Empty;

        return NotifyAsync(
            job,
            NotificationType.ExportCompleted,
            CompletedTitle,
            $"{char.ToUpperInvariant(reportLabel[0])}{reportLabel[1..]} của bạn đã được xuất xong"
            + $"{rowCountText}. Mở màn xuất báo cáo để tải file.{expiresText}");
    }

    public Task NotifyFailedAsync(ExportJob job, string reason)
    {
        var reportLabel = job.ReportType.ToLabel();

        return NotifyAsync(
            job,
            NotificationType.ExportFailed,
            FailedTitle,
            $"Không xuất được {reportLabel}. Lý do: {reason} "
            + "Hãy thử lại với khoảng thời gian hoặc bộ lọc hẹp hơn.");
    }

    /// <summary>
    /// Ghi rồi đẩy. Hai khối <c>try</c> riêng — xem doc-comment đầu file.
    /// </summary>
    private async Task NotifyAsync(
        ExportJob job,
        NotificationType type,
        string title,
        string body)
    {
        Notification notification;

        try
        {
            // Chống ghi trùng cùng lối TicketRequestResultNotifier: job bị đưa lại Pending sau khi
            // app tắt giữa chừng có thể chốt kết quả hai lần; người dùng chỉ cần một thông báo cho
            // một kết quả. Đọc-trước-khi-ghi chỉ đúng với một bản app — cùng giới hạn đã ghi ở
            // docs/26 và docs/33 §9.2.
            var alreadyNotified = await _db.Notifications
                .AsNoTracking()
                .AnyAsync(n => n.UserId == job.UserId
                    && n.Type == type
                    && n.RelatedEntityId == job.Id);

            if (alreadyNotified)
            {
                _logger.LogInformation(
                    "Job xuất {JobId} đã được báo {Type} cho người dùng {UserId} — bỏ qua lượt gọi lại.",
                    job.Id,
                    type,
                    job.UserId);
                return;
            }

            notification = new Notification
            {
                UserId = job.UserId,
                Type = type,
                RelatedEntityType = NotificationTarget.ExportJob,
                RelatedEntityId = job.Id,
                Title = title,
                Body = body,
                CreatedAt = DateTime.UtcNow,
            };

            _db.Notifications.Add(notification);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Không ghi được thông báo {Type} cho người dùng {UserId} về job xuất {JobId}.",
                type,
                job.UserId,
                job.Id);
            return;
        }

        try
        {
            await _hub.Clients
                .Group(NotificationHub.GroupOf(job.UserId))
                .NotificationReceived(notification.Id);
        }
        catch (Exception ex)
        {
            // Dòng thông báo đã nằm trong CSDL nên người dùng vẫn đọc được khi mở lại màn thông
            // báo. Mất lời nhắc tức thời không đáng làm hỏng job đã dựng xong file.
            _logger.LogWarning(
                ex,
                "Không đẩy được sự kiện NotificationReceived cho người dùng {UserId} "
                + "(thông báo {NotificationId} đã ghi).",
                job.UserId,
                notification.Id);
        }
    }
}
