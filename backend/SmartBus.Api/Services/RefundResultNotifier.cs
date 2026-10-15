using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Refunds;
using SmartBus.Api.Entities;
using SmartBus.Api.Hubs;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IRefundResultNotifier"/> — task Sprint 4 *"Thông báo kết quả hoàn tiền qua email
/// và trên web"* (dòng 34, Phùng Duy Hoàng). Xem interface để biết hai chốt hợp đồng: không bao giờ
/// ném lỗi, và không nhận <c>CancellationToken</c>.
///
/// <para>
/// <b>Chỉ báo kết quả CUỐI — phán quyết đọc từ dòng <c>Refunds</c> ĐÃ LƯU, không từ
/// <see cref="RefundAttemptOutcome.Result"/>:</b> một khoản hoàn có thể phải gọi cổng nhiều lượt
/// (thang thử lại 5' → 30' → 2h → 12h → 24h của <see cref="RefundRetryPolicy"/>), nên báo theo từng
/// lượt là dội tới sáu thông báo cho một khoản — "mỗi lượt gọi" là chuyện kỹ thuật, "kết quả cuối" mới
/// là chuyện khách quan tâm. Ba nhánh, đúng ba trạng thái phân biệt được của dòng đã lưu:
/// <list type="bullet">
/// <item><see cref="RefundStatus.Succeeded"/> → báo "đã hoàn tiền" (cổng đã xác nhận tiền đi);</item>
/// <item><see cref="RefundStatus.Failed"/> và <see cref="Refund.NextRetryAt"/> null → báo "hoàn tiền
/// thất bại" — hết thang, hệ thống đã làm hết phần tự động, khoản chờ quản trị;</item>
/// <item>mọi trường hợp còn lại → KHÔNG báo: <c>Failed</c> còn mốc thử lại (thất bại tạm thời, ngã
/// ngũ chưa xong) và <c>Pending</c> (cổng chưa trả lời — báo thành công hay thất bại đều là ĐOÁN,
/// mà đoán thất bại còn mở đường cho job gọi lại một khoản cổng đã trả).</item>
/// </list>
/// Đọc dòng đã lưu còn tự chữa ca thua cuộc đua: <c>outcome</c> là ảnh chụp lúc service chạy, còn dòng
/// có thể đã bị lượt khác lật kể từ đó — thứ được báo luôn là trạng thái thật mới nhất, và nếu lượt
/// khác cũng báo thì chốt chống trùng bên dưới nuốt lượt đến sau.
/// </para>
///
/// <para>
/// <b>Vì sao <c>Claimed = false</c> thì im lặng:</b> lượt thua cuộc đua (hoặc khoản đã biến mất) KHÔNG
/// gọi cổng và không ghi gì — không có kết quả nào để báo, và người gọi đã tự ghi log "bỏ qua" cho
/// chính lượt đó rồi (<see cref="RefundRetryService.SweepAsync"/>). Ghi thêm ở đây là hai dòng log cho
/// một sự việc không xảy ra.
/// </para>
///
/// <para>
/// ⚠️ <b>Vế "qua email" của tên task KHÔNG được cài:</b> A8.5 chốt thông báo in-app + bảng
/// <c>Notifications</c>, không email/SMS/Web Push — và người đặt việc chốt lại ngày 15/10: "chỉ cần
/// thông báo trên web". Không có mã gửi thư nào trong file này, cũng không có cấu hình SMTP nào được
/// đọc.
/// </para>
///
/// <para>
/// <b>Vì sao nội dung không có số tiền và không có mã khoản hoàn:</b> nội dung phải đọc đứng một mình
/// được (A8.5), nhưng con số phải là thứ khách tra ở màn "Hoàn tiền của tôi" — nơi dòng <c>Refunds</c>
/// sống. Chép số tiền vào đây là dựng bản sao thứ hai có thể lệch khỏi dòng gốc (số tiền đã thực sự
/// gửi lên cổng), và là dữ liệu chết trong chuỗi văn bản. Có test ghim body không chứa chữ số nào —
/// cùng lối docs/33 §6.1.
/// </para>
/// </summary>
public sealed class RefundResultNotifier : IRefundResultNotifier
{
    private const string RefundSucceededTitle = "Đã hoàn tiền";

    private const string RefundFailedTitle = "Hoàn tiền thất bại";

    private const string RefundSucceededBody
        = "Khoản hoàn tiền của bạn đã được xử lý thành công.";

    private const string RefundFailedBody
        = "Khoản hoàn tiền của bạn chưa thực hiện được sau nhiều lần thử lại. "
          + "Bạn vui lòng liên hệ nhà xe để được hỗ trợ.";

    private readonly AppDbContext _db;

    private readonly IHubContext<NotificationHub, INotificationClient> _hub;

    private readonly ILogger<RefundResultNotifier> _logger;

