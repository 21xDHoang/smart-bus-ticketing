using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Refunds;
using SmartBus.Api.Entities;
using SmartBus.Api.Hubs;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IRefundReconciliationNotifier"/> — cảnh báo lệch số liệu cho quản trị.
///
/// <para>
/// <b>Người nhận là Admin + Manager ĐANG HOẠT ĐỘNG</b> (xét cả vai trò chính <c>User.RoleId</c> lẫn
/// bảng nối <c>UserRoles</c>, đúng lối RBAC của dự án). Đây là cảnh báo vận hành, không phải thông
/// báo cho khách: hành khách không thấy dòng tiền của cả hệ thống, và tài khoản đã khoá thì không
/// đăng nhập được để đọc cảnh báo.
/// </para>
///
/// <para>
/// <b>Nội dung KHÔNG có số tiền</b> — cùng luật đã chốt cho mọi thông báo (docs/api-contract.md mục
/// "Chín loại thông báo"). Con số tiền phải tra ở chính dòng <c>Refunds</c>; chép vào đây là dựng bản
/// sao thứ hai có thể lệch. Cảnh báo chỉ nêu SỐ ĐIỂM LỆCH, số điểm nghiêm trọng và MÃ khoản hoàn —
/// đủ để người trực mở đúng dòng ở màn quản lý.
/// </para>
///
/// <para>
/// <b>Vì sao ghi theo lô rồi đẩy từng người:</b> tất cả dòng thông báo của một lượt đối soát cùng
/// sống hoặc cùng mất — một <c>SaveChanges</c> cho cả lô, đúng lối <c>Notification</c> là bảng
/// chỉ-ghi-thêm. Đẩy SignalR thì tách riêng từng người và từng khối <c>try</c>: một phiên đang mở
/// hỏng không được làm mất lời nhắc của người khác, và lỗi đẩy không được kéo theo lỗi ghi.
/// </para>
/// </summary>
public sealed class RefundReconciliationNotifier : IRefundReconciliationNotifier
{
    /// <summary>Tiêu đề cảnh báo — dưới trần <c>varchar(200)</c> của cột <c>Notifications.Title</c>.</summary>
    public const string AlertTitle = "Đối soát hoàn tiền phát hiện lệch số liệu";

    /// <summary>
    /// Số mã khoản hoàn tối đa nhét vào body. Nhiều hơn thì body dài mà không thêm thông tin — danh
    /// sách đầy đủ nằm trong log máy chủ, và người trực tra theo từng mã một.
    /// </summary>
    public const int MaxRefundCodesInBody = 10;

    /// <summary>Múi giờ Việt Nam — cùng hằng số cố định đã dùng ở vé/PDF và notifier đổi lịch trình.</summary>
    private const int VietnamUtcOffsetHours = 7;

    private readonly AppDbContext _db;

    private readonly IHubContext<NotificationHub, INotificationClient> _hub;

    private readonly ILogger<RefundReconciliationNotifier> _logger;

