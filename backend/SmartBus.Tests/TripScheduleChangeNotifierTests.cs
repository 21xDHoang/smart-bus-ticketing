using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;
using SmartBus.Api.Hubs;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test bộ phận ghi + đẩy thông báo trễ chuyến / đổi lịch trình — <see cref="TripScheduleChangeNotifier"/>
/// (task *"Service thông báo trễ chuyến / thay đổi lịch trình"*, dòng 44 Sprint 4, Phùng Duy Hoàng,
/// thuộc US 10).
///
/// Gọi thẳng service, không dựng host: đầu vào là một chuyến ĐÃ LƯU và ảnh chụp giờ cũ, đầu ra là các
/// dòng <c>Notifications</c> + sự kiện SignalR. Phần "lớp bọc và notifier có thật sự được nối vào app
/// không" nằm ở <see cref="TripScheduleChangeWiringTests"/>; phần "lớp bọc chụp giờ cũ đúng lúc" nằm ở
/// <see cref="NotifyingRouteTripsServiceTests"/>.
///
/// Dùng provider InMemory như phần còn lại của bộ test. Không seed Users/Seats: InMemory không cưỡng
/// chế khoá ngoài, mà notifier chỉ đọc <c>Tickets</c> theo <c>TripId</c>/<c>Status</c> rồi ghi
/// <c>Notifications</c>.
///
/// ⚠️ Khác hai notifier kết quả (huỷ/đổi vé, hoàn tiền), file này cố ý KHÔNG có bài "gọi lại lần hai
/// không sinh dòng thứ hai": notifier này báo theo TỪNG LẦN ĐỔI THẬT. Một chuyến bị dời giờ hai lần là
/// hai sự kiện, khách phải nhận đủ hai — xem <see cref="Hai_lan_doi_gio_lien_tiep_deu_bao"/> và §3 của
/// docs/44-thong-bao-tre-chuyen-doi-lich-trinh.md.
///
/// ⚠️ InMemory cũng KHÔNG cưỡng chế chỉ mục, nên file này xanh không nói gì về chuyện
/// IX_Notifications_UserId_CreatedAt (A6) có tồn tại hay không.
/// </summary>
public class TripScheduleChangeNotifierTests
{
    private static readonly Guid KhachA = Guid.NewGuid();
    private static readonly Guid KhachB = Guid.NewGuid();
    private static readonly Guid KhachC = Guid.NewGuid();

