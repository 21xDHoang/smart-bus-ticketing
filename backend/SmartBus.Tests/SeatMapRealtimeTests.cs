using System.Net;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.SeatHolds;
using SmartBus.Api.Entities;
using SmartBus.Api.Hubs;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Đồng bộ trạng thái ghế realtime giữa các phiên (SignalR) — task cùng tên, dòng 11 Sprint 3
/// (Hoàng Văn Thịnh), phục vụ US 2 "Là hành khách, tôi muốn xem sơ đồ xe và chọn vị trí ghế còn
/// trống để có chỗ ngồi mong muốn".
///
/// Kiến trúc đã chốt ở docs/api-contract.md, mục "Đồng bộ ghế realtime — /hubs/seat-map": sự kiện
/// đi theo CHUYẾN ("sơ đồ ghế của chuyến X vừa đổi"), client nhận rồi tự gọi lại
/// GET /trips/{id}/seats và tự diff — hub không bao giờ là nguồn dữ liệu thứ hai.
///
/// Bốn nhóm bài ở đây:
///   1. Hub sống trong pipeline THẬT (TestAppFactory chạy nguyên Program.cs): negotiate mở được cho
///      khách chưa đăng nhập, và negotiate KHÔNG sinh dòng nhật ký giả — bài kiểm hồi quy cho dòng
///      <c>new("/hubs")</c> trong AuditLogMiddleware.IgnoredPaths.
///   2. Ghim chỗ NỐI ở Program.cs: ba interface phải trỏ vào ba lớp bọc Notifying*, không phải
///      service trần — đảo thứ tự đăng ký là lớp bọc biến mất trong im lặng, mọi thứ vẫn biên dịch.
///   3. Ba lớp bọc, chạy với SERVICE THẬT + InMemory: các service thật có phương thức non-virtual
///      và ctor chỉ nhận AppDbContext nên không thể giả bằng lớp con — dùng đồ thật, gắn một
///      notifier ghi nhận thay cho kênh SignalR.
///   4. SeatMapNotifier: đẩy đúng nhóm <c>trip:{id}</c>, và nuốt lỗi kênh đẩy theo hợp đồng của
///      ISeatMapNotifier (lời nhắc realtime là phụ trợ, không được làm hỏng thao tác nghiệp vụ).
/// </summary>
public class SeatMapRealtimeTests
{
    // ── Nhóm 1: hub trong pipeline thật ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_negotiate_hop_dong_tra_200_cho_khach_chua_dang_nhap()
    {
        using var factory = new TestAppFactory();

        // Hub không gắn [Authorize]: người XEM sơ đồ ghế chưa chắc đã đăng nhập — chỉ lúc GIỮ ghế
        // mới cần tài khoản. Chặn ở đây là chặn luôn bước đầu của cả luồng US 2.
        var response = await factory.CreateClient()
            .PostAsync("/hubs/seat-map/negotiate?negotiateVersion=1", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Post_negotiate_khong_sinh_dong_nhat_ky_gia()
    {
        using var factory = new TestAppFactory();

        var response = await factory.CreateClient()
            .PostAsync("/hubs/seat-map/negotiate?negotiateVersion=1", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // negotiate là POST 2xx nên rơi đúng vào tầm ngắm của AuditLogMiddleware. Không có dòng nào
        // trong IgnoredPaths chặn /hubs thì MỖI lượt khách mở màn chọn ghế sinh một dòng Create giả
        // với Target "Negotiate" — một bảng không tồn tại. Đây là bài kiểm hồi quy cho dòng
        // `new("/hubs")`; bài này TỪNG ĐỎ trước khi dòng đó được thêm vào.
        Assert.Empty(await LogsAsync(factory));
    }

    // ── Nhóm 2: ghim chỗ nối ở Program.cs ────────────────────────────────────────────────────────

    [Fact]
    public void Ba_lop_boc_va_notifier_duoc_dang_ky_dung_cho_interface()
    {
        using var factory = new TestAppFactory();
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;

        // Đăng ký interface bằng factory "bản ghi sau thắng": ai đó đảo thứ tự trong Program.cs là
        // lớp bọc biến mất trong im lặng — mọi thứ vẫn biên dịch, mọi endpoint vẫn 200, chỉ có sự
        // kiện realtime ngừng phát. Bốn dòng này bắt đúng ca đó.
        Assert.IsType<NotifyingSeatHoldCreateService>(services.GetRequiredService<ISeatHoldCreateService>());
        Assert.IsType<NotifyingSeatHoldReleaseService>(services.GetRequiredService<ISeatHoldReleaseService>());
        Assert.IsType<NotifyingSeatHoldExpiryService>(services.GetRequiredService<ISeatHoldExpiryService>());
        Assert.IsType<SeatMapNotifier>(services.GetRequiredService<ISeatMapNotifier>());
    }

    // ── Nhóm 3: ba lớp bọc, service thật + InMemory ──────────────────────────────────────────────

    [Fact]
    public async Task Giu_ghe_thanh_cong_phat_dung_mot_su_kien_cho_chuyen_cua_request()
    {
        await using var db = TaoDb("giu-thanh-cong");
        var (chuyen, ghe) = await SeedChuyenVaGheAsync(db);
        var notifier = new NotifierGhiNhan();
        var service = new NotifyingSeatHoldCreateService(new SeatHoldCreateService(db), notifier);

        var result = await service.CreateAsync(Guid.NewGuid(), new CreateSeatHoldRequest
        {
            TripId = chuyen.Id,
            SeatIds = [ghe.Id],
        });

        Assert.True(result.Success);
        Assert.Equal(new[] { chuyen.Id }, notifier.Trips);
    }

    [Fact]
    public async Task Giu_ghe_that_bai_khong_phat_su_kien()
    {
        await using var db = TaoDb("giu-that-bai");
        var notifier = new NotifierGhiNhan();
        var service = new NotifyingSeatHoldCreateService(new SeatHoldCreateService(db), notifier);

        // Chuyến không tồn tại → 404: nhánh hỏng không đổi trạng thái ghế nào, không có gì để báo.
        var result = await service.CreateAsync(Guid.NewGuid(), new CreateSeatHoldRequest
        {
            TripId = Guid.NewGuid(),
            SeatIds = [Guid.NewGuid()],
        });

        Assert.False(result.Success);
        Assert.Empty(notifier.Trips);
    }

    [Fact]
    public async Task Nha_ghe_thanh_cong_phat_su_kien_theo_TripId_trong_response()
    {
        await using var db = TaoDb("nha-thanh-cong");
        var userId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        const string sessionCode = "PHIEN-REALTIME1";
        await SeedHoldAsync(db, tripId, userId, sessionCode, SeatHoldStatus.Holding, Moc(5));

        var notifier = new NotifierGhiNhan();
        var service = new NotifyingSeatHoldReleaseService(new SeatHoldReleaseService(db), notifier);

        // Nguồn TripId duy nhất của lượt nhả là RESPONSE — đầu vào chỉ có mã phiên.
        var result = await service.ReleaseAsync(userId, sessionCode);

        Assert.True(result.Success);
        Assert.Equal(new[] { tripId }, notifier.Trips);

        // Gọi lại lần hai trên phiên đã kết thúc (endpoint idempotent, vẫn 200) vẫn phát: CỐ Ý —
        // "thà thừa", client nhận lời nhắc rồi gọi lại sơ đồ ghế và diff thấy không đổi. Ghim ở đây
        // để ai định "tối ưu" bỏ lượt phát thứ hai biết đó là quyết định, không phải chỗ sót.
        await service.ReleaseAsync(userId, sessionCode);
        Assert.Equal(new[] { tripId, tripId }, notifier.Trips);
    }

    [Fact]
    public async Task Nha_ghe_khong_tim_thay_phien_khong_phat_su_kien()
    {
        await using var db = TaoDb("nha-khong-thay");
        var notifier = new NotifierGhiNhan();
        var service = new NotifyingSeatHoldReleaseService(new SeatHoldReleaseService(db), notifier);

        var result = await service.ReleaseAsync(Guid.NewGuid(), "PHIEN-KHONG-CO");

        Assert.False(result.Success);
        Assert.Empty(notifier.Trips);
    }

    [Fact]
    public async Task Het_han_phat_mot_su_kien_cho_moi_chuyen_vua_bi_lat()
    {
        await using var db = TaoDb("het-han");
        var now = new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);
        var userId = Guid.NewGuid();

        var chuyenA = Guid.NewGuid();
        var chuyenB = Guid.NewGuid();
        var chuyenDungMoc = Guid.NewGuid();
        var chuyenChuaToiHan = Guid.NewGuid();
        var chuyenDaKetThuc = Guid.NewGuid();

        // A có HAI ghế hết hạn cùng lượt — chỉ được MỘT sự kiện (DISTINCT theo chuyến), vì sự kiện
        // đi theo chuyến chứ không theo ghế.
        await SeedHoldAsync(db, chuyenA, userId, "PHIEN-A", SeatHoldStatus.Holding, now.AddMinutes(-1));
        await SeedHoldAsync(db, chuyenA, userId, "PHIEN-A", SeatHoldStatus.Holding, now.AddMinutes(-2));
        await SeedHoldAsync(db, chuyenB, userId, "PHIEN-B", SeatHoldStatus.Holding, now.AddMinutes(-5));

        // Biên: ĐÚNG mốc ExpiresAt vẫn còn hiệu lực (service thật so sánh nghiêm ngặt) — không được
        // lật, không được phát.
        await SeedHoldAsync(db, chuyenDungMoc, userId, "PHIEN-MOC", SeatHoldStatus.Holding, now);

        // Chưa tới hạn / đã kết thúc từ trước — ngoài tầm lượt quét.
        await SeedHoldAsync(db, chuyenChuaToiHan, userId, "PHIEN-SAU", SeatHoldStatus.Holding, now.AddMinutes(5));
        await SeedHoldAsync(db, chuyenDaKetThuc, userId, "PHIEN-ROI", SeatHoldStatus.Released, now.AddMinutes(-5));

        var notifier = new NotifierGhiNhan();
        var service = new NotifyingSeatHoldExpiryService(new SeatHoldExpiryService(db), db, notifier);

        var count = await service.ExpireDueHoldsAsync(now);

        Assert.Equal(3, count);
        Assert.Equal(2, notifier.Trips.Count); // A hai ghế nhưng một sự kiện
        Assert.Contains(chuyenA, notifier.Trips);
        Assert.Contains(chuyenB, notifier.Trips);
    }

    [Fact]
    public async Task Luot_quet_khong_lat_gi_thi_khong_phat()
    {
        await using var db = TaoDb("quet-khong-lat");
        var now = new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);
        var tripId = Guid.NewGuid();
        await SeedHoldAsync(db, tripId, Guid.NewGuid(), "PHIEN-Q", SeatHoldStatus.Holding, now.AddMinutes(-1));

        var notifier = new NotifierGhiNhan();
        var service = new NotifyingSeatHoldExpiryService(new SeatHoldExpiryService(db), db, notifier);

        Assert.Equal(1, await service.ExpireDueHoldsAsync(now));
        Assert.Equal(new[] { tripId }, notifier.Trips);

        // Lượt quét thứ hai trên cùng mốc: dòng đã Expired nên không lật gì, con số 0 chặn lượt phát.
        // Lớp bọc phát theo "danh sách hỏi trước" mà quên con số thật của service thì chỗ này phát
        // thừa lần hai — đúng cái bẫy mà thiết kế "danh sách chỉ là ước lượng" phải chặn.
        Assert.Equal(0, await service.ExpireDueHoldsAsync(now));
        Assert.Equal(new[] { tripId }, notifier.Trips);
    }

    // ── Nhóm 4: SeatMapNotifier ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Notifier_day_dung_nhom_va_dung_su_kien()
    {
        var tripId = Guid.NewGuid();
        var client = new ClientGhiNhan();
        string? nhom = null;
        var notifier = new SeatMapNotifier(
            new HubContextGia(groupName =>
            {
                nhom = groupName;
                return client;
            }),
            NullLogger<SeatMapNotifier>.Instance);

        await notifier.SeatMapChangedAsync(tripId);

        // Tên nhóm do SeatMapHub.GroupOf sinh — một chỗ duy nhất, JoinTrip phía client dùng đúng hàm
        // đó, nên ghim hình dạng "trip:{id}" ở đây là ghim luôn hai đầu khớp nhau.
        Assert.Equal($"trip:{tripId}", nhom);
        Assert.Equal(new[] { tripId }, client.Trips);
    }

    [Fact]
    public async Task Notifier_nuot_loi_khi_kenh_day_hong()
    {
        var notifier = new SeatMapNotifier(
            new HubContextGia(_ => throw new InvalidOperationException("kênh đẩy hỏng")),
            NullLogger<SeatMapNotifier>.Instance);

        // Hợp đồng của ISeatMapNotifier: KHÔNG ném. Người gọi đang ở cuối một thao tác nghiệp vụ đã
        // thành công — để lỗi kênh đẩy nổi lên là biến một lượt giữ ghế thành công thành 500.
        var loi = await Record.ExceptionAsync(() => notifier.SeatMapChangedAsync(Guid.NewGuid()));

        Assert.Null(loi);
    }

    // ── Hạ tầng dùng chung của lớp test ──────────────────────────────────────────────────────────
    // Mỗi lớp test tự chép helper, không có lớp base chung — đúng lệ đang có của dự án.

    private static AppDbContext TaoDb(string ten)
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"seat-map-{ten}-{Guid.NewGuid()}")
            .Options);

    private static DateTime Moc(double phutTuBayGio)
        => new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc).AddMinutes(phutTuBayGio);

    /// <summary>
    /// Chuyến + một ghế CÙNG xe — điều kiện tối thiểu để SeatHoldCreateService đi hết nhánh thành
    /// công (chuyến tồn tại, không Cancelled/Completed, ghế thuộc đúng xe của chuyến). Không seed
    /// dòng Buses: InMemory không cưỡng chế khoá ngoại, chỉ cần hai BusId khớp nhau — đúng điều
    /// kiện mà service thật kiểm.
    /// </summary>
    private static async Task<(Trip Chuyen, Seat Ghe)> SeedChuyenVaGheAsync(AppDbContext db)
    {
        var busId = Guid.NewGuid();
        var chuyen = new Trip
        {
            RouteId = Guid.NewGuid(),
            BusId = busId,
            DepartureTime = new DateTime(2026, 10, 11, 8, 0, 0, DateTimeKind.Utc),
        };
        var ghe = new Seat
        {
            BusId = busId,
            SeatLayoutId = Guid.NewGuid(),
            Floor = 1,
            RowIndex = 1,
            ColumnIndex = 1,
            SeatNumber = "A1",
        };

        db.Trips.Add(chuyen);
        db.Seats.Add(ghe);
        await db.SaveChangesAsync();
        return (chuyen, ghe);
    }

    private static async Task SeedHoldAsync(
        AppDbContext db,
        Guid tripId,
        Guid userId,
        string sessionCode,
        SeatHoldStatus status,
        DateTime expiresAt)
    {
        db.SeatHolds.Add(new SeatHold
        {
            TripId = tripId,

            // Ghế/chuyến thật không cần cho các ca ở đây: cả lượt nhả lẫn lượt quét chỉ hỏi
            // SeatHolds, và InMemory không cưỡng chế khoá ngoại.
            SeatId = Guid.NewGuid(),
            UserId = userId,
            SessionCode = sessionCode,
            Status = status,
            ExpiresAt = expiresAt,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<List<AuditLog>> LogsAsync(TestAppFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Sắp xếp tiếp theo Id vì nhiều bản ghi sinh trong cùng một mili giây nên CreatedAt bằng
        // nhau — chỉ OrderBy(CreatedAt) là thứ tự không xác định (cùng lối AuditLogCoverageTests).
        return await db.AuditLogs
            .AsNoTracking()
            .OrderBy(log => log.CreatedAt)
            .ThenBy(log => log.Id)
            .ToListAsync();
    }

    private sealed class NotifierGhiNhan : ISeatMapNotifier
    {
        public List<Guid> Trips { get; } = [];

        public Task SeatMapChangedAsync(Guid tripId)
        {
            Trips.Add(tripId);
            return Task.CompletedTask;
        }
    }

    private sealed class ClientGhiNhan : ISeatMapClient
    {
        public List<Guid> Trips { get; } = [];

        public Task SeatMapChanged(Guid tripId)
        {
            Trips.Add(tripId);
            return Task.CompletedTask;
        }
    }

    private sealed class HubContextGia : IHubContext<SeatMapHub, ISeatMapClient>
    {
        public HubContextGia(Func<string, ISeatMapClient> group) => Clients = new KhachGia(group);

        public IHubClients<ISeatMapClient> Clients { get; }

        public IGroupManager Groups => throw new NotSupportedException();
    }

    private sealed class KhachGia : IHubClients<ISeatMapClient>
    {
        private readonly Func<string, ISeatMapClient> _group;

        public KhachGia(Func<string, ISeatMapClient> group) => _group = group;

        public ISeatMapClient Group(string groupName) => _group(groupName);

        // Tám thành viên còn lại không dùng tới trong các ca ở đây — ném để lộ ngay nếu có người lỡ gọi.
        public ISeatMapClient All => throw new NotSupportedException();

        public ISeatMapClient AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();

        public ISeatMapClient Client(string connectionId) => throw new NotSupportedException();

        public ISeatMapClient Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();

        public ISeatMapClient GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();

        public ISeatMapClient Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();

        public ISeatMapClient User(string userId) => throw new NotSupportedException();

        public ISeatMapClient Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }
}
