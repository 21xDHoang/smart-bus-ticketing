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
/// Test bộ phận báo lệch số liệu cho quản trị — <see cref="RefundReconciliationNotifier"/> (US 8,
/// task *"Đối soát hoàn tiền hằng ngày + cảnh báo khi lệch số liệu"*, dòng 32 Sprint 4, Nguyễn Duy
/// Kiên).
///
/// Gọi thẳng notifier, KHÔNG dựng host — như <see cref="RefundResultNotifierTests"/>. Chỗ nối vào
/// app (service và job có được đăng ký không) nằm ở <see cref="RefundReconciliationWiringTests"/>.
///
/// <para>
/// Ba chính sách được ghim ở đây, vì cả ba đều là quyết định chứ không phải hệ quả của code:
/// <list type="number">
/// <item>người nhận là Admin/Manager ĐANG HOẠT ĐỘNG, xét cả vai trò chính lẫn bảng nối;</item>
/// <item>chỉ báo khi có lệch, tối đa một lần mỗi người mỗi NGÀY;</item>
/// <item>nội dung không có số tiền — cùng luật đã chốt cho mọi thông báo.</item>
/// </list>
/// </para>
/// </summary>
public class RefundReconciliationNotifierTests
{
    private static readonly DateTime BayGio = new(2026, 10, 17, 0, 5, 0, DateTimeKind.Utc);

    private readonly string _tenCsdl = $"canh-bao-doi-soat-{Guid.NewGuid():N}";

    // ── Người nhận ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Chi_bao_cho_admin_va_manager_dang_hoat_dong()
    {
        await using var db = TaoDb();
        var admin = await SeedNguoiDungAsync(db, RoleIds.Admin, "0900000001");
        var managerChinh = await SeedNguoiDungAsync(db, RoleIds.Manager, "0900000002");
        var managerPhu = await SeedNguoiDungAsync(db, RoleIds.Passenger, "0900000003");
        var taiXe = await SeedNguoiDungAsync(db, RoleIds.Driver, "0900000004");
        var hanhKhach = await SeedNguoiDungAsync(db, RoleIds.Passenger, "0900000005");
        var managerDaKhoa = await SeedNguoiDungAsync(
            db,
            RoleIds.Manager,
            "0900000006",
            isActive: false);

        // Manager chỉ có vai trò qua bảng nối — đúng lối RBAC nhiều vai trò của dự án.
        db.UserRoles.Add(new UserRole { UserId = managerPhu.Id, RoleId = RoleIds.Manager });
        await db.SaveChangesAsync();

        var client = new ClientGhiNhan();
        var nhom = new List<string>();
        var result = await TaoService(db, client, nhom.Add)
            .NotifyAsync(BaoCao("RF-aaaa1111"));

        Assert.Equal(3, result.RecipientCount);
        Assert.Equal(3, result.CreatedCount);
        Assert.Equal(0, result.SkippedCount);

        var nguoiNhan = db.Notifications.Select(n => n.UserId).ToHashSet();

        // Đúng ba người: Admin, Manager vai trò chính, Manager qua bảng nối. Tài khoản đã khoá không
        // đăng nhập được để đọc cảnh báo, còn tài xế/hành khách không thấy dòng tiền của hệ thống.
        Assert.Equal(new[] { admin.Id, managerChinh.Id, managerPhu.Id }.ToHashSet(), nguoiNhan);
        Assert.DoesNotContain(taiXe.Id, nguoiNhan);
        Assert.DoesNotContain(hanhKhach.Id, nguoiNhan);
        Assert.DoesNotContain(managerDaKhoa.Id, nguoiNhan);

        Assert.All(db.Notifications, n =>
        {
            Assert.Equal(NotificationType.RefundReconciliationAlert, n.Type);
            Assert.Equal(RefundReconciliationNotifier.AlertTitle, n.Title);
        });

        // Cảnh báo nói về NHIỀU khoản hoàn — con trỏ đa hình phải null, không được giả vờ trỏ một dòng.
        Assert.All(db.Notifications, n =>
        {
            Assert.Null(n.RelatedEntityType);
            Assert.Null(n.RelatedEntityId);
        });

        Assert.Equal(3, client.NotificationIds.Count);

        // Tên nhóm do NotificationHub.GroupOf sinh — OnConnectedAsync phía hub dùng đúng hàm đó, nên
        // ghim hình dạng "user:{id}" ở đây là ghim luôn hai đầu khớp nhau.
        Assert.Equal(
            new[] { admin.Id, managerChinh.Id, managerPhu.Id }
                .Select(id => $"user:{id}")
                .ToHashSet(),
            nhom.ToHashSet());
    }

