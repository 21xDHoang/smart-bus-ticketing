using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test phần ruột job quét hold hết hạn — <see cref="SeatHoldExpiryService"/>
/// (task *"Migrate bảng SeatHoldLogs + job quét hold hết hạn"* — Vàng Thị Dăm).
///
/// Gọi thẳng service với một mốc <c>now</c> ghim sẵn, KHÔNG dựng host: vòng lặp
/// <see cref="SeatHoldExpiryBackgroundService"/> chạy theo nhịp 1 phút nên chờ nó là test chập chờn
/// và chậm. Phần "job có thật sự được đăng ký vào app không" nằm ở
/// <see cref="SeatHoldExpiryWiringTests"/>.
///
/// Dùng provider InMemory như phần còn lại của bộ test. Không seed Trips/Seats/Users: InMemory
/// không cưỡng chế khoá ngoại, mà truy vấn của job chỉ đọc đúng bảng SeatHolds.
///
/// ⚠️ InMemory CŨNG KHÔNG cưỡng chế unique index <c>(SeatHoldId, Action)</c> của SeatHoldLogs, nên
/// chốt chống ghi trùng nhật ký khi hai bản app cùng quét KHÔNG có test nào phủ ở đây — nó chỉ tồn
/// tại ở tầng PostgreSQL, xem docs/26-csdl-so-do-ghe.md §1. Đừng đọc file này xanh mà tưởng chốt
/// đó đã được kiểm.
/// </summary>
public class SeatHoldExpiryServiceTests
{
    /// <summary>Mốc "bây giờ" cố định — mọi lượt giữ trong file dựng quanh mốc này.</summary>
    private static readonly DateTime BayGio = new(2026, 10, 7, 9, 30, 0, DateTimeKind.Utc);

    /// <summary>
    /// Tên CSDL InMemory riêng cho từng ca test. xUnit dựng một instance lớp test mới cho mỗi ca nên
    /// mỗi ca có CSDL sạch — cùng tên là dùng chung dữ liệu.
    /// </summary>
    private readonly string _tenCsdl = $"hold-het-han-{Guid.NewGuid()}";

    // ---------------------------------------------------------------------------------------
    // Lật đúng lượt giữ đến hạn
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Luot_giu_qua_han_bi_lat_sang_Expired()
    {
        using var db = TaoDb();
        var luotGiu = await SeedHoldAsync(db, expiresAt: BayGio.AddSeconds(-1));

        var soLuot = await new SeatHoldExpiryService(db).ExpireDueHoldsAsync(BayGio);

        Assert.Equal(1, soLuot);

        // Đọc lại bằng context KHÁC để chắc chắn thay đổi đã xuống CSDL, không chỉ nằm trong change
        // tracker của context vừa dùng.
        using var dbDoc = TaoDb();
        var sauKhiQuet = await dbDoc.SeatHolds.SingleAsync(h => h.Id == luotGiu.Id);

        Assert.Equal(SeatHoldStatus.Expired, sauKhiQuet.Status);
        Assert.Equal(BayGio, sauKhiQuet.UpdatedAt);

        // Nhả ghế là LẬT TRẠNG THÁI, không phải xoá dòng: ghế phải trống cho khách sau là nhờ
        // partial unique index chỉ chặn Status = 'Holding', còn dòng ở lại làm lịch sử.
    }

    [Fact]
    public async Task Luot_giu_dung_moc_ExpiresAt_van_con_hieu_luc()
    {
        using var db = TaoDb();
        var luotGiu = await SeedHoldAsync(db, expiresAt: BayGio);

        var soLuot = await new SeatHoldExpiryService(db).ExpireDueHoldsAsync(BayGio);

        // US 3 hứa với khách "giữ chỗ 10 phút". Đúng mốc ExpiresAt lượt giữ vẫn còn hiệu lực, nên
        // điều kiện phải là ExpiresAt < now chứ không phải <=. Đây là ca dễ viết sai nhất của job.
        Assert.Equal(0, soLuot);
        Assert.Equal(SeatHoldStatus.Holding, luotGiu.Status);
        Assert.Null(luotGiu.UpdatedAt);
    }

    [Fact]
    public async Task Luot_giu_con_han_khong_bi_dong_den()
    {
        using var db = TaoDb();
        var luotGiu = await SeedHoldAsync(db, expiresAt: BayGio.AddSeconds(1));

        var soLuot = await new SeatHoldExpiryService(db).ExpireDueHoldsAsync(BayGio);

        Assert.Equal(0, soLuot);
        Assert.Equal(SeatHoldStatus.Holding, luotGiu.Status);
        Assert.Null(luotGiu.UpdatedAt);
    }

