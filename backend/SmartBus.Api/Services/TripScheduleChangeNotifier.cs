using System.Globalization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;
using SmartBus.Api.Hubs;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="ITripScheduleChangeNotifier"/> — task Sprint 4 *"Service thông báo trễ chuyến /
/// thay đổi lịch trình"* (dòng 44, Phùng Duy Hoàng). Xem interface để biết hai chốt hợp đồng: không
/// bao giờ ném lỗi, và không nhận <c>CancellationToken</c>.
///
/// <para>
/// <b>Báo theo TỪNG LẦN ĐỔI THẬT, không theo lượt PUT:</b> phán quyết dựa trên phép so ảnh chụp
/// <c>previous</c> (lấy trước khi sửa) với giờ ĐÃ LƯU. Chỉ đổi xe, chỉ đổi <c>status</c>, hay PUT lại
/// đúng dữ liệu cũ đều không làm giờ đổi nên không phát gì. Một chuyến bị dời giờ hai lần là HAI sự
/// kiện — khách nhận cả hai — nên notifier này cố ý KHÔNG có phép đọc-trước-khi-ghi như
/// <see cref="TicketRequestResultNotifier"/> và <see cref="RefundResultNotifier"/>: chốt đó khoá theo
/// (người nhận, loại, dòng nghiệp vụ) và sẽ nuốt mất lần dời thứ hai vì cả hai lần cùng trỏ về một
/// dòng <c>Trips</c>.
/// </para>
///
/// <para>
/// <b>Hai loại, phủ kín mọi lần đổi giờ:</b>
/// <list type="bullet">
/// <item><see cref="NotificationType.TripDelayed"/> — mọi mốc đổi đều LÙI RA SAU, và
/// <see cref="Trip.ArrivalTime"/> không đổi giữa null và có giá trị (nếu đổi thì đó là đổi lịch trình,
/// không phải trễ);</item>
/// <item><see cref="NotificationType.TripScheduleChanged"/> — mọi trường hợp còn lại có giờ đổi: có mốc
/// SỚM hơn, hoặc đổi cả hai chiều, hoặc mốc tới được thêm/bỏ.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Chuyến đã huỷ hoặc đã chạy xong thì im lặng:</b> dời giờ một chuyến <see cref="TripStatus.Cancelled"/>
/// là thao tác trên bản ghi đã chết — báo "chuyến bị trễ" cho khách lúc này là nói sai sự thật; chuyến
/// <see cref="TripStatus.Completed"/> cũng vậy. Chỉ <see cref="TripStatus.Scheduled"/> và
/// <see cref="TripStatus.Running"/> mới có khách còn chờ xe.
/// </para>
///
/// <para>
/// ⚠️ <b>Huỷ chuyến bằng <c>DELETE /routes/{routeId}/trips/{id}</c> KHÔNG đi qua đây:</b> đó là nghiệp
/// vụ huỷ chuyến, không phải trễ/đổi lịch trình, và bảng phân công không có dòng nào cho thông báo huỷ
/// chuyến. Đổi <c>status</c> sang <see cref="TripStatus.Cancelled"/> bằng PUT cũng im lặng — không phải
/// một lần đổi giờ. Cùng lý do, thay đổi giá vé hay sơ đồ ghế không thuộc phạm vi thông báo này.
/// </para>
///
/// <para>
/// <b>Nội dung không có số tiền</b> (cùng luật với các notifier khác): có tên tuyến và mốc cũ → mốc
/// mới của đúng những mốc đã đổi, để thông báo đọc đứng một mình được (A8.5) mà không chép dữ liệu
/// doanh thu. Giờ hiển thị theo giờ Việt Nam (UTC+7) — cùng lối cố định
/// <c>VietnamUtcOffsetHours</c> của vé/PDF, không dùng <c>TimeZoneInfo.FindSystemTimeZoneById</c> vì
/// id vùng khác nhau giữa Windows và Linux (CI chạy ubuntu).
/// </para>
/// </summary>
public sealed class TripScheduleChangeNotifier : ITripScheduleChangeNotifier
{
    private const string TripDelayedTitle = "Chuyến xe bị trễ";

    private const string TripScheduleChangedTitle = "Lịch trình chuyến xe thay đổi";

