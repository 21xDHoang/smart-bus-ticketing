using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test phần ruột job cảnh báo tài khoản giữ chỗ quá nhiều lần — <see cref="SeatHoldAbuseService"/>
/// (task *"Ghi log và cảnh báo khi một tài khoản giữ chỗ quá nhiều lần"* — Vàng Thị Dăm).
///
/// Gọi thẳng service với một mốc <c>now</c> ghim sẵn, KHÔNG dựng host: vòng lặp
/// <see cref="SeatHoldAbuseBackgroundService"/> chạy theo nhịp 5 phút nên chờ nó là test chập chờn
/// và chậm. Phần "job có thật sự được đăng ký vào app không" nằm ở
/// <see cref="SeatHoldAbuseWiringTests"/>.
///
/// Dùng provider InMemory như phần còn lại của bộ test. Không seed Trips/Seats/Users: InMemory
/// không cưỡng chế khoá ngoài, mà truy vấn của job chỉ đọc hai bảng SeatHolds và AuditLogs.
///
/// ⚠️ InMemory cũng KHÔNG cưỡng chế chỉ mục, nên file này xanh không nói gì về chuyện
/// IX_SeatHolds_UserId_CreatedAt có tồn tại hay không — chỉ mục đó nằm ở
/// AppDbContext.Seat.cs và là việc của migration, không phải của test này.
/// </summary>
public class SeatHoldAbuseServiceTests
{
    /// <summary>Mốc "bây giờ" cố định — mọi lượt giữ trong file dựng quanh mốc này.</summary>
    private static readonly DateTime BayGio = new(2026, 10, 8, 9, 30, 0, DateTimeKind.Utc);

    /// <summary>
    /// Tên CSDL InMemory riêng cho từng ca test. xUnit dựng một instance lớp test mới cho mỗi ca nên
    /// mỗi ca có CSDL sạch — cùng tên là dùng chung dữ liệu.
    /// </summary>
    private readonly string _tenCsdl = $"canh-bao-giu-cho-{Guid.NewGuid()}";

    // ---------------------------------------------------------------------------------------
    // Ngưỡng: đạt thì cảnh báo, chưa đạt thì thôi
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Tai_khoan_dat_nguong_thi_sinh_dung_mot_dong_canh_bao()
    {
        using var db = TaoDb();
        var userId = Guid.NewGuid();
        await SeedNhieuPhienAsync(db, userId, soPhien: 5);

        var soTaiKhoan = await TaoService(db).ScanAndWarnAsync(BayGio);

        Assert.Equal(1, soTaiKhoan);

        var log = Assert.Single(db.AuditLogs);

        Assert.Equal(AuditAction.Warning, log.Action);
        Assert.Equal(userId, log.UserId);
        Assert.Equal(BayGio, log.CreatedAt);

        // Target theo đúng định dạng "<Tên bảng>:<Id>" của A9, trỏ về tài khoản bị gắn cờ — đây là
        // thứ Admin đọc trên màn nhật ký để biết cảnh báo nói về ai.
        Assert.Equal($"Users:{userId}", log.Target);

        // Job nền không có request nào để lấy IP. Cột nullable đúng vì ca này.
        Assert.Null(log.IpAddress);
    }

    [Fact]
    public async Task Tai_khoan_duoi_nguong_khong_bi_canh_bao()
    {
        using var db = TaoDb();
        await SeedNhieuPhienAsync(db, Guid.NewGuid(), soPhien: 4);

        var soTaiKhoan = await TaoService(db).ScanAndWarnAsync(BayGio);

        Assert.Equal(0, soTaiKhoan);
        Assert.Empty(db.AuditLogs);
    }