    // 17/10/2026 08:00 UTC = 15:00 giờ Việt Nam — mọi ca dùng chung mốc này để body ghim được cả
    // phép đổi múi giờ, không chỉ phép so giờ.
    private static readonly DateTime CuKhoiHanh = new(2026, 10, 17, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime CuToiDuKien = new(2026, 10, 17, 9, 0, 0, DateTimeKind.Utc);

    // ── Phát đúng người, đúng nội dung ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Tre_gio_ghi_mot_dong_cho_moi_khach_Paid_va_day_dung_nhom()
    {
        await using var db = TaoDb();
        var trip = await SeedChuyenAsync(db);
        var client = new ClientGhiNhan();
        var groups = new List<string>();

        // Khách A mua HAI vé Paid (đi cùng người thân) — vẫn chỉ một dòng thông báo. Khách B một vé.
        // Khách C có vé Used và Cancelled: cả hai đều không còn là "khách đang chờ chuyến này".
        db.Tickets.AddRange(
            Ve(trip.Id, KhachA, TicketStatus.Paid),
            Ve(trip.Id, KhachA, TicketStatus.Paid),
            Ve(trip.Id, KhachB, TicketStatus.Paid),
            Ve(trip.Id, KhachC, TicketStatus.Used),
            Ve(trip.Id, KhachC, TicketStatus.Cancelled));
        await db.SaveChangesAsync();

        trip.DepartureTime = CuKhoiHanh.AddMinutes(30); // 15:30 giờ Việt Nam
        trip.ArrivalTime = CuToiDuKien.AddMinutes(30);  // 16:30 giờ Việt Nam
        await db.SaveChangesAsync();

        await TaoService(db, client, group =>
        {
            groups.Add(group);
            return client;
        }).NotifyAsync(trip.Id, AnhChupCu());

        var thongBao = db.Notifications.OrderBy(n => n.UserId).ToList();

        Assert.Equal(2, thongBao.Count);
        Assert.Equal(
            new[] { KhachA, KhachB }.OrderBy(id => id),
            thongBao.Select(n => n.UserId));
        Assert.DoesNotContain(KhachC, thongBao.Select(n => n.UserId));

        foreach (var dong in thongBao)
        {
            Assert.Equal(NotificationType.TripDelayed, dong.Type);
            Assert.Equal(NotificationTarget.Trip, dong.RelatedEntityType);
            Assert.Equal(trip.Id, dong.RelatedEntityId);
            Assert.Equal("Chuyến xe bị trễ", dong.Title);

            // Nội dung đứng một mình được (A8.5): có tên tuyến và cả hai mốc cũ → mới. Giờ là giờ
            // Việt Nam (UTC+7), KHÔNG phải giờ UTC trong CSDL — 08:30 UTC phải hiện thành 15:30.
            Assert.Equal(
                "Chuyến xe Bến Thành — Chợ Lớn bị trễ. "
                + "Khởi hành: 17/10/2026 15:00 → 17/10/2026 15:30. "
                + "Tới dự kiến: 17/10/2026 16:00 → 17/10/2026 16:30.",
                dong.Body);
            Assert.DoesNotContain("08:30", dong.Body);
        }

        // Nhóm suy từ ID NGƯỜI NHẬN, không từ tham số client gửi lên — cùng NotificationHub.GroupOf
        // mà OnConnectedAsync dùng, nên ghim hình dạng ở đây là ghim hai đầu khớp nhau.
        Assert.Equal(
            new[] { $"user:{KhachA}", $"user:{KhachB}" }.OrderBy(g => g),
            groups.OrderBy(g => g));
        Assert.Equal(
            thongBao.Select(n => n.Id).OrderBy(id => id),
            client.NotificationIds.OrderBy(id => id));
    }

    [Fact]
    public async Task Chi_doi_gio_khoi_hanh_ra_sau_van_la_tre_va_body_chi_neu_moc_doi()
    {
        await using var db = TaoDb();
        var trip = await SeedChuyenAsync(db);
        db.Tickets.Add(Ve(trip.Id, KhachA, TicketStatus.Paid));
        await db.SaveChangesAsync();

        trip.DepartureTime = CuKhoiHanh.AddMinutes(15);
        await db.SaveChangesAsync();

        await TaoService(db).NotifyAsync(trip.Id, AnhChupCu());

        var thongBao = Assert.Single(db.Notifications);

        Assert.Equal(NotificationType.TripDelayed, thongBao.Type);
        Assert.Equal(
            "Chuyến xe Bến Thành — Chợ Lớn bị trễ. "
            + "Khởi hành: 17/10/2026 15:00 → 17/10/2026 15:15.",
            thongBao.Body);
    }

    [Fact]
    public async Task Chi_doi_gio_toi_ra_sau_van_la_tre()
    {
        await using var db = TaoDb();
        var trip = await SeedChuyenAsync(db);
        db.Tickets.Add(Ve(trip.Id, KhachA, TicketStatus.Paid));
        await db.SaveChangesAsync();

        trip.ArrivalTime = CuToiDuKien.AddMinutes(20);
        await db.SaveChangesAsync();

        await TaoService(db).NotifyAsync(trip.Id, AnhChupCu());

        var thongBao = Assert.Single(db.Notifications);

        Assert.Equal(NotificationType.TripDelayed, thongBao.Type);
        Assert.Equal(
            "Chuyến xe Bến Thành — Chợ Lớn bị trễ. "
            + "Tới dự kiến: 17/10/2026 16:00 → 17/10/2026 16:20.",
            thongBao.Body);
    }

    // ── Phân loại: mọi biến thể không phải "lùi giờ" đều là đổi lịch trình ─────────────────────

    [Fact]
    public async Task Doi_gio_khoi_hanh_som_hon_la_doi_lich_trinh()
    {
        await using var db = TaoDb();
        var trip = await SeedChuyenAsync(db);
        db.Tickets.Add(Ve(trip.Id, KhachA, TicketStatus.Paid));
        await db.SaveChangesAsync();

        trip.DepartureTime = CuKhoiHanh.AddMinutes(-10);
        await db.SaveChangesAsync();

        await TaoService(db).NotifyAsync(trip.Id, AnhChupCu());

        var thongBao = Assert.Single(db.Notifications);

        // Chạy SỚM hơn không phải trễ — khách có thể ra bến muộn và mất xe, nhưng gọi nó là "trễ" là
        // nói sai sự thật.
        Assert.Equal(NotificationType.TripScheduleChanged, thongBao.Type);
        Assert.Equal("Lịch trình chuyến xe thay đổi", thongBao.Title);
        Assert.Contains("Khởi hành: 17/10/2026 15:00 → 17/10/2026 14:50.", thongBao.Body);
    }

    [Fact]
    public async Task Doi_hai_chieu_mot_som_mot_muon_la_doi_lich_trinh()
    {
        await using var db = TaoDb();
        var trip = await SeedChuyenAsync(db);
        db.Tickets.Add(Ve(trip.Id, KhachA, TicketStatus.Paid));
        await db.SaveChangesAsync();

        trip.DepartureTime = CuKhoiHanh.AddMinutes(-10);
        trip.ArrivalTime = CuToiDuKien.AddMinutes(15);
        await db.SaveChangesAsync();

        await TaoService(db).NotifyAsync(trip.Id, AnhChupCu());

        var thongBao = Assert.Single(db.Notifications);

        // Có ít nhất một mốc SỚM hơn nên không được gọi là "trễ", dù mốc còn lại ra sau. Hai loại
        // Trip… phải phủ kín mọi lần đổi giờ — rơi vào đây mà im lặng là mất thông báo.
        Assert.Equal(NotificationType.TripScheduleChanged, thongBao.Type);
        Assert.Contains("Khởi hành: 17/10/2026 15:00 → 17/10/2026 14:50.", thongBao.Body);
        Assert.Contains("Tới dự kiến: 17/10/2026 16:00 → 17/10/2026 16:15.", thongBao.Body);
    }

    [Fact]
    public async Task Them_moc_toi_tu_null_la_doi_lich_trinh_du_gio_ra_sau()
    {
        await using var db = TaoDb();
        var trip = await SeedChuyenAsync(db, coMocToi: false);
        db.Tickets.Add(Ve(trip.Id, KhachA, TicketStatus.Paid));
        await db.SaveChangesAsync();

        // Thêm mốc tới là lịch trình đổi HÌNH DẠNG, không phải lùi giờ — dù mốc mới nằm sau giờ khởi
        // hành. So sự hiện diện trước khi so giá trị để null không bị coi là "sớm hơn mọi giờ".
        trip.ArrivalTime = CuToiDuKien;
        await db.SaveChangesAsync();

        await TaoService(db).NotifyAsync(
            trip.Id,
            new TripScheduleSnapshot(CuKhoiHanh, null));

        var thongBao = Assert.Single(db.Notifications);

        Assert.Equal(NotificationType.TripScheduleChanged, thongBao.Type);
        Assert.Contains("Tới dự kiến: (chưa có) → 17/10/2026 16:00.", thongBao.Body);
    }

    [Fact]
    public async Task Bo_moc_toi_thanh_null_la_doi_lich_trinh()
    {
        await using var db = TaoDb();
        var trip = await SeedChuyenAsync(db);
        db.Tickets.Add(Ve(trip.Id, KhachA, TicketStatus.Paid));
        await db.SaveChangesAsync();

        trip.ArrivalTime = null;
        await db.SaveChangesAsync();

        await TaoService(db).NotifyAsync(trip.Id, AnhChupCu());

        var thongBao = Assert.Single(db.Notifications);

        Assert.Equal(NotificationType.TripScheduleChanged, thongBao.Type);
        Assert.Contains("Tới dự kiến: 17/10/2026 16:00 → (chưa có).", thongBao.Body);
    }

    // ── Những lượt PUT không có gì để báo ──────────────────────────────────────────────────────

    [Fact]
    public async Task Khong_doi_gio_thi_im_lang()
    {
        await using var db = TaoDb();
        var trip = await SeedChuyenAsync(db);
        db.Tickets.Add(Ve(trip.Id, KhachA, TicketStatus.Paid));
        await db.SaveChangesAsync();

        // PUT lại đúng dữ liệu cũ, hoặc chỉ đổi xe/ghi chú — giờ không đổi thì không có gì để báo.
        var client = new ClientGhiNhan();
        await TaoService(db, client).NotifyAsync(trip.Id, AnhChupCu());

        Assert.Empty(db.Notifications);
        Assert.Empty(client.NotificationIds);
    }

    [Theory]
    [InlineData(TripStatus.Cancelled)]
    [InlineData(TripStatus.Completed)]
    public async Task Chuyen_da_huy_hoac_da_xong_thi_im_lang(TripStatus status)
    {
        await using var db = TaoDb();
        var trip = await SeedChuyenAsync(db, status: status);
        db.Tickets.Add(Ve(trip.Id, KhachA, TicketStatus.Paid));
        await db.SaveChangesAsync();

        trip.DepartureTime = CuKhoiHanh.AddHours(2);
        await db.SaveChangesAsync();

        var client = new ClientGhiNhan();
        await TaoService(db, client).NotifyAsync(trip.Id, AnhChupCu());

        // Dời giờ một chuyến đã chết là thao tác trên bản ghi không còn ai chờ. Báo "chuyến bị trễ"
        // lúc này là nói sai sự thật. Notifier đọc trạng thái SAU khi lưu: ca này trạng thái cuối
        // vẫn là Cancelled/Completed nên im lặng. (Lượt PUT vừa mở lại chuyến thành Scheduled vừa
        // đổi giờ là chuyện khác — trạng thái cuối là Scheduled nên thông báo có phát; đó là lần mở
        // chuyến kèm dời giờ, không phải ca đang ghim ở đây.)
        Assert.Empty(db.Notifications);
        Assert.Empty(client.NotificationIds);
    }

    [Fact]
    public async Task Khong_co_ve_Paid_thi_im_lang()
    {
        await using var db = TaoDb();
        var trip = await SeedChuyenAsync(db);
        db.Tickets.AddRange(
            Ve(trip.Id, KhachA, TicketStatus.Used),
            Ve(trip.Id, KhachB, TicketStatus.Cancelled));
        await db.SaveChangesAsync();

        trip.DepartureTime = CuKhoiHanh.AddMinutes(5);
        await db.SaveChangesAsync();

        var client = new ClientGhiNhan();
        await TaoService(db, client).NotifyAsync(trip.Id, AnhChupCu());

        // Không còn ai đang chờ chuyến này. Báo cho người đã đi xong / đã huỷ vé là làm nhiễu hộp thư
        // của khách và không ai hành động theo.
        Assert.Empty(db.Notifications);
        Assert.Empty(client.NotificationIds);
    }

    [Fact]
    public async Task Khong_chup_duoc_gio_cu_thi_im_lang_khong_doan()
    {
        await using var db = TaoDb();
        var trip = await SeedChuyenAsync(db);
        db.Tickets.Add(Ve(trip.Id, KhachA, TicketStatus.Paid));
        await db.SaveChangesAsync();

        trip.DepartureTime = CuKhoiHanh.AddMinutes(30);
        await db.SaveChangesAsync();

        var client = new ClientGhiNhan();
        await TaoService(db, client).NotifyAsync(trip.Id, previous: null);

        // Ảnh chụp null = không biết trước đó chạy giờ nào. Không có mốc cũ thì không phân loại được
        // trễ hay đổi lịch trình — im lặng, không đoán.
        Assert.Empty(db.Notifications);
        Assert.Empty(client.NotificationIds);
    }

    [Fact]
    public async Task Chuyen_khong_con_trong_csdl_thi_im_lang()
    {
        await using var db = TaoDb();
        var client = new ClientGhiNhan();

        var loi = await Record.ExceptionAsync(
            () => TaoService(db, client).NotifyAsync(Guid.NewGuid(), AnhChupCu()));

        Assert.Null(loi);
        Assert.Empty(db.Notifications);
        Assert.Empty(client.NotificationIds);
    }

    // ── Sự kiện lặp: KHÔNG có chốt đọc-trước-khi-ghi ──────────────────────────────────────────

    [Fact]
    public async Task Hai_lan_doi_gio_lien_tiep_deu_bao()
    {
        await using var db = TaoDb();
        var trip = await SeedChuyenAsync(db);
        db.Tickets.Add(Ve(trip.Id, KhachA, TicketStatus.Paid));
        await db.SaveChangesAsync();

        var client = new ClientGhiNhan();
        var service = TaoService(db, client, _ => client);

        // Lần 1: 15:00 → 15:30, ảnh chụp cũ 15:00.
        trip.DepartureTime = CuKhoiHanh.AddMinutes(30);
        await db.SaveChangesAsync();
        await service.NotifyAsync(trip.Id, AnhChupCu());

        // Lần 2: 15:30 → 16:00, ảnh chụp cũ 15:30. Cùng MỘT dòng Trips, cùng người nhận, cùng loại.
        trip.DepartureTime = CuKhoiHanh.AddMinutes(60);
        await db.SaveChangesAsync();
        await service.NotifyAsync(trip.Id, new TripScheduleSnapshot(CuKhoiHanh.AddMinutes(30), CuToiDuKien));

        // Hai sự kiện thì hai thông báo. Nếu ai đó "tối ưu" bằng phép đọc-trước-khi-ghi theo
        // (người nhận, loại, dòng nghiệp vụ) như hai notifier kết quả, lần dời thứ hai sẽ bị nuốt —
        // khách giữ mốc 15:30 trong hộp thư trong khi xe chạy 16:00.
        Assert.Equal(2, db.Notifications.Count());
        Assert.All(db.Notifications, n => Assert.Equal(NotificationType.TripDelayed, n.Type));
        Assert.Equal(2, client.NotificationIds.Count);
        Assert.Contains("Khởi hành: 17/10/2026 15:30 → 17/10/2026 16:00.", db.Notifications.Last().Body);
    }

    // ── Hợp đồng "không bao giờ ném lỗi" ───────────────────────────────────────────────────────

    [Fact]
    public async Task Ken_duy_hong_khong_nem_loi_va_dong_van_duoc_ghi()
    {
        await using var db = TaoDb();
        var trip = await SeedChuyenAsync(db);
        db.Tickets.Add(Ve(trip.Id, KhachA, TicketStatus.Paid));
        await db.SaveChangesAsync();

        trip.DepartureTime = CuKhoiHanh.AddMinutes(15);
        await db.SaveChangesAsync();

        var service = TaoService(db, new ClientGhiNhan(),
            _ => throw new InvalidOperationException("kênh đẩy hỏng"));

        var loi = await Record.ExceptionAsync(
            () => service.NotifyAsync(trip.Id, AnhChupCu()));

        // Người gọi đang ở cuối một lượt PUT ĐÃ LƯU THÀNH CÔNG. Để lỗi kênh đẩy nổi lên là quản lý
        // thấy lượt sửa báo 500 dù giờ đã đổi thật — và có thể sửa lại lần nữa.
        Assert.Null(loi);

        // Dòng vẫn phải còn: mất lời nhắc tức thời là chuyện nhỏ, mất bản ghi để khách đọc lại khi
        // offline (A8.5) mới là chuyện lớn.
        Assert.Single(db.Notifications);
    }

    [Fact]
    public async Task Ghi_hong_khong_nem_loi()
    {
        var db = TaoDb();
        var service = TaoService(db);

        // DbContext đã bị dispose → mọi lượt đọc/ghi đều ném ObjectDisposedException. Cách rẻ nhất để
        // dựng nhánh "CSDL hỏng" mà không cần provider biết ném lỗi.
        await db.DisposeAsync();

        var loi = await Record.ExceptionAsync(
            () => service.NotifyAsync(Guid.NewGuid(), AnhChupCu()));

        Assert.Null(loi);
    }

    // ── Hạ tầng dùng chung của lớp test ────────────────────────────────────────────────────────

    private static AppDbContext TaoDb()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"thong-bao-tre-chuyen-{Guid.NewGuid()}")
            .Options);

    private static TripScheduleSnapshot AnhChupCu() => new(CuKhoiHanh, CuToiDuKien);

    private static TripScheduleChangeNotifier TaoService(
        AppDbContext db,
        ClientGhiNhan? client = null,
        Func<string, INotificationClient>? onGroup = null)
        => new(
            db,
            new HubContextGia(group =>
            {
                client ??= new ClientGhiNhan();
                return onGroup?.Invoke(group) ?? client;
            }),
            NullLogger<TripScheduleChangeNotifier>.Instance);

    /// <summary>
    /// Một tuyến + một chuyến đủ để notifier đọc được tên tuyến và hai mốc giờ. Không seed Xe: notifier
    /// không đọc quan hệ đó.
    /// </summary>
    private static async Task<Trip> SeedChuyenAsync(
        AppDbContext db,
        TripStatus status = TripStatus.Scheduled,
        bool coMocToi = true)
    {
        var route = new Route
        {
            Code = "01",
            Name = "Bến Thành — Chợ Lớn",
            Origin = "Bến Thành",
            Destination = "Chợ Lớn",
        };

        var trip = new Trip
        {
            RouteId = route.Id,
            Route = route,
            BusId = Guid.NewGuid(),
            DepartureTime = CuKhoiHanh,
            ArrivalTime = coMocToi ? CuToiDuKien : null,
            Status = status,
        };

        db.Trips.Add(trip);
        await db.SaveChangesAsync();
        return trip;
    }

    private static Ticket Ve(Guid tripId, Guid userId, TicketStatus status)
        => new()
        {
            Code = Guid.NewGuid().ToString(),
            TripId = tripId,
            SeatId = Guid.NewGuid(),
            UserId = userId,
            Status = status,
        };

    private sealed class ClientGhiNhan : INotificationClient
    {
        public List<Guid> NotificationIds { get; } = [];

        public Task NotificationReceived(Guid notificationId)
        {
            NotificationIds.Add(notificationId);
            return Task.CompletedTask;
        }
    }

    private sealed class HubContextGia : IHubContext<NotificationHub, INotificationClient>
    {
        public HubContextGia(Func<string, INotificationClient> group) => Clients = new KhachGia(group);

        public IHubClients<INotificationClient> Clients { get; }

        public IGroupManager Groups => throw new NotSupportedException();

        private sealed class KhachGia : IHubClients<INotificationClient>
        {
            private readonly Func<string, INotificationClient> _group;

            public KhachGia(Func<string, INotificationClient> group) => _group = group;

            public INotificationClient Group(string groupName) => _group(groupName);

            // Tám thành viên còn lại không dùng tới trong các ca ở đây — ném để lộ ngay nếu có người lỡ gọi.
            public INotificationClient All => throw new NotSupportedException();

            public INotificationClient AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();

            public INotificationClient Client(string connectionId) => throw new NotSupportedException();

            public INotificationClient Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();

            public INotificationClient GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();

            public INotificationClient Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();

            public INotificationClient User(string userId) => throw new NotSupportedException();

            public INotificationClient Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
        }
    }
}