    [Theory]
    [InlineData(SeatHoldStatus.Released)]
    [InlineData(SeatHoldStatus.Confirmed)]
    [InlineData(SeatHoldStatus.Expired)]
    public async Task Luot_giu_da_ket_thuc_khong_bi_lat_lai(SeatHoldStatus trangThai)
    {
        using var db = TaoDb();

        // Quá hạn từ lâu nhưng đã kết thúc vì lý do khác (khách tự nhả, đã thành vé, hoặc lượt quét
        // trước đã lật). Chỉ Holding mới chặn ghế, nên ba trạng thái này không phải việc của job.
        var luotGiu = await SeedHoldAsync(
            db,
            expiresAt: BayGio.AddMinutes(-30),
            trangThai: trangThai);

        var soLuot = await new SeatHoldExpiryService(db).ExpireDueHoldsAsync(BayGio);

        Assert.Equal(0, soLuot);
        Assert.Equal(trangThai, luotGiu.Status);
        Assert.Empty(db.SeatHoldLogs);
    }

    // ---------------------------------------------------------------------------------------
    // Chạy lặp lại không được ghi thêm gì
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Luot_giu_da_Expired_san_khong_bi_ghi_de_UpdatedAt()
    {
        using var db = TaoDb();
        var mocLatLanTruoc = BayGio.AddHours(-3);

        var luotGiu = await SeedHoldAsync(
            db,
            expiresAt: BayGio.AddMinutes(-40),
            trangThai: SeatHoldStatus.Expired,
            updatedAt: mocLatLanTruoc);

        var soLuot = await new SeatHoldExpiryService(db).ExpireDueHoldsAsync(BayGio);

        // Không khớp điều kiện Holding nữa ⇒ không nằm trong lượt này. Nếu quên chốt Status, mỗi lượt
        // quét sẽ đè UpdatedAt của mọi lượt đã hết hạn từ trước — mốc "nhả lúc nào" mất giá trị.
        Assert.Equal(0, soLuot);
        Assert.Equal(mocLatLanTruoc, luotGiu.UpdatedAt);
    }

    [Fact]
    public async Task Quet_hai_luot_lien_tiep_luot_sau_khong_lat_them_va_khong_ghi_them_nhat_ky()
    {
        using var db = TaoDb();
        await SeedHoldAsync(db, expiresAt: BayGio.AddMinutes(-1));

        var service = new SeatHoldExpiryService(db);

        Assert.Equal(1, await service.ExpireDueHoldsAsync(BayGio));

        // Lượt sau (app khởi động lại, hoặc bản app thứ hai chạy song song) không được lật lại và
        // cũng không được ghi thêm một dòng nhật ký nữa cho cùng sự kiện.
        Assert.Equal(0, await service.ExpireDueHoldsAsync(BayGio));
        Assert.Equal(1, await db.SeatHoldLogs.CountAsync());
    }

    // ---------------------------------------------------------------------------------------
    // Nhiều lượt giữ và ca rỗng
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Chi_lat_luot_giu_den_han_va_tra_ve_dung_so_luong()
    {
        using var db = TaoDb();

        var quaHan1 = await SeedHoldAsync(db, expiresAt: BayGio.AddMinutes(-10));
        var quaHan2 = await SeedHoldAsync(db, expiresAt: BayGio.AddSeconds(-1));
        var conHan = await SeedHoldAsync(db, expiresAt: BayGio.AddMinutes(5));
        var daExpired = await SeedHoldAsync(
            db,
            expiresAt: BayGio.AddMinutes(-1),
            trangThai: SeatHoldStatus.Expired);

        var soLuot = await new SeatHoldExpiryService(db).ExpireDueHoldsAsync(BayGio);

        Assert.Equal(2, soLuot);
        Assert.Equal(SeatHoldStatus.Expired, quaHan1.Status);
        Assert.Equal(SeatHoldStatus.Expired, quaHan2.Status);
        Assert.Equal(SeatHoldStatus.Holding, conHan.Status);
        Assert.Null(daExpired.UpdatedAt);
    }

    [Fact]
    public async Task Khong_co_luot_giu_nao_thi_tra_ve_khong_va_khong_nem_loi()
    {
        using var db = TaoDb();

        // Lượt quét thường gặp nhất trong đời thật là lượt không có gì để làm — nó phải sạch.
        var soLuot = await new SeatHoldExpiryService(db).ExpireDueHoldsAsync(BayGio);

        Assert.Equal(0, soLuot);
        Assert.Empty(db.SeatHoldLogs);
    }