    private const string ArrivalHasNoValue = "(chưa có)";

    /// <summary>Việt Nam không có giờ mùa hè — cố định UTC+7 (cùng lối vé/PDF).</summary>
    private static readonly TimeSpan VietnamUtcOffset = TimeSpan.FromHours(7);

    private readonly AppDbContext _db;

    private readonly IHubContext<NotificationHub, INotificationClient> _hub;

    private readonly ILogger<TripScheduleChangeNotifier> _logger;

    public TripScheduleChangeNotifier(
        AppDbContext db,
        IHubContext<NotificationHub, INotificationClient> hub,
        ILogger<TripScheduleChangeNotifier> logger)
    {
        _db = db;
        _hub = hub;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task NotifyAsync(Guid tripId, TripScheduleSnapshot? previous)
    {
        try
        {
            await NotifyCoreAsync(tripId, previous);
        }
        catch (Exception ex)
        {
            // Chốt hợp đồng: không bao giờ ném lỗi — xem ITripScheduleChangeNotifier. Lượt PUT đã lưu
            // thành công trước khi vào đây, nên không có lỗi phụ trợ nào được phép làm hỏng nó.
            _logger.LogError(
                ex,
                "Không báo được thay đổi lịch trình chuyến {TripId} — bỏ qua thông báo.",
                tripId);
        }
    }

    private async Task NotifyCoreAsync(Guid tripId, TripScheduleSnapshot? previous)
    {
        if (previous is null)
        {
            // Lớp bọc không chụp được giờ cũ (CSDL lỗi tạm thời) hoặc chuyến không tồn tại trước lượt
            // sửa. Không có mốc cũ thì không phân loại được — im lặng, không đoán.
            _logger.LogInformation(
                "Không có giờ chạy cũ của chuyến {TripId} để so — bỏ qua thông báo đổi lịch trình.",
                tripId);
            return;
        }

        // Đọc lại trạng thái ĐÃ LƯU sau lượt PUT: ảnh chụp cũ chỉ nói trước đó là gì, còn đây mới là
        // thứ khách sắp gặp. AsNoTracking vì notifier chỉ đọc, không ghi gì vào dòng Trips.
        var trip = await _db.Trips
            .AsNoTracking()
            .Include(t => t.Route)
            .FirstOrDefaultAsync(t => t.Id == tripId);

        if (trip is null || trip.Route is null)
        {
            _logger.LogWarning(
                "Chuyến {TripId} không còn trong CSDL ở bước báo đổi lịch trình — bỏ qua thông báo.",
                tripId);
            return;
        }

        if (trip.Status is not (TripStatus.Scheduled or TripStatus.Running))
        {
            // Chuyến đã huỷ / đã chạy xong: không có hành khách nào còn chờ chuyến này. Cố ý không ghi
            // log — mọi lượt sửa chuyến cũ đều rơi vào nhánh này.
            return;
        }

        var departureChanged = trip.DepartureTime != previous.DepartureTime;
        var arrivalChanged = trip.ArrivalTime != previous.ArrivalTime;

        if (!departureChanged && !arrivalChanged)
        {
            // Chỉ đổi xe / chỉ đổi status, hoặc PUT lại đúng dữ liệu cũ. Im lặng có chủ ý.
            return;
        }

        var type = IsDelay(previous, trip)
            ? NotificationType.TripDelayed
            : NotificationType.TripScheduleChanged;

        var title = type == NotificationType.TripDelayed
            ? TripDelayedTitle
            : TripScheduleChangedTitle;

        var body = BuildBody(previous, trip, type);

        var recipientIds = await _db.Tickets
            .AsNoTracking()
            .Where(t => t.TripId == tripId && t.Status == TicketStatus.Paid)
            .Select(t => t.UserId)
            .Distinct()
            .ToListAsync();

        if (recipientIds.Count == 0)
        {
            // Chuyến chưa ai mua vé (hoặc mọi vé đã Used/Cancelled) — không có ai để báo.
            return;
        }

        // Ghi TẤT CẢ dòng thông báo trong MỘT lượt SaveChanges: hoặc cả nhóm nhận được, hoặc không ai
        // nhận — tránh nửa tá khách có thông báo còn nửa kia thì không chỉ vì CSDL chập chờn giữa vòng
        // lặp. Không có phép đọc-trước-khi-ghi (xem doc-comment lớp): mỗi lần đổi thật là một sự kiện.
        var notifications = new List<Notification>(recipientIds.Count);

        foreach (var recipientId in recipientIds)
        {
            notifications.Add(new Notification
            {
                UserId = recipientId,
                Type = type,
                RelatedEntityType = NotificationTarget.Trip,
                RelatedEntityId = tripId,
                Title = title,
                Body = body,
                CreatedAt = DateTime.UtcNow,
            });
        }

        try
        {
            _db.Notifications.AddRange(notifications);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Không ghi được {Count} thông báo {Type} về chuyến {TripId}.",
                notifications.Count,
                type,
                tripId);
            return;
        }

        _logger.LogInformation(
            "Đã ghi {Count} thông báo {Type} về chuyến {TripId}.",
            notifications.Count,
            type,
            tripId);

        foreach (var notification in notifications)
        {
            try
            {
                // Nhóm suy ra từ ID NGƯỜI NHẬN, không từ tham số client gửi lên — xem NotificationHub.
                await _hub.Clients
                    .Group(NotificationHub.GroupOf(notification.UserId))
                    .NotificationReceived(notification.Id);
            }
            catch (Exception ex)
            {
                // Dòng đã nằm trong CSDL nên khách vẫn đọc lại được; mất lời nhắc tức thời của MỘT
                // người không được chặn những người còn lại.
                _logger.LogWarning(
                    ex,
                    "Không đẩy được sự kiện NotificationReceived cho khách {UserId} "
                    + "(thông báo {NotificationId} đã ghi).",
                    notification.UserId,
                    notification.Id);
            }
        }
    }

