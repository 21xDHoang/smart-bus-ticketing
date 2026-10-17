using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.MonthlyPasses.Admin;
using SmartBus.Api.Dtos.PassTypes;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Quản trị vé tháng — danh sách lọc/phân trang, huỷ/mở lại vé, thống kê, và CRUD loại vé
/// (GET/PATCH /monthly-passes*, /pass-types). Chạy trên InMemory + gọi thẳng service, cùng lối
/// <see cref="BusAssignmentTests"/>.
///
/// Ba luật dễ sai được ghim ở đây:
///   - Vé Cancelled là CHẶN CỨNG — không còn xuất hiện trong GET /monthly-passes/me dù cặp mốc
///     vẫn bao quanh "bây giờ".
///   - Thống kê đếm theo hiệu lực thật (cặp mốc), KHÔNG theo cột Status của Active/Expired —
///     cột đó có độ trễ do job quét.
///   - Loại vé đã được dùng không xoá được; đổi giá không làm vé đã bán nhảy theo.
/// </summary>
public class MonthlyPassAdminTests
{
    private const string PassTypeCode = "OneMonth";

    [Fact]
    public async Task Danh_sach_loc_theo_tu_khoa_trang_thai_tuyen_va_loai_ve()
    {
        await using var db = TaoDb();
        var seed = await SeedAsync(db);
        var service = new MonthlyPassAdminService(db);

        // Không lọc: đủ bốn vé.
        var all = await service.ListAsync(new ListMonthlyPassesRequest());
        Assert.Equal(4, all.Total);

        // Từ khoá khớp mã vé.
        var byCode = await service.ListAsync(new ListMonthlyPassesRequest { Search = "bbbbbb" });
        Assert.Equal(seed.PassUpcoming.Id, Assert.Single(byCode.Items).Id);

        // Từ khoá khớp SĐT hành khách.
        var byPhone = await service.ListAsync(new ListMonthlyPassesRequest { Search = "0901" });
        Assert.Equal(4, byPhone.Total);

        // Lọc theo cột Status — chỉ vé đã huỷ.
        var cancelled = await service.ListAsync(new ListMonthlyPassesRequest { Status = "cancelled" });
        Assert.Equal(seed.PassCancelled.Id, Assert.Single(cancelled.Items).Id);

        // Trạng thái lạ = bộ lọc không khớp gì, không phải lỗi.
        var unknown = await service.ListAsync(new ListMonthlyPassesRequest { Status = "KhongCo" });
        Assert.Empty(unknown.Items);
        Assert.Equal(0, unknown.Total);

        // Lọc theo tuyến và loại vé.
        var byRoute = await service.ListAsync(new ListMonthlyPassesRequest { RouteId = seed.Route.Id });
        Assert.Equal(4, byRoute.Total);

        var byType = await service.ListAsync(new ListMonthlyPassesRequest { PassTypeCode = PassTypeCode });
        Assert.Equal(4, byType.Total);

        var byWrongType = await service.ListAsync(new ListMonthlyPassesRequest { PassTypeCode = "TwelveMonths" });
        Assert.Equal(0, byWrongType.Total);
    }

    [Fact]
    public async Task Danh_sach_phan_trang_va_moi_nhat_truoc()
    {
        await using var db = TaoDb();
        var seed = await SeedAsync(db);
        var service = new MonthlyPassAdminService(db);

        var page1 = await service.ListAsync(new ListMonthlyPassesRequest { Page = 1, PageSize = 2 });
        Assert.Equal(4, page1.Total);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(seed.PassActive.Id, page1.Items[0].Id);   // CreatedAt mới nhất đứng đầu

        var page2 = await service.ListAsync(new ListMonthlyPassesRequest { Page = 2, PageSize = 2 });
        Assert.Equal(2, page2.Items.Count);
        Assert.Empty(page1.Items.Select(p => p.Id).Intersect(page2.Items.Select(p => p.Id)));
    }

    [Fact]
    public async Task Huy_ve_la_chan_cung_khong_con_trong_ve_cua_toi()
    {
        await using var db = TaoDb();
        var seed = await SeedAsync(db);
        var service = new MonthlyPassAdminService(db);
        var lookup = new MonthlyPassLookupService(db);

        // Trước khi huỷ, vé đang hiệu lực nằm trong "vé của tôi".
        var truoc = await lookup.GetActiveAsync(seed.User.Id);
        Assert.Contains(truoc, p => p.Id == seed.PassActive.Id);

        var result = await service.UpdateStatusAsync(
            seed.PassActive.Id,
            new UpdateMonthlyPassStatusRequest { Status = "Cancelled" });

        Assert.True(result.Success);
        Assert.Equal("Cancelled", result.Data!.Status);
        Assert.NotNull(result.Data.UpdatedAt);

        // Cặp mốc vẫn bao quanh "bây giờ" nhưng Cancelled là chặn cứng → không còn hiệu lực.
        var sau = await lookup.GetActiveAsync(seed.User.Id);
        Assert.DoesNotContain(sau, p => p.Id == seed.PassActive.Id);

        // Huỷ lần hai là thao tác thừa → 409.
        var lanHai = await service.UpdateStatusAsync(
            seed.PassActive.Id,
            new UpdateMonthlyPassStatusRequest { Status = "Cancelled" });
        Assert.False(lanHai.Success);
        Assert.Equal(ServiceErrorKind.Conflict, lanHai.ErrorKind);
    }

