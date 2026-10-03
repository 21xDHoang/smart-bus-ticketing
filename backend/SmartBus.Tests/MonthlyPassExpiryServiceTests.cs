using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test phần ruột job quét vé tháng hết hạn — <see cref="MonthlyPassExpiryService"/>
/// (task *"BackgroundService tự động chuyển vé tháng hết hạn sang trạng thái Expired"* —
/// Nguyễn Duy Kiên).
///
/// Gọi thẳng service với một mốc <c>now</c> ghim sẵn, KHÔNG dựng host: vòng lặp
/// <see cref="MonthlyPassExpiryBackgroundService"/> chạy theo nhịp 15 phút nên chờ nó là test chập
/// chờn và chậm. Phần "job có thật sự được đăng ký vào app không" nằm ở
/// <see cref="MonthlyPassExpiryWiringTests"/>.
///
/// Dùng provider InMemory như phần còn lại của bộ test. Không seed Users/Routes/PassTypes: InMemory
/// không cưỡng chế khoá ngoại, mà truy vấn của job chỉ đọc đúng bảng MonthlyPasses.
/// </summary>
public class MonthlyPassExpiryServiceTests
{
    /// <summary>Mốc "bây giờ" cố định — mọi vé trong file dựng quanh mốc này.</summary>
    private static readonly DateTime BayGio = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Tên CSDL InMemory riêng cho từng ca test. xUnit dựng một instance lớp test mới cho mỗi ca nên
    /// mỗi ca có CSDL sạch — cùng tên là dùng chung dữ liệu.
    /// </summary>
    private readonly string _tenCsdl = $"ve-thang-het-han-{Guid.NewGuid()}";

    // ---------------------------------------------------------------------------------------
    // Lật đúng vé đến hạn
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Ve_da_qua_ValidTo_bi_lat_sang_Expired()
    {
        using var db = TaoDb();
        var ve = await SeedVeAsync(db, validTo: BayGio.AddSeconds(-1));

        var soVe = await new MonthlyPassExpiryService(db).ExpireDuePassesAsync(BayGio);

        Assert.Equal(1, soVe);

        // Đọc lại bằng context KHÁC để chắc chắn thay đổi đã xuống CSDL, không chỉ nằm trong change
        // tracker của context vừa dùng.
        using var dbDoc = TaoDb();
        var sauKhiQuet = await dbDoc.MonthlyPasses.SingleAsync(p => p.Id == ve.Id);

        Assert.Equal(MonthlyPassStatus.Expired, sauKhiQuet.Status);
        Assert.Equal(BayGio, sauKhiQuet.UpdatedAt);

        // Vé hết hạn là hết hiệu lực, KHÔNG phải bị xoá — dòng ở lại làm lịch sử mua bán (A8.2).
    }

    [Fact]
    public async Task Ve_het_han_dung_moc_ValidTo_van_con_hieu_luc()
    {
        using var db = TaoDb();
        var ve = await SeedVeAsync(db, validTo: BayGio);

        var soVe = await new MonthlyPassExpiryService(db).ExpireDuePassesAsync(BayGio);

        // Hợp đồng API: khoảng hiệu lực "tính cả hai mốc". Đúng lúc ValidTo vé vẫn dùng được, nên
        // điều kiện phải là ValidTo < now chứ không phải <=. Đây là ca dễ viết sai nhất của job.
        Assert.Equal(0, soVe);
        Assert.Equal(MonthlyPassStatus.Active, ve.Status);
        Assert.Null(ve.UpdatedAt);
    }

    [Fact]
    public async Task Ve_con_han_khong_bi_dong_den()
    {
        using var db = TaoDb();
        var ve = await SeedVeAsync(db, validTo: BayGio.AddSeconds(1));

        var soVe = await new MonthlyPassExpiryService(db).ExpireDuePassesAsync(BayGio);

        Assert.Equal(0, soVe);
        Assert.Equal(MonthlyPassStatus.Active, ve.Status);
        Assert.Null(ve.UpdatedAt);
    }

    [Fact]
    public async Task Ve_dang_ky_truoc_cho_ky_sau_khong_bi_lat()
    {
        using var db = TaoDb();

        // Vé mua trước cho kỳ sau: chưa tới ngày hiệu lực nhưng đã Active trong cột Status (đúng
        // như hợp đồng mô tả — hiệu lực hỏi bằng cặp mốc, không hỏi Status).
        var ve = await SeedVeAsync(
            db,
            validFrom: BayGio.AddDays(5),
            validTo: BayGio.AddDays(35));

        var soVe = await new MonthlyPassExpiryService(db).ExpireDuePassesAsync(BayGio);

        Assert.Equal(0, soVe);
        Assert.Equal(MonthlyPassStatus.Active, ve.Status);
    }