    // ---------------------------------------------------------------------------------------
    // Nhật ký SeatHoldLogs — phần mới của task này
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Moi_luot_giu_bi_lat_sinh_dung_mot_dong_nhat_ky()
    {
        using var db = TaoDb();
        var userId = Guid.NewGuid();
        var luotGiu = await SeedHoldAsync(
            db,
            expiresAt: BayGio.AddMinutes(-2),
            userId: userId,
            sessionCode: "PHIEN-ABC123");

        await new SeatHoldExpiryService(db).ExpireDueHoldsAsync(BayGio);

        var log = Assert.Single(db.SeatHoldLogs);

        Assert.Equal(luotGiu.Id, log.SeatHoldId);
        Assert.Equal(SeatHoldLogAction.Expired, log.Action);
        Assert.Equal(BayGio, log.CreatedAt);

        // Hai cột chép lại từ lượt giữ: có chúng thì câu hỏi "tài khoản này để hết hạn bao nhiêu
        // lần" và "phiên này có ghế nào bị nhả không" trả lời được bằng một truy vấn trên bảng nhật
        // ký, không phải join sang SeatHolds.
        Assert.Equal(userId, log.UserId);
        Assert.Equal("PHIEN-ABC123", log.SessionCode);
    }

    [Fact]
    public async Task Luot_giu_con_han_khong_sinh_dong_nhat_ky_nao()
    {
        using var db = TaoDb();
        await SeedHoldAsync(db, expiresAt: BayGio.AddMinutes(5));

        await new SeatHoldExpiryService(db).ExpireDueHoldsAsync(BayGio);

        Assert.Empty(db.SeatHoldLogs);
    }

    [Fact]
    public async Task Ba_ghe_cung_phien_bi_lat_thi_sinh_ba_dong_nhat_ky_cung_ma_phien()
    {
        using var db = TaoDb();

        // Một phiên giữ nhiều ghế = nhiều dòng SeatHolds cùng SessionCode (docs/26 §1), nên nhật ký
        // cũng phải là ba dòng chứ không phải một — gộp lại là mất dấu từng ghế.
        var userId = Guid.NewGuid();
        await SeedHoldAsync(db, expiresAt: BayGio.AddMinutes(-1), userId: userId, sessionCode: "PHIEN-3GHE");
        await SeedHoldAsync(db, expiresAt: BayGio.AddMinutes(-1), userId: userId, sessionCode: "PHIEN-3GHE");
        await SeedHoldAsync(db, expiresAt: BayGio.AddMinutes(-1), userId: userId, sessionCode: "PHIEN-3GHE");

        var soLuot = await new SeatHoldExpiryService(db).ExpireDueHoldsAsync(BayGio);

        Assert.Equal(3, soLuot);
        Assert.Equal(3, await db.SeatHoldLogs.CountAsync());
        Assert.Equal(3, await db.SeatHoldLogs.CountAsync(l => l.SessionCode == "PHIEN-3GHE"));
        // Ba dòng nhật ký phải trỏ vào BA lượt giữ khác nhau, không phải cùng một dòng ba lần.
        Assert.Equal(3, await db.SeatHoldLogs.Select(l => l.SeatHoldId).Distinct().CountAsync());
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
    /// Một lượt giữ ghế quanh mốc <see cref="BayGio"/>. Mặc định là lượt giữ bình thường: đang
    /// Holding, chưa gia hạn. Ca cần trạng thái khác truyền <paramref name="trangThai"/> tường minh.
    /// </summary>
    private static async Task<SeatHold> SeedHoldAsync(
        AppDbContext db,
        DateTime expiresAt,
        SeatHoldStatus trangThai = SeatHoldStatus.Holding,
        Guid? userId = null,
        string sessionCode = "PHIEN-MAC-DINH",
        DateTime? updatedAt = null)
    {
        var luotGiu = new SeatHold
        {
            TripId = Guid.NewGuid(),
            SeatId = Guid.NewGuid(),
            UserId = userId ?? Guid.NewGuid(),
            SessionCode = sessionCode,
            Status = trangThai,
            ExpiresAt = expiresAt,
            CreatedAt = expiresAt.AddMinutes(-10),
            UpdatedAt = updatedAt,
        };

        db.SeatHolds.Add(luotGiu);
        await db.SaveChangesAsync();

        return luotGiu;
    }
}