    public RefundResultNotifier(
        AppDbContext db,
        IHubContext<NotificationHub, INotificationClient> hub,
        ILogger<RefundResultNotifier> logger)
    {
        _db = db;
        _hub = hub;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task NotifyAsync(RefundAttemptOutcome outcome)
    {
        if (!outcome.Claimed)
        {
            // Xem doc-comment lớp: lượt không giành được dòng thì không có gì để báo, và người gọi đã
            // ghi log "bỏ qua" cho chính lượt đó.
            return;
        }

        Refund? refund;

        try
        {
            // Đọc lại dòng thật: outcome là ảnh chụp lúc service chạy, còn đây là trạng thái đã lưu.
            // AsNoTracking vì chỉ đọc, không ghi gì vào dòng Refunds — lượt gọi cổng đã ghi xong kết
            // quả trước khi notifier chạy, và notifier không phải người lật trạng thái.
            refund = await _db.Refunds
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == outcome.RefundId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Không đọc được khoản hoàn {RefundCode} để báo kết quả cho khách — bỏ qua thông báo.",
                outcome.RefundCode);
            return;
        }

        if (refund is null)
        {
            // Lượt gọi vừa giành được dòng nên dòng phải tồn tại; đến đây nghĩa là dòng bị xoá giữa
            // hai bước (không luồng nghiệp vụ nào làm thế — Refunds là dữ liệu doanh thu, không xoá).
            _logger.LogWarning(
                "Khoản hoàn {RefundCode} không còn trong CSDL ở bước báo kết quả — bỏ qua thông báo.",
                outcome.RefundCode);
            return;
        }

        if (refund.Status == RefundStatus.Succeeded)
        {
            await WriteAndPushAsync(
                refund.UserId,
                NotificationType.RefundSucceeded,
                refund.Id,
                RefundSucceededTitle,
                RefundSucceededBody);
            return;
        }

        if (refund.Status == RefundStatus.Failed && refund.NextRetryAt is null)
        {
            await WriteAndPushAsync(
                refund.UserId,
                NotificationType.RefundFailed,
                refund.Id,
                RefundFailedTitle,
                RefundFailedBody);
            return;
        }

        // Còn lại: Failed CÒN mốc thử lại (đang chờ lịch) hoặc Pending (cổng chưa trả lời). Cả hai
        // đều chưa có kết quả cuối để báo. Im lặng là chủ ý và cố ý KHÔNG ghi log: đường này đi qua ở
        // mọi lượt thất bại tạm thời, ghi log ở đây là làm ngập log vận hành bằng những dòng không
        // nói lên điều gì mới.
    }

    /// <summary>
    /// Ghi dòng thông báo rồi đẩy sự kiện. Hai bước nằm ở hai khối <c>try</c> RIÊNG, cùng lý do đã ghi
    /// ở <see cref="TicketRequestResultNotifier"/>: ghi hỏng nghĩa là khách không có gì đọc lại (mất
    /// thông báo thật), còn đẩy hỏng chỉ nghĩa là phiên đang mở không được nhắc (khách vẫn thấy khi
    /// mở lại màn thông báo).
    /// </summary>
    private async Task WriteAndPushAsync(
        Guid recipientId,
        NotificationType type,
        Guid relatedEntityId,
        string title,
        string body)
    {
        Notification notification;

        try
        {
            // Chống ghi trùng: một kết quả cuối chỉ được báo MỘT lần cho mỗi loại. Khoá suy luận là
            // (người nhận, loại, dòng Refunds) — lặp lại cùng loại bị nuốt; nhưng hết thang thất bại
            // rồi quản trị bấm thử tay thành công là HAI loại khác nhau, nên khách vẫn nhận được cả
            // hai thông báo — đúng vòng đời thật của khoản đó.
            //
            // ⚠️ Phép ĐỌC TRƯỚC KHI GHI — cùng giới hạn một-bản-app và cùng lý do cố ý KHÔNG đặt
            // unique index ở tầng CSDL đã ghi đầy đủ ở TicketRequestResultNotifier.NotifyAsync: dư một
            // dòng trùng là thiệt hại nhỏ hơn hẳn một thông báo hợp lệ bị chặn.
            var alreadyNotified = await _db.Notifications
                .AsNoTracking()
                .AnyAsync(n => n.UserId == recipientId && n.Type == type && n.RelatedEntityId == relatedEntityId);

            if (alreadyNotified)
            {
                _logger.LogInformation(
                    "Khoản hoàn {RelatedEntityId} đã được báo cho khách {UserId} ({Type}) — bỏ qua lượt gọi lại.",
                    relatedEntityId,
                    recipientId,
                    type);
                return;
            }

            notification = new Notification
            {
                UserId = recipientId,
                Type = type,
                RelatedEntityType = NotificationTarget.Refund,
                RelatedEntityId = relatedEntityId,
                Title = title,
                Body = body,
                CreatedAt = DateTime.UtcNow,
            };

            _db.Notifications.Add(notification);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Nuốt lỗi có chủ ý — xem IRefundResultNotifier. Ghi lại đủ chi tiết để còn dựng lại được
            // thông báo đã mất: người nhận, loại, và dòng mà nó nói về.
            _logger.LogError(
                ex,
                "Không ghi được thông báo {Type} cho khách {UserId} về khoản hoàn {RelatedEntityId}.",
                type,
                recipientId,
                relatedEntityId);
            return;
        }

        _logger.LogInformation(
            "Đã ghi thông báo {Type} cho khách {UserId} về khoản hoàn {RelatedEntityId}.",
            type,
            recipientId,
            relatedEntityId);

        try
        {
            // Nhóm suy ra từ ID NGƯỜI NHẬN, không từ tham số client gửi lên — xem NotificationHub.
            await _hub.Clients
                .Group(NotificationHub.GroupOf(recipientId))
                .NotificationReceived(notification.Id);
        }
        catch (Exception ex)
        {
            // Cùng hợp đồng nuốt lỗi như TicketRequestResultNotifier: dòng thông báo đã nằm trong CSDL,
            // nên khách vẫn đọc được khi mở lại màn thông báo. Mất đúng lời nhắc tức thời thì không
            // đáng làm hỏng lượt gọi cổng vừa thành công.
            _logger.LogWarning(
                ex,
                "Không đẩy được sự kiện NotificationReceived cho khách {UserId} (thông báo {NotificationId} đã ghi).",
                recipientId,
                notification.Id);
        }
    }
}