    public RefundReconciliationNotifier(
        AppDbContext db,
        IHubContext<NotificationHub, INotificationClient> hub,
        ILogger<RefundReconciliationNotifier> logger)
    {
        _db = db;
        _hub = hub;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<RefundReconciliationNotifyResult> NotifyAsync(RefundReconciliationReport report)
    {
        // Không lệch thì không có gì để nói. Cố ý không ghi log: một dòng "không lệch" mỗi ngày là
        // 365 dòng không nói gì, và service đối soát đã ghi đúng một dòng tổng kết cho lượt đó rồi.
        if (!report.HasMismatch)
        {
            return new RefundReconciliationNotifyResult();
        }

        List<Guid> recipients;
        HashSet<Guid> alreadyNotified;

        try
        {
            recipients = await LoadRecipientsAsync();
            alreadyNotified = await LoadAlreadyNotifiedAsync(report.RunAtUtc);
        }
        catch (Exception ex)
        {
            // Đọc hỏng = không biết báo cho ai. Nuốt lỗi theo hợp đồng giao diện; log đủ để người
            // vận hành biết cảnh báo của đêm đó đã mất và tra báo cáo trong log của service đối soát.
            _logger.LogError(
                ex,
                "Không đọc được danh sách quản trị / lịch sử cảnh báo đối soát — bỏ lượt báo cho {Count} điểm lệch.",
                report.Issues.Count);
            return new RefundReconciliationNotifyResult();
        }

        if (recipients.Count == 0)
        {
            _logger.LogWarning(
                "Đối soát hoàn tiền phát hiện {Count} điểm lệch nhưng không có tài khoản Admin/Manager "
                + "nào đang hoạt động để nhận cảnh báo.",
                report.Issues.Count);
            return new RefundReconciliationNotifyResult();
        }

        var title = AlertTitle;
        var body = BuildBody(report);
        var createdAt = report.RunAtUtc;

        var toCreate = new List<Notification>();
        var skipped = 0;

        foreach (var userId in recipients)
        {
            if (!alreadyNotified.Add(userId))
            {
                // alreadyNotified là HashSet: Add trả false nghĩa là người này đã có cảnh báo trong
                // ngày. Dùng luôn tập đó làm tập "đã báo" cho cả lượt, không cần tập thứ hai.
                skipped++;
                continue;
            }

            toCreate.Add(new Notification
            {
                UserId = userId,
                Type = NotificationType.RefundReconciliationAlert,
                Title = title,
                Body = body,
                // Cảnh báo nói về NHIỀU khoản hoàn, không phải một dòng — con trỏ đa hình để null.
                // Gán một Id đại diện là nói dối về phạm vi của thông báo.
                RelatedEntityType = null,
                RelatedEntityId = null,
                CreatedAt = createdAt,
            });
        }

        if (toCreate.Count == 0)
        {
            _logger.LogInformation(
                "Cảnh báo đối soát ngày {Day:yyyy-MM-dd} đã được gửi trước đó cho toàn bộ {Count} quản trị.",
                createdAt,
                recipients.Count);
            return new RefundReconciliationNotifyResult
            {
                RecipientCount = recipients.Count,
                SkippedCount = skipped,
            };
        }

        try
        {
            _db.Notifications.AddRange(toCreate);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Ghi hỏng: không ai có bản ghi để đọc lại. Nuốt lỗi theo hợp đồng; log số người đáng lẽ
            // được báo để còn dựng lại được cảnh báo từ log của service đối soát.
            _logger.LogError(
                ex,
                "Không ghi được {Count} thông báo cảnh báo đối soát hoàn tiền (ngày {Day:yyyy-MM-dd}).",
                toCreate.Count,
                createdAt);
            return new RefundReconciliationNotifyResult
            {
                RecipientCount = recipients.Count,
                SkippedCount = skipped,
            };
        }

        _logger.LogInformation(
            "Đã ghi {Created} cảnh báo đối soát hoàn tiền cho {Recipients} quản trị (bỏ qua {Skipped} "
            + "người đã nhận trong ngày {Day:yyyy-MM-dd}).",
            toCreate.Count,
            recipients.Count,
            skipped,
            createdAt);

        foreach (var notification in toCreate)
        {
            try
            {
                await _hub.Clients
                    .Group(NotificationHub.GroupOf(notification.UserId))
                    .NotificationReceived(notification.Id);
            }
            catch (Exception ex)
            {
                // Dòng thông báo đã nằm trong CSDL — người nhận vẫn thấy khi mở lại màn thông báo.
                // Mất đúng lời nhắc tức thời không đáng làm hỏng lượt đối soát.
                _logger.LogWarning(
                    ex,
                    "Không đẩy được sự kiện cảnh báo đối soát cho quản trị {UserId} "
                    + "(thông báo {NotificationId} đã ghi).",
                    notification.UserId,
                    notification.Id);
            }
        }

        return new RefundReconciliationNotifyResult
        {
            RecipientCount = recipients.Count,
            CreatedCount = toCreate.Count,
            SkippedCount = skipped,
        };
    }

    /// <summary>
    /// Tài khoản đang hoạt động có vai trò Admin hoặc Manager — xét cả vai trò CHÍNH lẫn bảng nối.
    /// Đọc hai bảng bằng hai truy vấn phẳng rồi lọc trong bộ nhớ, không dùng navigation: cùng lối đã
    /// ghi ở TicketValidationService (provider InMemory của tầng test xử lý Include/projection chạm
    /// navigation khác PostgreSQL).
    /// </summary>
    private async Task<List<Guid>> LoadRecipientsAsync()
    {
        var users = await _db.Users
            .AsNoTracking()
            .Where(u => u.IsActive)
            .Select(u => new { u.Id, u.RoleId })
            .ToListAsync();

        var links = await _db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.RoleId == RoleIds.Admin || ur.RoleId == RoleIds.Manager)
            .Select(ur => new { ur.UserId, ur.RoleId })
            .ToListAsync();

        var linkedUserIds = links.Select(l => l.UserId).ToHashSet();

        return users
            .Where(u => u.RoleId == RoleIds.Admin
                || u.RoleId == RoleIds.Manager
                || linkedUserIds.Contains(u.Id))
            .Select(u => u.Id)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Những người đã nhận cảnh báo đối soát trong NGÀY của lượt chạy. Khoá chống trùng là
    /// (người nhận, loại, ngày) — cùng giới hạn một-bản-app và cùng lý do cố ý KHÔNG đặt unique index
    /// đã ghi đầy đủ ở <see cref="RefundResultNotifier"/>: dư một dòng trùng là thiệt hại nhỏ hơn hẳn
    /// một cảnh báo hợp lệ bị chặn.
    /// </summary>
    private async Task<HashSet<Guid>> LoadAlreadyNotifiedAsync(DateTime runAtUtc)
    {
        var dayStart = runAtUtc.Date;
        var dayEnd = dayStart.AddDays(1);

        var userIds = await _db.Notifications
            .AsNoTracking()
            .Where(n => n.Type == NotificationType.RefundReconciliationAlert
                && n.CreatedAt >= dayStart
                && n.CreatedAt < dayEnd)
            .Select(n => n.UserId)
            .ToListAsync();

        return userIds.ToHashSet();
    }

    /// <summary>
    /// Dựng body cho MỌI người nhận (cùng một lượt đối soát thì cùng nội dung). Không có số tiền —
    /// xem doc-comment lớp. Mốc thời gian theo giờ Việt Nam vì người đọc là quản trị nhà xe, cùng lối
    /// thông báo trễ/đổi chuyến.
    /// </summary>
    private static string BuildBody(RefundReconciliationReport report)
    {
        var runAtVietnam = report.RunAtUtc.AddHours(VietnamUtcOffsetHours);

        var codes = report.Issues
            .Select(i => i.RefundCode)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code!)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxRefundCodesInBody)
            .ToList();

        var totalDistinctCodes = report.Issues
            .Select(i => i.RefundCode)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.Ordinal)
            .Count();

        var codePart = codes.Count == 0
            ? "Không xác định được khoản hoàn nào (lệch phía sao kê cổng)."
            : $"Khoản hoàn liên quan: {string.Join(", ", codes)}"
                + (totalDistinctCodes > codes.Count ? ", …." : ".");

        return $"Lượt đối soát lúc {runAtVietnam:dd/MM/yyyy HH:mm} (giờ Việt Nam) phát hiện "
            + $"{report.Issues.Count} điểm lệch, trong đó {report.CriticalIssueCount} nghiêm trọng. "
            + $"{codePart} Chi tiết từng lỗi nằm trong nhật ký máy chủ.";
    }
}
