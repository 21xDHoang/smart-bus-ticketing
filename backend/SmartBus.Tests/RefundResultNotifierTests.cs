using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Refunds;
using SmartBus.Api.Entities;
using SmartBus.Api.Hubs;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test bộ phận báo kết quả hoàn tiền cho khách — <see cref="RefundResultNotifier"/> (task *"Thông báo
/// kết quả hoàn tiền qua email và trên web"*, dòng 34 Sprint 4, Phùng Duy Hoàng).
///
/// Gọi thẳng service, KHÔNG dựng host — như <see cref="TicketRequestResultNotifierTests"/>. Chỗ nối
/// vào app (lớp bọc <see cref="NotifyingRefundAttemptService"/> có thật sự được đăng ký không) nằm ở
/// <see cref="RefundRetryWiringTests"/>.
///
/// ⚠️ Khác căn bản với test của notifier bên US 5: <b>phán quyết ở đây đọc từ dòng <c>Refunds</c> ĐÃ
/// LƯU</b>, không từ tham số truyền vào. Vì vậy mỗi bài ở dưới dựng một dòng <c>Refunds</c> thật trong
/// InMemory rồi truyền <see cref="RefundAttemptOutcome"/> của một lượt gọi cổng — trạng thái "có gì để
/// báo" là trạng thái CỦA DÒNG, và các bài cố ý cho outcome và dòng nói hai chuyện khác nhau để ghim
/// đúng điều đó.
///
/// ⚠️ InMemory không cưỡng chế khoá ngoài và không cưỡng chế chỉ mục — file này xanh không nói gì về
/// IX_Notifications_UserId_CreatedAt (A6). Chốt chống ghi trùng là phép ĐỌC TRƯỚC KHI GHI (cùng giới
/// hạn một-bản-app đã ghi ở docs/33 §4): bài <see cref="Goi_lai_lan_hai_khong_sinh_dong_thu_hai"/>
/// kiểm đúng hành vi đó trên MỘT tiến trình.
/// </summary>
public class RefundResultNotifierTests
{
    private const string MaHoan = "RF-8f3a2c1d";

    private const decimal SoTien = 175_000m;

    /// <summary>Mốc "bây giờ" cố định của cả file — không đồng hồ thật, không chập chờn.</summary>
    private static readonly DateTime BayGio = new(2026, 10, 14, 8, 0, 0, DateTimeKind.Utc);

    /// <summary>Tên CSDL InMemory riêng cho từng test — xUnit dựng lớp test mới cho mỗi test.</summary>
    private readonly string _tenCsdl = $"hoan-tien-thong-bao-{Guid.NewGuid():N}";

    // ── Hai kết quả CUỐI: báo, và báo đúng câu ──────────────────────────────────────────────────

    [Fact]
    public async Task Hoan_thanh_cong_thi_ghi_mot_dong_va_day_dung_nhom()
    {
        await using var db = TaoDb();
        var khoan = await SeedHoanAsync(db, RefundStatus.Succeeded);
        var client = new ClientGhiNhan();
        string? nhom = null;

        await TaoService(db, client, group => nhom = group).NotifyAsync(KetQua(khoan));

        var thongBao = Assert.Single(db.Notifications);

        Assert.Equal(khoan.UserId, thongBao.UserId);
        Assert.Equal(NotificationType.RefundSucceeded, thongBao.Type);

        // Con trỏ đa hình phải trỏ về ĐÚNG bảng và ĐÚNG dòng — màn "Hoàn tiền của tôi" dùng nó để mở
        // chi tiết, và phép chống ghi trùng lọc theo nó.
        Assert.Equal(NotificationTarget.Refund, thongBao.RelatedEntityType);
        Assert.Equal(khoan.Id, thongBao.RelatedEntityId);

        Assert.Equal("Đã hoàn tiền", thongBao.Title);
        Assert.Equal("Khoản hoàn tiền của bạn đã được xử lý thành công.", thongBao.Body);

        // Không có CHỮ SỐ nào trong nội dung — chốt chứ không phải chỗ sót, cùng lối docs/33 §6.1:
        // con số phải là thứ khách tra ở màn "Hoàn tiền của tôi" (nơi dòng Refunds sống), chép vào đây
        // là dựng bản sao thứ hai có thể lệch. Bài này TỪNG ĐỎ nếu ai đó thêm "175.000đ" vào body.
        Assert.DoesNotMatch("[0-9]", thongBao.Body);

        // Tên nhóm do NotificationHub.GroupOf sinh — OnConnectedAsync phía hub dùng đúng hàm đó, nên
        // ghim hình dạng "user:{id}" ở đây là ghim luôn hai đầu khớp nhau.
        Assert.Equal($"user:{khoan.UserId}", nhom);
        Assert.Equal(new[] { thongBao.Id }, client.NotificationIds);
    }

    [Fact]
    public async Task Het_lich_thi_bao_that_bai_nhung_khong_lo_ly_do_ky_thuat()
    {
        await using var db = TaoDb();
        var khoan = await SeedHoanAsync(
            db,
            RefundStatus.Failed,
            failReason: "Sai mã định danh tài khoản nhận hoàn");
        var client = new ClientGhiNhan();
        string? nhom = null;

        await TaoService(db, client, group => nhom = group)
            .NotifyAsync(KetQua(khoan, RefundLogResult.Failed));

        var thongBao = Assert.Single(db.Notifications);

        Assert.Equal(khoan.UserId, thongBao.UserId);
        Assert.Equal(NotificationType.RefundFailed, thongBao.Type);
        Assert.Equal(NotificationTarget.Refund, thongBao.RelatedEntityType);
        Assert.Equal(khoan.Id, thongBao.RelatedEntityId);

        Assert.Equal("Hoàn tiền thất bại", thongBao.Title);
        Assert.Equal(
            "Khoản hoàn tiền của bạn chưa thực hiện được sau nhiều lần thử lại. "
            + "Bạn vui lòng liên hệ nhà xe để được hỗ trợ.",
            thongBao.Body);

        // Lý do cổng từ chối là chuyện KỸ THUẬT ("sai mã định danh tài khoản") — khách đọc câu đó
        // không làm được gì ngoài việc hoang mang, và thông báo là chỗ đọc lại được (A8.5) nên nó sẽ
        // sống lâu. Việc của khách ở đây là một câu: liên hệ nhà xe.
        Assert.DoesNotContain("Sai mã định danh", thongBao.Body, StringComparison.Ordinal);

        Assert.DoesNotMatch("[0-9]", thongBao.Body);

        Assert.Equal($"user:{khoan.UserId}", nhom);
        Assert.Equal(new[] { thongBao.Id }, client.NotificationIds);
    }

    // ── Những lượt KHÔNG có gì để báo: im lặng là chủ ý ──────────────────────────────────────────

    [Fact]
    public async Task That_bai_con_lich_thi_im_lang()
    {
        await using var db = TaoDb();
        var khoan = await SeedHoanAsync(
            db,
            RefundStatus.Failed,
            nextRetryAt: BayGio.AddMinutes(5),
            failReason: "Cổng bận");
        var client = new ClientGhiNhan();

        await TaoService(db, client)
            .NotifyAsync(KetQua(khoan, RefundLogResult.Failed, nextRetryAt: BayGio.AddMinutes(5)));

        // Thất bại TẠM THỜI — ngã ngũ chưa xong. Báo ở đây là một khoản hỏng rồi tự khỏi dội tới sáu
        // thông báo giống hệt nhau (thang 5' → 30' → 2h → 12h → 24h), trong đó thông báo cuối mới là
        // sự thật. Khách chỉ nghe khi hết đường tự động.
        Assert.Empty(db.Notifications);
        Assert.Empty(client.NotificationIds);
    }

    [Fact]
    public async Task Cong_chua_tra_loi_thi_im_lang()
    {
        await using var db = TaoDb();
        var khoan = await SeedHoanAsync(db, RefundStatus.Pending);
        var client = new ClientGhiNhan();

        // Unresolved: cổng hết thời gian chờ / đứt kết nối — hệ thống CHƯA biết tiền có rời cổng hay
        // không (RefundLogResult.Unresolved). Báo "thành công" là đoán, mà báo "thất bại" còn tệ hơn:
        // nó mở đường cho job gọi lại một khoản cổng đã trả — hoàn hai lần. Tra cho ra nhẽ là việc của
        // bước đối soát.
        await TaoService(db, client).NotifyAsync(KetQua(khoan, RefundLogResult.Unresolved));

        Assert.Empty(db.Notifications);
        Assert.Empty(client.NotificationIds);
    }

    [Fact]
    public async Task Luot_thua_cuoc_dua_thi_im_lang()
    {
        await using var db = TaoDb();

        // Dòng đã Succeeded THẬT (lượt thắng đã gọi cổng và ghi kết quả) nhưng outcome này là của lượt
        // thua: Claimed = false, không gọi cổng, không ghi gì. Cổng chốt trước tiên là Claimed — lượt
        // thua không được báo hộ lượt thắng (lượt thắng có lượt NotifyAsync riêng của nó), và người gọi
        // đã ghi log "bỏ qua" cho chính lượt này rồi.
        var khoan = await SeedHoanAsync(db, RefundStatus.Succeeded);
        var client = new ClientGhiNhan();

        await TaoService(db, client).NotifyAsync(KetQua(khoan, claimed: false));

        Assert.Empty(db.Notifications);
        Assert.Empty(client.NotificationIds);
    }

    // ── Phán quyết là trạng thái ĐÃ LƯU, không phải outcome ──────────────────────────────────────

    [Fact]
    public async Task Phan_quyet_doc_tu_dong_da_luu_chu_khong_tin_outcome()
    {
        await using var db = TaoDb();
        var client = new ClientGhiNhan();
        var service = TaoService(db, client);

        // Chiều 1 — outcome nói lượt này THÀNH CÔNG, nhưng dòng đã bị lật về Pending kể từ đó (một
        // lượt khác giành được nó và đang bay). Tin outcome là báo "đã hoàn tiền" cho một khoản chưa
        // chắc đã hoàn. Không báo gì cả.
        var daBiLat = await SeedHoanAsync(db, RefundStatus.Pending, refundCode: "RF-11111111");
        await service.NotifyAsync(KetQua(daBiLat, RefundLogResult.Succeeded));

        Assert.Empty(db.Notifications);

        // Chiều 2 — ngược lại: outcome của lượt này là Unresolved, nhưng dòng đã Succeeded (một lượt
        // khác thắng và thành công trước khi notifier chạy). Thứ khách cần biết vẫn là sự thật mới
        // nhất của DÒNG: tiền đã về.
        var daThanhCong = await SeedHoanAsync(db, RefundStatus.Succeeded, refundCode: "RF-22222222");
        await service.NotifyAsync(KetQua(daThanhCong, RefundLogResult.Unresolved));

        var thongBao = Assert.Single(db.Notifications);
        Assert.Equal(NotificationType.RefundSucceeded, thongBao.Type);
        Assert.Equal(daThanhCong.Id, thongBao.RelatedEntityId);
    }

    // ── Chống ghi trùng ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Goi_lai_lan_hai_khong_sinh_dong_thu_hai()
    {
        await using var db = TaoDb();
        var khoan = await SeedHoanAsync(db, RefundStatus.Succeeded);
        var client = new ClientGhiNhan();
        var service = TaoService(db, client);

        await service.NotifyAsync(KetQua(khoan));
        await service.NotifyAsync(KetQua(khoan));

        // Một kết quả cuối = MỘT thông báo. Không có chốt này thì một lượt gọi lại (job thử lại chạy
        // chồng, lớp bọc bị gọi hai lần) làm khách thấy hai dòng y hệt nhau trên màn thông báo.
        Assert.Single(db.Notifications);

        // Lượt thứ hai không đẩy lại: không có gì mới để nhắc.
        Assert.Single(client.NotificationIds);
    }

    [Fact]
    public async Task Het_thang_that_bai_roi_thu_tay_thanh_cong_thi_bao_ca_hai()
    {
        await using var db = TaoDb();
        var khoan = await SeedHoanAsync(db, RefundStatus.Failed, failReason: "Cổng bận");
        var client = new ClientGhiNhan();
        var service = TaoService(db, client);

        // Lượt cuối của thang: hết lịch, khoản nằm chờ quản trị.
        await service.NotifyAsync(KetQua(khoan, RefundLogResult.Failed));
        Assert.Single(db.Notifications);

        // Quản trị bấm "Thử lại" và cổng lần này nhận — dòng lật sang Succeeded, hết lịch thử lại.
        khoan.Status = RefundStatus.Succeeded;
        khoan.NextRetryAt = null;
        khoan.FailReason = null;
        await db.SaveChangesAsync();

        await service.NotifyAsync(KetQua(khoan));

        // Phép chống ghi trùng khoá theo (người nhận, LOẠI, dòng) — hai loại khác nhau nên khách nhận
        // đủ cả hai: "thất bại, gọi nhà xe" rồi "đã hoàn tiền" là vòng đời THẬT của khoản đó, không
        // phải nhiễu. Chỉ lặp lại CÙNG loại mới bị nuốt.
        Assert.Equal(2, await db.Notifications.CountAsync());
        Assert.Single(db.Notifications.Where(n => n.Type == NotificationType.RefundFailed));
        Assert.Single(db.Notifications.Where(n => n.Type == NotificationType.RefundSucceeded));
        Assert.Equal(2, client.NotificationIds.Count);
    }

    [Fact]
    public async Task Hoan_KHAC_thi_van_bao_binh_thuong()
    {
        await using var db = TaoDb();
        var userId = Guid.NewGuid();

        // Phép chống ghi trùng lọc theo ĐÚNG dòng Refunds, không theo người nhận: một khách có hai
        // khoản hoàn khác nhau (ví dụ hai giao dịch hỏng) phải nhận đủ hai thông báo.
        var khoanA = await SeedHoanAsync(db, RefundStatus.Succeeded, refundCode: "RF-aaaa1111", userId: userId);
        var khoanB = await SeedHoanAsync(db, RefundStatus.Succeeded, refundCode: "RF-bbbb2222", userId: userId);
        var service = TaoService(db);

        await service.NotifyAsync(KetQua(khoanA));
        await service.NotifyAsync(KetQua(khoanB));

        Assert.Equal(2, await db.Notifications.CountAsync());
    }

    // ── Hai nhánh hỏng: hợp đồng "không bao giờ ném lỗi" ────────────────────────────────────────

    [Fact]
    public async Task Ken_duy_hong_khong_nem_loi_va_dong_van_duoc_ghi()
    {
        await using var db = TaoDb();
        var khoan = await SeedHoanAsync(db, RefundStatus.Succeeded);

        var service = TaoService(db, new ClientGhiNhan(), _ => throw new InvalidOperationException("kênh đẩy hỏng"));

        var loi = await Record.ExceptionAsync(() => service.NotifyAsync(KetQua(khoan)));

        // Người gọi đang ở cuối một lượt gọi cổng ĐÃ THÀNH CÔNG — tiền đã rời cổng. Để lỗi kênh đẩy nổi
        // lên là biến lượt hoàn thành công thành lỗi ở mắt job thử lại; job tưởng khoản còn hỏng.
        Assert.Null(loi);

        // Và dòng thông báo vẫn phải còn: mất lời nhắc tức thời là chuyện nhỏ, mất bản ghi để khách đọc
        // lại khi offline (A8.5) mới là chuyện lớn.
        Assert.Single(db.Notifications);
    }

    [Fact]
    public async Task Doc_dong_hoan_hong_khong_nem_loi()
    {
        var db = TaoDb();
        var service = TaoService(db, new ClientGhiNhan());

        // DbContext đã bị dispose → lượt ĐỌC dòng Refunds ném ObjectDisposedException. Đây là cách rẻ
        // nhất để dựng nhánh "CSDL hỏng ngay bước đầu" mà không cần provider biết ném lỗi.
        await db.DisposeAsync();

        var loi = await Record.ExceptionAsync(
            () => service.NotifyAsync(KetQua(SeedHoanChuaLuu())));

        Assert.Null(loi);
    }

    [Fact]
    public async Task Ghi_thong_bao_hong_khong_nem_loi()
    {
        // Nhánh còn lại: ĐỌC được nhưng GHI hỏng. InMemory không cưỡng chế gì nên không có cách tự
        // nhiên nào làm SaveChanges ném — dựng bằng interceptor chặn đúng lượt SavingChanges, cùng lối
        // BomXminDoiThuInterceptor ở RefundClaimConcurrencyTests (interceptor là điểm duy nhất trong
        // pipeline của EF chạm được vào lượt ghi).
        var chanGhi = new ChanGhiInterceptor();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_tenCsdl)
            .AddInterceptors(chanGhi)
            .Options);

        var khoan = await SeedHoanAsync(db, RefundStatus.Succeeded);
        chanGhi.Nap();   // nạp SAU khi seed — lượt ghi của chính bài test không bị chặn

        var service = TaoService(db, new ClientGhiNhan());
        var loi = await Record.ExceptionAsync(() => service.NotifyAsync(KetQua(khoan)));

        Assert.Null(loi);
        Assert.Empty(db.Notifications);
    }

    // ── Hạ tầng dùng chung của lớp test ──────────────────────────────────────────────────────────
    // Mỗi lớp test tự chép helper, không có lớp base chung — đúng lệ đang có của dự án.

    private AppDbContext TaoDb()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_tenCsdl)
            .Options);

    /// <summary>
    /// Service với một kênh đẩy giả. <paramref name="onGroup"/> nhận tên nhóm mà service yêu cầu —
    /// dùng để ghim hình dạng tên nhóm, thứ mà một lớp <c>IHubContext</c> giả không tự kiểm được.
    /// </summary>
    private static RefundResultNotifier TaoService(
        AppDbContext db,
        ClientGhiNhan? client = null,
        Action<string>? onGroup = null)
        => new(
            db,
            new HubContextGia(group =>
            {
                onGroup?.Invoke(group);
                return client ?? new ClientGhiNhan();
            }),
            NullLogger<RefundResultNotifier>.Instance);

    /// <summary>
    /// Dòng <c>Refunds</c> tối thiểu để notifier đi hết một nhánh. Không cần Payment/Ticket thật:
    /// InMemory không cưỡng chế khoá ngoài, mà notifier chỉ đọc chính dòng này.
    /// </summary>
    private async Task<Refund> SeedHoanAsync(
        AppDbContext db,
        RefundStatus trangThai,
        DateTime? nextRetryAt = null,
        string? failReason = null,
        string refundCode = MaHoan,
        Guid? userId = null)
    {
        var khoan = new Refund
        {
            RefundCode = refundCode,
            Source = RefundSource.PaymentFailure,
            PaymentId = Guid.NewGuid(),
            UserId = userId ?? Guid.NewGuid(),
            Amount = SoTien,
            Status = trangThai,
            NextRetryAt = nextRetryAt,
            FailReason = failReason,
            CreatedAt = BayGio.AddHours(-1),
        };

        db.Refunds.Add(khoan);
        await db.SaveChangesAsync();
        return khoan;
    }

    /// <summary>Khoản hoàn chưa lưu — chỉ để lấy Id/RefundCode dựng outcome khi không cần dòng trong CSDL.</summary>
    private static Refund SeedHoanChuaLuu()
        => new() { RefundCode = MaHoan };

    /// <summary>
    /// Outcome của một lượt gọi cổng, mặc định là lượt thắng thành công. <c>claimed: false</c> thì
    /// <c>Result</c> phải null — cùng bất biến của <see cref="RefundAttemptOutcome"/> (không gọi thì
    /// không có kết quả để nói).
    /// </summary>
    private static RefundAttemptOutcome KetQua(
        Refund khoan,
        RefundLogResult? ketQuaCong = RefundLogResult.Succeeded,
        bool claimed = true,
        DateTime? nextRetryAt = null)
        => new()
        {
            RefundId = khoan.Id,
            RefundCode = khoan.RefundCode,
            Claimed = claimed,
            Result = claimed ? ketQuaCong : null,
            NextRetryAt = nextRetryAt,
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

    /// <summary>
    /// Chặn lượt ghi khi được nạp — dựng nhánh "đọc được nhưng ghi hỏng" mà InMemory không có cách
    /// nào tự nhiên tạo ra. Nạp SAU khi seed để lượt ghi của chính bài test không bị chặn.
    /// </summary>
    private sealed class ChanGhiInterceptor : SaveChangesInterceptor
    {
        private bool _daNap;

        public void Nap() => _daNap = true;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (_daNap)
            {
                throw new InvalidOperationException("CSDL hỏng ở bước ghi");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
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