    // ---------------------------------------------------------------------------------------
    // Đếm theo PHIÊN, không theo ghế — quyết định nghiệp vụ khoá của task này
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Nam_ghe_cung_mot_phien_chi_tinh_la_mot_lan()
    {
        using var db = TaoDb();
        var userId = Guid.NewGuid();

        // Khách đặt vé cho cả gia đình: một phiên, năm ghế. Đây là MỘT lần giữ chỗ.
        await SeedPhienAsync(db, userId, "PHIEN-GIA-DINH", soGhe: 5, createdAt: BayGio.AddMinutes(-5));

        var soTaiKhoan = await TaoService(db).ScanAndWarnAsync(BayGio);

        // Nếu đếm theo dòng SeatHolds thì ca này ra 5 và khách mua nhiều vé nhất lại là người bị gắn
        // cờ đầu tiên. Đây là ca khoá của quyết định "đếm theo phiên".
        Assert.Equal(0, soTaiKhoan);
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    public async Task Bon_phien_moi_phien_ba_ghe_van_chi_la_bon_lan()
    {
        using var db = TaoDb();
        var userId = Guid.NewGuid();

        // 4 phiên × 3 ghế = 12 dòng SeatHolds, nhưng chỉ 4 lần giữ chỗ — vẫn dưới ngưỡng 5.
        for (var i = 0; i < 4; i++)
        {
            await SeedPhienAsync(db, userId, $"PHIEN-{i}", soGhe: 3, createdAt: BayGio.AddMinutes(-10));
        }

        var soTaiKhoan = await TaoService(db).ScanAndWarnAsync(BayGio);

        Assert.Equal(0, soTaiKhoan);
        Assert.Empty(db.AuditLogs);
    }

    // ---------------------------------------------------------------------------------------
    // Cửa sổ trượt
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Luot_giu_cu_hon_cua_so_khong_duoc_tinh()
    {
        using var db = TaoDb();
        var userId = Guid.NewGuid();

        // 4 phiên trong cửa sổ + 3 phiên đã cũ hơn 1 giờ. Tổng là 7 nhưng không phải "7 lần trong
        // một giờ" — đếm luỹ kế thì mọi khách trung thành đều bị gắn cờ.
        await SeedNhieuPhienAsync(db, userId, soPhien: 4, createdAt: BayGio.AddMinutes(-30));

        for (var i = 0; i < 3; i++)
        {
            await SeedPhienAsync(
                db, userId, $"PHIEN-CU-{i}", soGhe: 1, createdAt: BayGio.AddHours(-2));
        }

        var soTaiKhoan = await TaoService(db).ScanAndWarnAsync(BayGio);

        Assert.Equal(0, soTaiKhoan);
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    public async Task Luot_giu_dung_moc_dau_cua_so_van_duoc_tinh()
    {
        using var db = TaoDb();
        var userId = Guid.NewGuid();

        // Cửa sổ là [now - 1h, now], ĐÓNG hai đầu. Lượt giữ đúng mốc đầu vẫn nằm trong cửa sổ —
        // cùng lối "ExpiresAt < now" nghiêm ngặt của SeatHoldExpiryService, chỉ khác chiều.
        await SeedPhienAsync(db, userId, "PHIEN-DUNG-MOC", soGhe: 1, createdAt: BayGio - SeatHoldAbuseService.CuaSo);

        for (var i = 1; i < 5; i++)
        {
            await SeedPhienAsync(db, userId, $"PHIEN-{i}", soGhe: 1, createdAt: BayGio.AddMinutes(-10));
        }

        var soTaiKhoan = await TaoService(db).ScanAndWarnAsync(BayGio);

        Assert.Equal(1, soTaiKhoan);
    }

    // ---------------------------------------------------------------------------------------
    // Chống ghi trùng — chốt dễ quên nhất
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Quet_lan_hai_trong_cung_cua_so_khong_ghi_them_dong_nao()
    {
        using var db = TaoDb();
        var userId = Guid.NewGuid();
        await SeedNhieuPhienAsync(db, userId, soPhien: 6);

        var service = TaoService(db);

        Assert.Equal(1, await service.ScanAndWarnAsync(BayGio));

        // Nhịp quét 5 phút × cửa sổ 1 giờ: không chặn thì một hành vi sinh 12 dòng giống hệt nhau.
        Assert.Equal(0, await service.ScanAndWarnAsync(BayGio.AddMinutes(5)));
        Assert.Equal(1, await db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task Canh_bao_cu_hon_cua_so_khong_chan_canh_bao_moi()
    {
        using var db = TaoDb();
        var userId = Guid.NewGuid();
        await SeedNhieuPhienAsync(db, userId, soPhien: 5);

        // Cảnh báo của lượt trước đã trôi ra khỏi cửa sổ. Tài khoản vẫn giữ chỗ quá nhiều lần trong
        // giờ vừa qua thì phải được nhắc lại — "vẫn đang tiếp diễn" là thông tin Admin cần.
        db.AuditLogs.Add(new AuditLog
        {
            Action = AuditAction.Warning,
            UserId = userId,
            Target = $"Users:{userId}",
            CreatedAt = BayGio.AddHours(-2),
        });
        await db.SaveChangesAsync();

        var soTaiKhoan = await TaoService(db).ScanAndWarnAsync(BayGio);

        Assert.Equal(1, soTaiKhoan);
        Assert.Equal(2, await db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task Nhat_ky_khac_loai_khong_chan_canh_bao()
    {
        using var db = TaoDb();
        var userId = Guid.NewGuid();
        await SeedNhieuPhienAsync(db, userId, soPhien: 5);

        // Chỉ dòng Warning mới là "đã cảnh báo rồi". Một dòng Create của chính tài khoản đó trong
        // cùng cửa sổ (vd. họ vừa đăng ký) không được phép làm bỏ qua cảnh báo.
        db.AuditLogs.Add(new AuditLog
        {
            Action = AuditAction.Create,
            UserId = userId,
            Target = $"Users:{userId}",
            CreatedAt = BayGio.AddMinutes(-10),
        });
        await db.SaveChangesAsync();

        var soTaiKhoan = await TaoService(db).ScanAndWarnAsync(BayGio);

        Assert.Equal(1, soTaiKhoan);
        Assert.Single(db.AuditLogs, a => a.Action == AuditAction.Warning);
    }

    // ---------------------------------------------------------------------------------------
    // Nhiều tài khoản và ca rỗng
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Nhieu_tai_khoan_thi_chi_canh_bao_nguoi_vuot_nguong()
    {
        using var db = TaoDb();
        var nguoiVuot = Guid.NewGuid();
        var nguoiDuoi = Guid.NewGuid();

        await SeedNhieuPhienAsync(db, nguoiVuot, soPhien: 5);
        await SeedNhieuPhienAsync(db, nguoiDuoi, soPhien: 4);

        var soTaiKhoan = await TaoService(db).ScanAndWarnAsync(BayGio);

        Assert.Equal(1, soTaiKhoan);

        var log = Assert.Single(db.AuditLogs);
        Assert.Equal(nguoiVuot, log.UserId);
    }

    [Fact]
    public async Task Mot_luot_quet_canh_bao_nhieu_tai_khoan_thi_ghi_du_tung_nguoi_mot()
    {
        using var db = TaoDb();
        var nguoi1 = Guid.NewGuid();
        var nguoi2 = Guid.NewGuid();

        await SeedNhieuPhienAsync(db, nguoi1, soPhien: 5);
        await SeedNhieuPhienAsync(db, nguoi2, soPhien: 7);

        var soTaiKhoan = await TaoService(db).ScanAndWarnAsync(BayGio);

        Assert.Equal(2, soTaiKhoan);
        Assert.Equal(2, await db.AuditLogs.CountAsync());
        Assert.Single(db.AuditLogs, a => a.UserId == nguoi1);
        Assert.Single(db.AuditLogs, a => a.UserId == nguoi2);
    }

    [Fact]
    public async Task Khong_co_luot_giu_nao_thi_tra_ve_khong_va_khong_nem_loi()
    {
        using var db = TaoDb();

        // Lượt quét thường gặp nhất trong đời thật là lượt không có gì để làm — nó phải sạch.
        var soTaiKhoan = await TaoService(db).ScanAndWarnAsync(BayGio);

        Assert.Equal(0, soTaiKhoan);
        Assert.Empty(db.AuditLogs);
    }

    // ---------------------------------------------------------------------------------------
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Context mới trên cùng một CSDL InMemory. Gọi nhiều lần được để đọc lại bằng context sạch —
    /// cùng tên CSDL nên dữ liệu dùng chung.
    /// </summary>
    private AppDbContext TaoDb() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_tenCsdl)
            .Options);

    /// <summary>
    /// Service cần một <see cref="ILogger{T}"/>; test không kiểm phần log ứng dụng (nội dung log
    /// không phải hợp đồng) nên dùng logger rỗng. Cảnh báo bền vững nằm ở AuditLogs và đó là thứ
    /// các ca dưới đây khẳng định.
    /// </summary>
    private static SeatHoldAbuseService TaoService(AppDbContext db)
        => new(db, NullLogger<SeatHoldAbuseService>.Instance);

    /// <summary>
    /// Một phiên giữ chỗ của <paramref name="userId"/>: <paramref name="soGhe"/> dòng SeatHolds cùng
    /// <paramref name="sessionCode"/> — đúng hình dạng "một phiên giữ nhiều ghế" của docs/26 §1.
    /// </summary>
    private static async Task SeedPhienAsync(
        AppDbContext db,
        Guid userId,
        string sessionCode,
        int soGhe,
        DateTime createdAt)
    {
        for (var i = 0; i < soGhe; i++)
        {
            db.SeatHolds.Add(new SeatHold
            {
                TripId = Guid.NewGuid(),
                SeatId = Guid.NewGuid(),
                UserId = userId,
                SessionCode = sessionCode,
                Status = SeatHoldStatus.Holding,
                ExpiresAt = createdAt.AddMinutes(10),
                CreatedAt = createdAt,
            });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// <paramref name="soPhien"/> phiên khác nhau của cùng một tài khoản, mỗi phiên một ghế — dựng
    /// đúng thứ mà job đếm.
    /// </summary>
    private static async Task SeedNhieuPhienAsync(
        AppDbContext db,
        Guid userId,
        int soPhien,
        DateTime? createdAt = null)
    {
        var moc = createdAt ?? BayGio.AddMinutes(-20);

        for (var i = 0; i < soPhien; i++)
        {
            await SeedPhienAsync(db, userId, $"PHIEN-{i}", soGhe: 1, moc);
        }
    }
}
