using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;
using SmartBus.Api.Hubs;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="ITicketRequestResultNotifier"/> — task Sprint 4 *"Ghi log và thông báo kết quả
/// xử lý cho hành khách"* (Vàng Thị Dăm, dòng 7). Xem interface để biết hai chốt hợp đồng: không bao
/// giờ ném lỗi, và không nhận <c>CancellationToken</c>.
///
/// <para>
/// <b>Vì sao dùng <see cref="IHubContext{THub,TClient}"/> chứ không phải Hub:</b> người gọi là quản lý
/// đang gọi API duyệt yêu cầu, còn người nhận là PHIÊN KHÁC (của hành khách). IHubContext mới là cửa
/// để đẩy sự kiện tới client của người khác — cùng lối <see cref="SeatMapNotifier"/>.
/// </para>
///
/// <para>
/// <b>Vì sao nội dung không có số tiền:</b> hai lý do, cả hai đều có thật. (1) Số tiền hoàn chưa phải
/// chuyện đã xong — nó do bảng <c>Refunds</c> (US 8, một sprint khác) thực hiện, có thể thất bại rồi
/// thử lại. Thông báo ghi "đã hoàn 70.000đ" khi lượt hoàn còn chưa chạy là hứa trước một việc hệ
/// thống chưa làm. (2) Định dạng tiền là việc của frontend: nhét một chuỗi "70.000 đ" vào CSDL là
/// đóng đinh định dạng và ngôn ngữ vào dữ liệu, trong khi màn theo dõi yêu cầu (Nguyễn Đình Băng,
/// dòng 10) đã hiển thị con số đó từ chính dòng yêu cầu.
/// </para>
///
/// <para>
/// ⚠️ <b>Về vế "ghi log" của tên task:</b> KHÔNG có dòng <c>AuditLogs</c> nào được ghi ở đây, và đó là
/// chủ ý. <see cref="AuditLogMiddleware"/> đã tự ghi một dòng <c>Update</c> với
/// <c>Target = "CancellationRequests:{id}"</c> (hoặc <c>"TicketChanges:{id}"</c>) cho chính lượt PATCH
/// mà quản lý vừa gọi, kèm người thực hiện và IP — ghi thêm ở đây là mỗi lượt duyệt sinh hai dòng
/// nhật ký cho một sự việc, làm loãng màn kiểm toán của Admin (US 23). Còn
/// <c>AuditLogs.UserId</c> là người THỰC HIỆN: điền id hành khách vào đó là ghi sai sự thật.
/// Phần "log" của task nằm ở: dòng <see cref="Notification"/> (bản ghi bền, trả lời được câu "hệ thống
/// đã báo gì cho khách, lúc nào") + log ứng dụng có cấu trúc ở cả ba nhánh (thành công, kênh đẩy hỏng,
/// ghi hỏng).
/// </para>
/// </summary>
public sealed class TicketRequestResultNotifier : ITicketRequestResultNotifier
{
    private const string CancellationApprovedTitle = "Đã duyệt yêu cầu huỷ vé";

    private const string CancellationRejectedTitle = "Từ chối yêu cầu huỷ vé";

    private const string TicketChangeApprovedTitle = "Đã duyệt yêu cầu đổi chuyến";

    private const string TicketChangeRejectedTitle = "Từ chối yêu cầu đổi chuyến";

    private const string CancellationApprovedBody
        = "Yêu cầu huỷ vé của bạn đã được duyệt. Nhà xe sẽ xử lý hoàn tiền theo quy định.";

    private const string TicketChangeApprovedBody
        = "Yêu cầu đổi chuyến của bạn đã được duyệt. Vé mới của bạn đã được phát hành.";

    /// <summary>Phần đuôi chung của hai nhánh từ chối — dùng khi quản lý không nêu lý do.</summary>
    private const string NoReasonSuffix = " Người duyệt không nêu lý do.";

    private readonly AppDbContext _db;

    private readonly IHubContext<NotificationHub, INotificationClient> _hub;

    private readonly ILogger<TicketRequestResultNotifier> _logger;

    public TicketRequestResultNotifier(
        AppDbContext db,
        IHubContext<NotificationHub, INotificationClient> hub,
        ILogger<TicketRequestResultNotifier> logger)
    {
        _db = db;
        _hub = hub;
        _logger = logger;
    }

    public async Task NotifyCancellationReviewedAsync(CancellationRequest request)
    {
        if (request.Status == CancellationRequestStatus.Pending)
        {
            _logger.LogWarning(
                "Yêu cầu huỷ {RequestId} còn ở trạng thái Pending nên không có kết quả nào để báo cho khách "
                + "{UserId}. Chỗ gọi phải lật trạng thái và lưu TRƯỚC khi gọi hàm này.",
                request.Id,
                request.UserId);
            return;
        }

        // Trạng thái là Approved hoặc Rejected — enum chỉ có ba giá trị, nên "không Pending" đã là
        // Approved. Viết thẳng phép so sánh thay vì else: nếu sau này enum thêm giá trị thứ tư, câu
        // này đọc ra "Rejected" cho giá trị mới thay vì âm thầm rơi vào nhánh duyệt.
        var approved = request.Status == CancellationRequestStatus.Approved;

        var body = approved
            ? CancellationApprovedBody
            : "Yêu cầu huỷ vé của bạn đã bị từ chối." + ReasonSuffix(request.RejectReason);

        await NotifyAsync(
            request.UserId,
            approved ? NotificationType.CancellationApproved : NotificationType.CancellationRejected,
            NotificationTarget.CancellationRequest,
            request.Id,
            approved ? CancellationApprovedTitle : CancellationRejectedTitle,
            body);
    }

