using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Trips;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;
using RouteEntity = SmartBus.Api.Entities.Route;

namespace SmartBus.Tests;

/// <summary>
/// Test lớp bọc <see cref="FleetAwareRouteTripsService"/> — sửa ngữ nghĩa "trùng khung giờ" của lượt
/// thêm chuyến lẻ cho đội xe nhiều chiếc (nợ của đợt mở rộng đội xe 17/10/2026, Phùng Duy Hoàng).
///
/// <para>
/// Ngữ nghĩa phải ghim:
/// <list type="bullet">
///   <item>cùng TUYẾN, KHÁC XE, khung giờ chồng lấn → hợp lệ. Đây là lỗi cũ: lớp trần coi mọi chuyến
///   cùng tuyến là xung đột nên tuyến nhiều xe không thêm được chuyến lẻ nào;</item>
///   <item>cùng XE chồng khung giờ → 409;</item>
///   <item>cùng TUYẾN trùng ĐÚNG giờ khởi hành (dù khác xe) → 409;</item>
///   <item>nối đuôi — chuyến trước đến đúng lúc chuyến sau khởi hành — không tính chồng;</item>
///   <item>chuỗi DI thật có lớp này trong chuỗi: resolve <see cref="IRouteTripsService"/> phải ra
///   <see cref="NotifyingRouteTripsService"/> và lượt tạo chuyến đi xuyên qua lớp hiểu đội xe.</item>
/// </list>
/// </para>
/// </summary>
public class FleetAwareRouteTripsServiceTests
{
    private static readonly DateTimeOffset GioBatDau = new(2026, 10, 17, 6, 0, 0, TimeSpan.Zero);

    // =======================================================================================
    // 1. Ngữ nghĩa trùng giờ
    // =======================================================================================

    [Fact]
    public async Task Cung_tuyen_khac_xe_duoc_tao_du_khung_gio_chong_lan()
    {
        await using var db = TaoDb();
        var route = await SeedTuyenAsync(db);
        var bus1 = await SeedXeAsync(db, "29B-111.11");
        var bus2 = await SeedXeAsync(db, "29B-222.22");

        await SeedChuyenAsync(db, route.Id, bus1.Id, GioBatDau, GioBatDau.AddHours(1));

        var service = TaoService(db);
        var result = await service.CreateAsync(route.Id, TaoRequest(bus2.Id, GioBatDau.AddMinutes(30), GioBatDau.AddMinutes(90)));

        Assert.True(result.Success);
        Assert.Equal(bus2.Id, result.Data!.BusId);
        Assert.Equal("29B-222.22", result.Data.BusLicensePlate);
        Assert.Equal(2, await db.Trips.CountAsync());
    }