    // ---------------------------------------------------------------------------------------
    // Chạy lặp lại không được ghi thêm gì
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Ve_da_Expired_san_khong_bi_ghi_de_UpdatedAt()
    {
        using var db = TaoDb();
        var mocLatLanTruoc = BayGio.AddHours(-3);

        // Vé lượt quét trước đã lật: Status đã Expired, ValidTo cũng đã qua.
        var ve = await SeedVeAsync(
            db,
            validTo: BayGio.AddDays(-2),
            trangThai: MonthlyPassStatus.Expired,
            updatedAt: mocLatLanTruoc);

        var soVe = await new MonthlyPassExpiryService(db).ExpireDuePassesAsync(BayGio);

        // Không khớp điều kiện Active nữa ⇒ không nằm trong lượt này. Nếu quên chốt Status, mỗi lượt
        // quét sẽ đè UpdatedAt của mọi vé đã hết hạn từ trước — mốc "lật lúc nào" mất giá trị.
        Assert.Equal(0, soVe);
        Assert.Equal(mocLatLanTruoc, ve.UpdatedAt);
    }

    [Fact]
    public async Task Quet_hai_luot_lien_tiep_luot_sau_khong_lat_them()
    {
        using var db = TaoDb();
        await SeedVeAsync(db, validTo: BayGio.AddMinutes(-1));

        var service = new MonthlyPassExpiryService(db);

        Assert.Equal(1, await service.ExpireDuePassesAsync(BayGio));
        // Lượt sau (app khởi động lại, hoặc bản app thứ hai chạy song song) không được lật lại.
        Assert.Equal(0, await service.ExpireDuePassesAsync(BayGio));
    }

    // ---------------------------------------------------------------------------------------
    // Nhiều vé và ca rỗng
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Chi_lat_ve_den_han_va_tra_ve_dung_so_luong()
    {
        using var db = TaoDb();

        var quaHan1 = await SeedVeAsync(db, validTo: BayGio.AddDays(-10));
        var quaHan2 = await SeedVeAsync(db, validTo: BayGio.AddMinutes(-1));
        var conHan = await SeedVeAsync(db, validTo: BayGio.AddDays(3));
        var dangExpired = await SeedVeAsync(
            db,
            validTo: BayGio.AddDays(-1),
            trangThai: MonthlyPassStatus.Expired);

        var soVe = await new MonthlyPassExpiryService(db).ExpireDuePassesAsync(BayGio);

        Assert.Equal(2, soVe);
        Assert.Equal(MonthlyPassStatus.Expired, quaHan1.Status);
        Assert.Equal(MonthlyPassStatus.Expired, quaHan2.Status);
        Assert.Equal(MonthlyPassStatus.Active, conHan.Status);
        Assert.Null(dangExpired.UpdatedAt);
    }

    [Fact]
    public async Task Khong_co_ve_nao_thi_tra_ve_khong_va_khong_nem_loi()
    {
        using var db = TaoDb();

        // Lượt quét thường gặp nhất trong đời thật là lượt không có gì để làm — nó phải sạch.
        var soVe = await new MonthlyPassExpiryService(db).ExpireDuePassesAsync(BayGio);

        Assert.Equal(0, soVe);
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
    /// Một vé tháng Active quanh mốc <see cref="BayGio"/>. Mặc định ValidFrom = ValidTo - 1 tháng
    /// (vé một tháng bình thường); ca cần vé tương lai truyền <paramref name="validFrom"/> tường minh.
    /// </summary>
    private static async Task<MonthlyPass> SeedVeAsync(
        AppDbContext db,
        DateTime validTo,
        DateTime? validFrom = null,
        MonthlyPassStatus trangThai = MonthlyPassStatus.Active,
        DateTime? updatedAt = null)
    {
        var ve = new MonthlyPass
        {
            UserId = Guid.NewGuid(),
            RouteId = Guid.NewGuid(),
            PassTypeId = Guid.NewGuid(),
            Price = 200_000m,
            Code = $"MP-01-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
            ValidFrom = validFrom ?? validTo.AddMonths(-1),
            ValidTo = validTo,
            Status = trangThai,
            UpdatedAt = updatedAt,
        };

        db.MonthlyPasses.Add(ve);
        await db.SaveChangesAsync();

        return ve;
    }
}