    [Fact]
    public async Task Khong_huy_duoc_ve_da_het_han()
    {
        await using var db = TaoDb();
        var seed = await SeedAsync(db);
        var service = new MonthlyPassAdminService(db);

        var result = await service.UpdateStatusAsync(
            seed.PassExpired.Id,
            new UpdateMonthlyPassStatusRequest { Status = "Cancelled" });

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorKind.Conflict, result.ErrorKind);

        var stored = await db.MonthlyPasses.AsNoTracking().SingleAsync(p => p.Id == seed.PassExpired.Id);
        Assert.Equal(MonthlyPassStatus.Active, stored.Status);   // không âm thầm ghi đè
    }

    [Fact]
    public async Task Mo_lai_ve_da_huy_khi_con_han()
    {
        await using var db = TaoDb();
        var seed = await SeedAsync(db);
        var service = new MonthlyPassAdminService(db);

        var result = await service.UpdateStatusAsync(
            seed.PassCancelled.Id,
            new UpdateMonthlyPassStatusRequest { Status = "Active" });

        Assert.True(result.Success);
        Assert.Equal("Active", result.Data!.Status);
    }

    [Fact]
    public async Task Thong_ke_dem_dung_bon_nhom_va_doanh_thu()
    {
        await using var db = TaoDb();
        await SeedAsync(db);
        var service = new MonthlyPassAdminService(db);

        var stats = await service.GetStatisticsAsync();

        Assert.Equal(4, stats.TotalCount);
        Assert.Equal(1, stats.ActiveCount);      // PassActive
        Assert.Equal(1, stats.UpcomingCount);    // PassUpcoming
        Assert.Equal(1, stats.ExpiredCount);     // PassExpired (cột vẫn Active — đếm theo ngày)
        Assert.Equal(1, stats.CancelledCount);   // PassCancelled

        // Doanh thu cộng cả vé đã huỷ: huỷ không hoàn tiền.
        Assert.Equal(200_000m + 550_000m + 1_000_000m + 1_800_000m, stats.TotalRevenue);

        // Chỉ vé tạo trong tháng hiện tại mới vào doanh thu tháng.
        Assert.Equal(200_000m, stats.RevenueThisMonth);
    }

    [Fact]
    public async Task Tao_loai_ve_trung_ma_tra_conflict_khong_phan_biet_hoa_thuong()
    {
        await using var db = TaoDb();
        await SeedAsync(db);
        var service = new PassTypeService(db);

        var result = await service.CreateAsync(new CreatePassTypeRequest
        {
            Code = "onemonth",
            Name = "Vé tháng 1 tháng (bản sao)",
            DurationMonths = 1,
            Price = 190_000,
        });

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorKind.Conflict, result.ErrorKind);
    }

    [Fact]
    public async Task Xoa_loai_ve_chua_dung_duoc_con_loai_ve_da_co_ve_thang_thi_khong()
    {
        await using var db = TaoDb();
        var seed = await SeedAsync(db);
        var service = new PassTypeService(db);

        var unused = new PassType
        {
            Code = "SixMonths",
            Name = "Vé tháng 6 tháng",
            DurationMonths = 6,
            Price = 1_000_000,
        };
        db.PassTypes.Add(unused);
        await db.SaveChangesAsync();

        var xoaDuoc = await service.DeleteAsync(unused.Id);
        Assert.True(xoaDuoc.Success);
        Assert.False(await db.PassTypes.AnyAsync(pt => pt.Id == unused.Id));

        // Loại vé đang có bốn vé tháng tham chiếu → 409, dòng vẫn còn.
        var xoaBiChan = await service.DeleteAsync(seed.PassType.Id);
        Assert.False(xoaBiChan.Success);
        Assert.Equal(ServiceErrorKind.Conflict, xoaBiChan.ErrorKind);
        Assert.True(await db.PassTypes.AnyAsync(pt => pt.Id == seed.PassType.Id));
    }

    [Fact]
    public async Task Sua_gia_loai_ve_khong_lam_gia_ve_da_ban_nhay_theo()
    {
        await using var db = TaoDb();
        var seed = await SeedAsync(db);
        var service = new PassTypeService(db);

        var result = await service.UpdateAsync(seed.PassType.Id, new UpdatePassTypeRequest
        {
            Name = "Vé tháng 1 tháng",
            DurationMonths = 1,
            Price = 250_000,
        });

        Assert.True(result.Success);
        Assert.Equal(250_000m, result.Data!.Price);
        Assert.Equal(4, result.Data.MonthlyPassCount);

        // Vé đã bán giữ nguyên giá chụp lúc mua.
        var pass = await db.MonthlyPasses.AsNoTracking().SingleAsync(p => p.Id == seed.PassActive.Id);
        Assert.Equal(200_000m, pass.Price);
    }

    [Fact]
    public async Task Danh_sach_loai_ve_kem_so_ve_dang_dung()
    {
        await using var db = TaoDb();
        var seed = await SeedAsync(db);
        var service = new PassTypeService(db);

        var result = await service.ListAsync();

        var row = Assert.Single(result);
        Assert.Equal(PassTypeCode, row.Code);
        Assert.Equal(4, row.MonthlyPassCount);
        Assert.Equal(seed.PassType.Id, row.Id);
    }

    // ---------------------------------------------------------------------------------------
    // Hạ tầng
    // ---------------------------------------------------------------------------------------

    private static AppDbContext TaoDb()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"ve-thang-quan-tri-{Guid.NewGuid()}")
            .Options);

    private sealed record Seed(
        User User,
        Route Route,
        PassType PassType,
        MonthlyPass PassActive,
        MonthlyPass PassUpcoming,
        MonthlyPass PassExpired,
        MonthlyPass PassCancelled);

    /// <summary>
    /// Một hành khách, một tuyến, một loại vé và bốn vé tháng phủ đủ bốn nhóm thống kê:
    /// còn hạn / kỳ sau / đã quá hạn (cột Status vẫn Active để chứng minh thống kê không đọc cột)
    /// / đã huỷ. Mốc thời gian tương đối để test không hết hạn theo lịch.
    /// </summary>
    private static async Task<Seed> SeedAsync(AppDbContext db)
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var lastMonth = monthStart.AddMonths(-1);

        var role = new Role { Code = "Passenger", Name = "Hành khách" };
        var user = new User
        {
            PhoneNumber = "0901234567",
            FullName = "Nguyễn Văn A",
            PasswordHash = "khong-dung-de-dang-nhap",
            RoleId = role.Id,
        };

        var route = new Route { Code = "01", Name = "Bến Thành — Chợ Lớn" };

        var passType = new PassType
        {
            Code = PassTypeCode,
            Name = "Vé tháng 1 tháng",
            DurationMonths = 1,
            Price = 200_000,
            CreatedAt = lastMonth,
        };

        var active = new MonthlyPass
        {
            Code = "MP-01-AAAAAA",
            UserId = user.Id,
            RouteId = route.Id,
            PassTypeId = passType.Id,
            Price = 200_000,
            ValidFrom = now.AddDays(-10),
            ValidTo = now.AddDays(20),
            Status = MonthlyPassStatus.Active,
            CreatedAt = now,
        };

        var upcoming = new MonthlyPass
        {
            Code = "MP-01-BBBBBB",
            UserId = user.Id,
            RouteId = route.Id,
            PassTypeId = passType.Id,
            Price = 550_000,
            ValidFrom = now.AddDays(10),
            ValidTo = now.AddDays(40),
            Status = MonthlyPassStatus.Active,
            CreatedAt = lastMonth,
        };

        var expired = new MonthlyPass
        {
            Code = "MP-01-CCCCCC",
            UserId = user.Id,
            RouteId = route.Id,
            PassTypeId = passType.Id,
            Price = 1_000_000,
            ValidFrom = now.AddDays(-40),
            ValidTo = now.AddDays(-10),
            // Cố ý để Active: job quét của Kiên chưa chạy — thống kê phải đếm theo ngày.
            Status = MonthlyPassStatus.Active,
            CreatedAt = lastMonth,
        };

        var cancelled = new MonthlyPass
        {
            Code = "MP-01-DDDDDD",
            UserId = user.Id,
            RouteId = route.Id,
            PassTypeId = passType.Id,
            Price = 1_800_000,
            ValidFrom = now.AddDays(-5),
            ValidTo = now.AddDays(25),
            Status = MonthlyPassStatus.Cancelled,
            UpdatedAt = now.AddDays(-1),
            CreatedAt = lastMonth,
        };

        db.Roles.Add(role);
        db.Users.Add(user);
        db.Routes.Add(route);
        db.PassTypes.Add(passType);
        db.MonthlyPasses.AddRange(active, upcoming, expired, cancelled);

        await db.SaveChangesAsync();

        return new Seed(user, route, passType, active, upcoming, expired, cancelled);
    }
}