    [Fact]
    public async Task Cung_xe_chong_khung_gio_bi_chan()
    {
        await using var db = TaoDb();
        var route = await SeedTuyenAsync(db);
        var bus = await SeedXeAsync(db);

        await SeedChuyenAsync(db, route.Id, bus.Id, GioBatDau, GioBatDau.AddHours(1));

        var service = TaoService(db);
        var result = await service.CreateAsync(route.Id, TaoRequest(bus.Id, GioBatDau.AddMinutes(30), GioBatDau.AddMinutes(90)));

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorKind.Conflict, result.ErrorKind);
        Assert.Contains("khung giờ", result.Error);
        Assert.Equal(1, await db.Trips.CountAsync());
    }

    [Fact]
    public async Task Cung_tuyen_trung_dung_gio_khoi_hanh_bi_chan_du_khac_xe()
    {
        await using var db = TaoDb();
        var route = await SeedTuyenAsync(db);
        var bus1 = await SeedXeAsync(db, "29B-111.11");
        var bus2 = await SeedXeAsync(db, "29B-222.22");

        await SeedChuyenAsync(db, route.Id, bus1.Id, GioBatDau, GioBatDau.AddHours(1));

        var service = TaoService(db);
        var result = await service.CreateAsync(route.Id, TaoRequest(bus2.Id, GioBatDau, GioBatDau.AddMinutes(45)));

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorKind.Conflict, result.ErrorKind);
        Assert.Contains("2026-10-17 06:00 UTC", result.Error);
        Assert.Equal(1, await db.Trips.CountAsync());
    }

    [Fact]
    public async Task Noi_duoi_dung_luc_chuyen_truoc_den_khong_tinh_chong()
    {
        await using var db = TaoDb();
        var route = await SeedTuyenAsync(db);
        var bus = await SeedXeAsync(db);

        await SeedChuyenAsync(db, route.Id, bus.Id, GioBatDau, GioBatDau.AddHours(1));

        // Chuyến cũ đến 07:00, chuyến mới khởi hành đúng 07:00 — nối đuôi, không chồng.
        var service = TaoService(db);
        var result = await service.CreateAsync(route.Id, TaoRequest(bus.Id, GioBatDau.AddHours(1), GioBatDau.AddMinutes(90)));

        Assert.True(result.Success);
        Assert.Equal(2, await db.Trips.CountAsync());
    }

    [Fact]
    public async Task Chuyen_cu_chua_co_gio_den_van_bi_coi_la_mot_moc()
    {
        await using var db = TaoDb();
        var route = await SeedTuyenAsync(db);
        var bus = await SeedXeAsync(db);

        // ArrivalTime null: chuyến cũ chỉ là mốc xuất phát 06:15. Khung mới 06:00–06:30 phủ qua mốc
        // đó nên vẫn tính chồng — biểu thức (ArrivalTime ?? DepartureTime) giữ được trường hợp này.
        await SeedChuyenAsync(db, route.Id, bus.Id, GioBatDau.AddMinutes(15), null);

        var service = TaoService(db);
        var result = await service.CreateAsync(route.Id, TaoRequest(bus.Id, GioBatDau, GioBatDau.AddMinutes(30)));

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorKind.Conflict, result.ErrorKind);
        Assert.Equal(1, await db.Trips.CountAsync());
    }

    // =======================================================================================
    // 2. Các kiểm tra còn lại giữ nguyên
    // =======================================================================================

    [Fact]
    public async Task Tuyen_khong_ton_tai_tra_404()
    {
        await using var db = TaoDb();
        var bus = await SeedXeAsync(db);
        var service = TaoService(db);

        var result = await service.CreateAsync(Guid.NewGuid(), TaoRequest(bus.Id, GioBatDau, null));

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorKind.NotFound, result.ErrorKind);
    }

    [Fact]
    public async Task Xe_bao_duong_tra_409()
    {
        await using var db = TaoDb();
        var route = await SeedTuyenAsync(db);
        var bus = await SeedXeAsync(db, status: BusStatus.Maintenance);
        var service = TaoService(db);

        var result = await service.CreateAsync(route.Id, TaoRequest(bus.Id, GioBatDau, null));

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorKind.Conflict, result.ErrorKind);
    }

    [Fact]
    public async Task Gio_den_truoc_gio_di_bao_loi_arrivalTime()
    {
        await using var db = TaoDb();
        var route = await SeedTuyenAsync(db);
        var bus = await SeedXeAsync(db);
        var service = TaoService(db);

        var result = await service.CreateAsync(route.Id, TaoRequest(bus.Id, GioBatDau, GioBatDau.AddMinutes(-30)));

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorKind.Invalid, result.ErrorKind);
        Assert.Contains("arrivalTime", result.Errors!.Keys);
        Assert.Empty(await db.Trips.AsNoTracking().ToListAsync());
    }

    // =======================================================================================
    // 3. Chuỗi DI thật
    // =======================================================================================

    [Fact]
    public async Task Chuoi_DI_that_tao_chuyen_di_qua_lop_hieu_doi_xe()
    {
        using var factory = new TestAppFactory();
        var route = new RouteEntity
        {
            Id = Guid.NewGuid(),
            Code = "01",
            Name = "Bến xe Mỹ Đình — Bến xe Gia Lâm",
            Origin = "Bến xe Mỹ Đình",
            Destination = "Bến xe Gia Lâm",
            DistanceKm = 20m,
        };
        var bus1 = new Bus
        {
            Id = Guid.NewGuid(),
            LicensePlate = "29B-111.11",
            BusType = "Xe buýt 45 chỗ",
            Capacity = 45,
            Status = BusStatus.Active,
        };
        var bus2 = new Bus
        {
            Id = Guid.NewGuid(),
            LicensePlate = "29B-222.22",
            BusType = "Xe buýt 45 chỗ",
            Capacity = 45,
            Status = BusStatus.Active,
        };

        await factory.SeedAsync(db =>
        {
            db.Routes.Add(route);
            db.Buses.Add(bus1);
            db.Buses.Add(bus2);
            db.Trips.Add(new Trip
            {
                RouteId = route.Id,
                BusId = bus1.Id,
                DepartureTime = GioBatDau.UtcDateTime,
                ArrivalTime = GioBatDau.AddHours(1).UtcDateTime,
            });
        });

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IRouteTripsService>();

        Assert.IsType<NotifyingRouteTripsService>(service);

        // Cùng tuyến, khác xe, chồng khung giờ — phải qua được lớp hiểu đội xe ở giữa chuỗi.
        var result = await service.CreateAsync(
            route.Id,
            TaoRequest(bus2.Id, GioBatDau.AddMinutes(30), GioBatDau.AddMinutes(90)));

        Assert.True(result.Success);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, await db.Trips.CountAsync());
    }

    // =======================================================================================
    // Helpers
    // =======================================================================================

    private static AppDbContext TaoDb()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"tao-chuyen-da-xe-{Guid.NewGuid()}")
            .Options);

    private static FleetAwareRouteTripsService TaoService(AppDbContext db)
        => new(new RouteTripsService(db), db);

    private static CreateTripRequest TaoRequest(Guid busId, DateTimeOffset departure, DateTimeOffset? arrival)
        => new()
        {
            BusId = busId,
            DepartureTime = departure,
            ArrivalTime = arrival,
        };

    private static async Task<RouteEntity> SeedTuyenAsync(AppDbContext db)
    {
        var route = new RouteEntity
        {
            Code = "01",
            Name = "Bến Thành — Chợ Lớn",
            Origin = "Bến Thành",
            Destination = "Chợ Lớn",
            DistanceKm = 20m,
        };

        db.Routes.Add(route);
        await db.SaveChangesAsync();
        return route;
    }

    private static async Task<Bus> SeedXeAsync(
        AppDbContext db,
        string licensePlate = "29B-123.45",
        BusStatus status = BusStatus.Active)
    {
        var bus = new Bus
        {
            LicensePlate = licensePlate,
            BusType = "Xe buýt 45 chỗ",
            Capacity = 45,
            Status = status,
        };

        db.Buses.Add(bus);
        await db.SaveChangesAsync();
        return bus;
    }

    private static async Task SeedChuyenAsync(
        AppDbContext db,
        Guid routeId,
        Guid busId,
        DateTimeOffset departure,
        DateTimeOffset? arrival)
    {
        db.Trips.Add(new Trip
        {
            RouteId = routeId,
            BusId = busId,
            DepartureTime = departure.UtcDateTime,
            ArrivalTime = arrival?.UtcDateTime,
        });
        await db.SaveChangesAsync();
    }
}