    [Fact]
    public async Task Khong_co_quan_tri_nao_thi_khong_nem_loi()
    {
        await using var db = TaoDb();
        await SeedNguoiDungAsync(db, RoleIds.Passenger, "0900000009");

        var result = await TaoService(db).NotifyAsync(BaoCao("RF-aaaa1111"));

        Assert.Equal(0, result.RecipientCount);
        Assert.Equal(0, result.CreatedCount);
        Assert.Empty(db.Notifications);
    }

    // ── Chính sách: im lặng và chống báo trùng ──────────────────────────────────────────────────

    [Fact]
    public async Task Khong_co_lech_thi_khong_ghi_gi()
    {
        await using var db = TaoDb();
        await SeedNguoiDungAsync(db, RoleIds.Admin, "0900000001");

        var client = new ClientGhiNhan();
        var result = await TaoService(db, client).NotifyAsync(BaoCao());

        // Một thông báo "hôm nay không lệch" mỗi ngày là 365 dòng không nói gì, và sẽ dạy quản trị
        // bỏ qua chuông. Im lặng là chủ ý, không phải thiếu sót.
        Assert.Equal(0, result.CreatedCount);
        Assert.Empty(db.Notifications);
        Assert.Empty(client.NotificationIds);
    }

    [Fact]
    public async Task Chay_lai_trong_cung_ngay_khong_ghi_them()
    {
        await using var db = TaoDb();
        await SeedNguoiDungAsync(db, RoleIds.Admin, "0900000001");
        await SeedNguoiDungAsync(db, RoleIds.Manager, "0900000002");

        var client = new ClientGhiNhan();
        var service = TaoService(db, client);

        var lanDau = await service.NotifyAsync(BaoCao("RF-aaaa1111"));
        var lanHai = await service.NotifyAsync(BaoCao("RF-bbbb2222"));

        // Khoá chống trùng là (người nhận, loại, NGÀY) — không theo mã khoản hoàn. Một lượt chạy lại
        // thắng đua trong cùng ngày không được dội thêm dòng, nội dung mới không thay đổi kết luận.
        Assert.Equal(2, lanDau.CreatedCount);
        Assert.Equal(0, lanHai.CreatedCount);
        Assert.Equal(2, lanHai.SkippedCount);
        Assert.Equal(2, await db.Notifications.CountAsync());
        Assert.Equal(2, client.NotificationIds.Count);
    }

    [Fact]
    public async Task Hom_sau_thi_bao_lai()
    {
        await using var db = TaoDb();
        await SeedNguoiDungAsync(db, RoleIds.Admin, "0900000001");

        var service = TaoService(db);

        await service.NotifyAsync(BaoCao("RF-aaaa1111"));
        await service.NotifyAsync(BaoCao(["RF-bbbb2222"], BayGio.AddDays(1)));

        Assert.Equal(2, await db.Notifications.CountAsync());
    }

    [Fact]
    public async Task Thong_bao_loai_khac_cung_ngay_khong_chan_canh_bao()
    {
        await using var db = TaoDb();
        var admin = await SeedNguoiDungAsync(db, RoleIds.Admin, "0900000001");

        db.Notifications.Add(new Notification
        {
            UserId = admin.Id,
            Type = NotificationType.RefundSucceeded,
            Title = "Đã hoàn tiền",
            Body = "Khoản hoàn tiền của bạn đã được xử lý thành công.",
            CreatedAt = BayGio.AddMinutes(-1),
        });
        await db.SaveChangesAsync();

        await TaoService(db).NotifyAsync(BaoCao("RF-aaaa1111"));

        // Chốt chống trùng lọc theo ĐÚNG loại cảnh báo này. Một thông báo khác trong ngày không phải
        // lý do để nuốt cảnh báo vận hành.
        Assert.Equal(1, await db.Notifications.CountAsync(
            n => n.Type == NotificationType.RefundReconciliationAlert));
    }