    public async Task NotifyTicketChangeReviewedAsync(TicketChange request)
    {
        if (request.Status == TicketChangeStatus.Pending)
        {
            _logger.LogWarning(
                "Yêu cầu đổi {RequestId} còn ở trạng thái Pending nên không có kết quả nào để báo cho khách "
                + "{UserId}. Chỗ gọi phải lật trạng thái và lưu TRƯỚC khi gọi hàm này.",
                request.Id,
                request.UserId);
            return;
        }

        var approved = request.Status == TicketChangeStatus.Approved;

        var body = approved
            ? TicketChangeApprovedBody
            : "Yêu cầu đổi chuyến của bạn đã bị từ chối." + ReasonSuffix(request.RejectReason);

        await NotifyAsync(
            request.UserId,
            approved ? NotificationType.TicketChangeApproved : NotificationType.TicketChangeRejected,
            NotificationTarget.TicketChange,
            request.Id,
            approved ? TicketChangeApprovedTitle : TicketChangeRejectedTitle,
            body);
    }

    /// <summary>Đuôi nội dung mang lý do từ chối do quản lý nhập — chép nguyên văn, chỉ cắt khoảng trắng thừa.</summary>
    private static string ReasonSuffix(string? rejectReason)
        => string.IsNullOrWhiteSpace(rejectReason)
            ? NoReasonSuffix
            : $" Lý do: {rejectReason.Trim()}";

    /// <summary>
    /// Ghi dòng thông báo rồi đẩy sự kiện. Hai bước nằm ở hai khối <c>try</c> RIÊNG vì hai thất bại
    /// này có hậu quả khác hẳn nhau: ghi hỏng nghĩa là khách không có gì để đọc lại (mất thông báo
    /// thật), còn đẩy hỏng chỉ nghĩa là phiên đang mở không được nhắc (khách vẫn thấy khi mở lại màn
    /// thông báo). Gộp một khối thì hai chuyện đó ra cùng một dòng log.
    /// </summary>
    private async Task NotifyAsync(
        Guid recipientId,
        NotificationType type,
        NotificationTarget target,
        Guid relatedEntityId,
        string title,
        string body)
    {
        Notification notification;

        try
        {
            // Chống ghi trùng: một quyết định chỉ được báo MỘT lần. Không có chốt này thì một lượt gọi
            // lại (endpoint retry, hoặc lớp bọc gọi hai lần) sinh dòng thứ hai, và khách thấy hai thông
            // báo cho cùng một kết quả.
            //
            // ⚠️ Đây là phép ĐỌC TRƯỚC KHI GHI nên chỉ đúng khi chạy MỘT bản app — cùng giới hạn đã ghi
            // ở docs/26 cho phép chống ghi trùng của job cảnh báo giữ chỗ. Cố ý KHÔNG đặt unique index
            // ở tầng CSDL: khoá đó sẽ là (UserId, Type, RelatedEntityId), mà Sprint 5 sẽ có loại thông
            // báo được phép lặp trên cùng một bản ghi (xe trễ chuyến hai lần cho cùng một chuyến) —
            // một unique index dựng bây giờ sẽ chặn nhầm nghiệp vụ chưa viết. Dư một dòng trùng là
            // thiệt hại nhỏ hơn hẳn một thông báo hợp lệ bị chặn.
            var alreadyNotified = await _db.Notifications
                .AsNoTracking()
                .AnyAsync(n => n.UserId == recipientId && n.Type == type && n.RelatedEntityId == relatedEntityId);

            if (alreadyNotified)
            {
                _logger.LogInformation(
                    "Yêu cầu {RelatedEntityId} đã được báo cho khách {UserId} ({Type}) — bỏ qua lượt gọi lại.",
                    relatedEntityId,
                    recipientId,
                    type);
                return;
            }

            notification = new Notification
            {
                UserId = recipientId,
                Type = type,
                RelatedEntityType = target,
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
            // Nuốt lỗi có chủ ý — xem ITicketRequestResultNotifier. Ghi lại đủ chi tiết để còn dựng lại
            // được thông báo đã mất: người nhận, loại, và bản ghi mà nó nói về.
            _logger.LogError(
                ex,
                "Không ghi được thông báo {Type} cho khách {UserId} về yêu cầu {RelatedEntityId}.",
                type,
                recipientId,
                relatedEntityId);
            return;
        }

        _logger.LogInformation(
            "Đã ghi thông báo {Type} cho khách {UserId} về yêu cầu {RelatedEntityId}.",
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
            // Cùng hợp đồng nuốt lỗi như SeatMapNotifier: dòng thông báo đã nằm trong CSDL, nên khách
            // vẫn đọc được khi mở lại màn thông báo. Mất đúng lời nhắc tức thời thì không đáng làm hỏng
            // lượt duyệt đã thành công.
            _logger.LogWarning(
                ex,
                "Không đẩy được sự kiện NotificationReceived cho khách {UserId} (thông báo {NotificationId} đã ghi).",
                recipientId,
                notification.Id);
        }
    }
}