    /// <summary>
    /// "Lùi giờ" = mọi mốc ĐỔI đều ra sau, và mốc tới không đổi giữa null và có giá trị. Hàm chỉ được
    /// gọi khi đã biết có ít nhất một mốc đổi, nên kết quả false nghĩa là
    /// <see cref="NotificationType.TripScheduleChanged"/>.
    /// </summary>
    private static bool IsDelay(TripScheduleSnapshot previous, Trip trip)
    {
        var departureEarlier = trip.DepartureTime < previous.DepartureTime;

        // Thêm hoặc bỏ mốc tới không phải "trễ" — lịch trình đổi hình dạng. So sự HIỆN DIỆN trước khi
        // so giá trị để null không bị coi là mốc sớm hơn mọi giờ.
        var arrivalPresenceChanged = (previous.ArrivalTime is null) != (trip.ArrivalTime is null);

        var arrivalEarlier = previous.ArrivalTime is { } oldArrival
                             && trip.ArrivalTime is { } newArrival
                             && newArrival < oldArrival;

        return !departureEarlier && !arrivalEarlier && !arrivalPresenceChanged;
    }

    private static string BuildBody(TripScheduleSnapshot previous, Trip trip, NotificationType type)
    {
        var opening = type == NotificationType.TripDelayed
            ? $"Chuyến xe {trip.Route!.Name} bị trễ."
            : $"Lịch trình chuyến xe {trip.Route!.Name} đã thay đổi.";

        var parts = new List<string> { opening };

        if (trip.DepartureTime != previous.DepartureTime)
        {
            parts.Add(
                $"Khởi hành: {FormatTime(previous.DepartureTime)} → {FormatTime(trip.DepartureTime)}.");
        }

        if (trip.ArrivalTime != previous.ArrivalTime)
        {
            parts.Add(
                $"Tới dự kiến: {FormatArrival(previous.ArrivalTime)} → {FormatArrival(trip.ArrivalTime)}.");
        }

        return string.Join(" ", parts);
    }

    private static string FormatArrival(DateTime? arrival)
    {
        return arrival is null ? ArrivalHasNoValue : FormatTime(arrival.Value);
    }

    /// <summary>
    /// Mọi giá trị trong CSDL đều là UTC (<c>timestamptz</c> — A3); đổi sang UTC+7 để khách đọc đúng
    /// giờ Việt Nam, cùng lối vé/PDF.
    /// </summary>
    private static string FormatTime(DateTime utc)
    {
        return utc
            .Add(VietnamUtcOffset)
            .ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
    }
}