    // ── Nội dung ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Body_neu_so_diem_lech_va_ma_khoan_hoan_nhung_khong_co_so_tien()
    {
        await using var db = TaoDb();
        await SeedNguoiDungAsync(db, RoleIds.Admin, "0900000001");

        await TaoService(db).NotifyAsync(BaoCao("RF-aaaa1111", "RF-bbbb2222"));

        var thongBao = Assert.Single(db.Notifications);

        Assert.Contains("2 điểm lệch", thongBao.Body, StringComparison.Ordinal);
        Assert.Contains("RF-aaaa1111", thongBao.Body, StringComparison.Ordinal);
        Assert.Contains("RF-bbbb2222", thongBao.Body, StringComparison.Ordinal);

        // Cùng luật với mọi thông báo khác: con số tiền phải tra ở chính dòng Refunds. Chép vào đây
        // là dựng bản sao thứ hai có thể lệch, và cảnh báo là thứ đọc lại được nên nó sống lâu.
        Assert.DoesNotContain("175.000", thongBao.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("175000", thongBao.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Danh_sach_ma_khoan_hoan_bi_cat_bot()
    {
        await using var db = TaoDb();
        await SeedNguoiDungAsync(db, RoleIds.Admin, "0900000001");

        var ma = Enumerable.Range(1, 12).Select(i => $"RF-A{i:00}").ToArray();
        await TaoService(db).NotifyAsync(BaoCao(ma));

        var thongBao = Assert.Single(db.Notifications);

        Assert.Contains($"RF-A{RefundReconciliationNotifier.MaxRefundCodesInBody:00}", thongBao.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("RF-A11", thongBao.Body, StringComparison.Ordinal);
        Assert.Contains("….", thongBao.Body, StringComparison.Ordinal);
    }

    // ── Hai nhánh hỏng: hợp đồng "không bao giờ ném lỗi" ────────────────────────────────────────

    [Fact]
    public async Task Kenh_day_hong_khong_nem_loi_va_dong_van_duoc_ghi()
    {
        await using var db = TaoDb();
        await SeedNguoiDungAsync(db, RoleIds.Admin, "0900000001");

        var service = TaoService(
            db,
            new ClientGhiNhan(),
            _ => throw new InvalidOperationException("kênh đẩy hỏng"));

        var loi = await Record.ExceptionAsync(() => service.NotifyAsync(BaoCao("RF-aaaa1111")));

        // Lượt đối soát đã tìm ra lệch và không được coi là hỏng chỉ vì kênh đẩy có sự cố — lượt sau
        // là 24 giờ nữa. Dòng thông báo vẫn phải còn để quản trị đọc lại.
        Assert.Null(loi);
        Assert.Single(db.Notifications);
    }

    [Fact]
    public async Task Doc_danh_sach_nguoi_nhan_hong_khong_nem_loi()
    {
        var db = TaoDb();
        var service = TaoService(db, new ClientGhiNhan());

        await db.DisposeAsync();

        var loi = await Record.ExceptionAsync(() => service.NotifyAsync(BaoCao("RF-aaaa1111")));

        Assert.Null(loi);
    }

    [Fact]
    public async Task Ghi_thong_bao_hong_khong_nem_loi()
    {
        var chanGhi = new ChanGhiInterceptor();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_tenCsdl)
            .AddInterceptors(chanGhi)
            .Options);

        await SeedNguoiDungAsync(db, RoleIds.Admin, "0900000001");
        chanGhi.Nap();   // nạp SAU khi seed — lượt ghi của chính bài test không bị chặn

        var service = TaoService(db, new ClientGhiNhan());
        var loi = await Record.ExceptionAsync(() => service.NotifyAsync(BaoCao("RF-aaaa1111")));

        Assert.Null(loi);
        Assert.Empty(db.Notifications);
    }

    // ── Hạ tầng dùng chung của lớp test ─────────────────────────────────────────────────────────

    private AppDbContext TaoDb()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_tenCsdl)
            .Options);

    private static RefundReconciliationNotifier TaoService(
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
            NullLogger<RefundReconciliationNotifier>.Instance);

    private async Task<User> SeedNguoiDungAsync(
        AppDbContext db,
        Guid roleId,
        string phoneNumber,
        bool isActive = true)
    {
        var user = new User
        {
            PhoneNumber = phoneNumber,
            FullName = $"Người dùng {phoneNumber}",
            PasswordHash = "hash-gia-cua-test",
            RoleId = roleId,
            IsActive = isActive,
            CreatedAt = BayGio.AddDays(-30),
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    /// <summary>
    /// Báo cáo tối thiểu để notifier đi hết một nhánh. Mỗi mã khoản hoàn thành một điểm lệch
    /// nghiêm trọng — bài nào cần cấu trúc khác thì truyền báo cáo dựng tay.
    /// </summary>
    private static RefundReconciliationReport BaoCao(params string[] refundCodes)
        => BaoCao(refundCodes, BayGio);

    private static RefundReconciliationReport BaoCao(string[] refundCodes, DateTime runAt)
        => new()
        {
            RunAtUtc = runAt,
            WindowStartUtc = runAt.AddHours(-24),
            WindowEndUtc = runAt,
            ScannedRefundCount = 1,
            StatementAvailable = false,
            Issues = refundCodes
                .Select(code => new RefundReconciliationIssue
                {
                    Kind = RefundReconciliationIssueKind.SuccessEvidenceOnOpenRefund,
                    Severity = RefundReconciliationSeverity.Critical,
                    RefundCode = code,
                    Message = "Nhật ký có lượt gọi thành công nhưng khoản hoàn chưa Succeeded.",
                })
                .ToList(),
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

    /// <summary>Chặn lượt ghi khi được nạp — dựng nhánh "đọc được nhưng ghi hỏng".</summary>
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

            // Các thành viên còn lại không dùng tới — ném để lộ ngay nếu có người lỡ gọi.
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
