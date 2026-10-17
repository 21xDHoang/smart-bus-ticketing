using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Trips;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;
using RouteEntity = SmartBus.Api.Entities.Route;

namespace SmartBus.Tests;

/// <summary>
/// Test <see cref="FleetTripGenerationService"/> — sinh chuyến hàng loạt chia VÒNG TRÒN cho nhiều xe
/// (nợ của đợt mở rộng đội xe 17/10/2026, Phùng Duy Hoàng).
///
/// <para>
/// Những việc phải ghim:
/// <list type="number">
///   <item>chia vòng tròn đúng thứ tự <c>busIds</c>, không dồn hết vào xe đầu — đây chính là lỗi cũ
///   khiến một xe chạy 62 chuyến/ngày;</item>
///   <item>bảng tải theo xe trả đủ mọi xe theo thứ tự yêu cầu, kể cả xe 0 chuyến;</item>
///   <item>không đủ xe thì trả 400 <c>errors.busIds</c> và KHÔNG ghi dòng nào (nguyên tử);</item>
///   <item>chuyến đang chạy của xe ở tuyến KHÁC cũng chiếm khung giờ — không chỉ chuyến cùng tuyến;</item>
///   <item>cùng tuyến chỉ cấm trùng đúng giờ khởi hành;</item>
///   <item>hai trần: 500 chuyến/lượt và 200 chuyến/ngày (UTC) của tuyến;</item>
///   <item>các lỗi tham số trả đúng field để màn hình gắn vào ô nhập.</item>
/// </list>
/// </para>
///
/// <para>
/// Chạy trên InMemory, dựng service trực tiếp — không cần HTTP host. Các con số ước lượng giờ đến
/// (5 km → 15 phút, 20 km → 50 phút) khớp <see cref="ArrivalEstimatingRouteTripsService"/>.
/// </para>
/// </summary>
public class FleetTripGenerationServiceTests
{
    private static readonly DateTimeOffset GioBatDau = new(2026, 10, 17, 6, 0, 0, TimeSpan.Zero);

    // =======================================================================================
    // 1. Chia vòng tròn + bảng tải
    // =======================================================================================

    [Fact]
    public async Task Chia_vong_tron_theo_thu_tu_busIds_va_tra_bang_tai()
    {
        await using var db = TaoDb();
        var route = await SeedTuyenAsync(db, distanceKm: 5m); // 5 km → 15 phút
        var bus1 = await SeedXeAsync(db, "29B-111.11");
        var bus2 = await SeedXeAsync(db, "29B-222.22");
        var bus3 = await SeedXeAsync(db, "29B-333.33");
        var service = new FleetTripGenerationService(db);

        var result = await service.GenerateAsync(
            route.Id,
            TaoRequest([bus1.Id, bus2.Id, bus3.Id], GioBatDau, GioBatDau.AddHours(1), 15));

        Assert.True(result.Success);
        var data = result.Data!;

        // 06:00, 06:15, 06:30, 06:45, 07:00 — vòng tròn b1, b2, b3, b1, b2.
        Assert.Equal(5, data.Total);
        Assert.Equal(
            new[] { bus1.Id, bus2.Id, bus3.Id, bus1.Id, bus2.Id },
            data.Items.Select(item => item.BusId).ToArray());

        // Bảng tải theo ĐÚNG thứ tự yêu cầu, đủ cả xe ít chuyến.
        Assert.Equal(
            new[] { bus1.Id, bus2.Id, bus3.Id },
            data.BusLoads.Select(load => load.BusId).ToArray());
        Assert.Equal(new[] { 2, 2, 1 }, data.BusLoads.Select(load => load.TripCount).ToArray());
        Assert.Equal(
            new[] { "29B-111.11", "29B-222.22", "29B-333.33" },
            data.BusLoads.Select(load => load.BusLicensePlate).ToArray());

        // Giờ đến điền ngay lúc tạo, cả DTO lẫn CSDL.
        Assert.All(
            data.Items,
            item => Assert.Equal(item.DepartureTime.AddMinutes(15), item.ArrivalTime!.Value));

        var daLuu = await db.Trips.AsNoTracking().OrderBy(trip => trip.DepartureTime).ToListAsync();
        Assert.Equal(5, daLuu.Count);
        Assert.All(daLuu, trip => Assert.Equal(trip.DepartureTime.AddMinutes(15), trip.ArrivalTime));
    }

    [Fact]
    public async Task Xe_khong_nhan_chuyen_nao_van_co_dong_trong_bang_tai()
    {
        await using var db = TaoDb();
        var route = await SeedTuyenAsync(db, distanceKm: 5m);
        var bus1 = await SeedXeAsync(db, "29B-111.11");
        var bus2 = await SeedXeAsync(db, "29B-222.22");
        var service = new FleetTripGenerationService(db);

        // Một chuyến duy nhất (06:00) → chỉ xe đầu nhận; xe thứ hai phải hiện 0, không bị bỏ khỏi bảng.
        var result = await service.GenerateAsync(
            route.Id,
            TaoRequest([bus1.Id, bus2.Id], GioBatDau, GioBatDau.AddMinutes(10), 15));

        Assert.True(result.Success);
        var loads = result.Data!.BusLoads;
        Assert.Equal(2, loads.Count);
        Assert.Equal(1, loads[0].TripCount);
        Assert.Equal(0, loads[1].TripCount);
    }

    // =======================================================================================
    // 2. Không đủ xe — nguyên tử
    // =======================================================================================

    [Fact]
    public async Task Khong_du_xe_thi_khong_ghi_dong_nao()
    {
        await using var db = TaoDb();
        var route = await SeedTuyenAsync(db, distanceKm: 20m); // 50 phút
        var bus = await SeedXeAsync(db, "29B-111.11");
        var service = new FleetTripGenerationService(db);

        // Tần suất 30 phút nhưng mỗi chuyến chạy 50 phút → một xe không kham nổi chuyến thứ hai.
        var result = await service.GenerateAsync(
            route.Id,
            TaoRequest([bus.Id], GioBatDau, GioBatDau.AddMinutes(90), 30));

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorKind.Invalid, result.ErrorKind);
        Assert.NotNull(result.Errors);
        Assert.Contains("busIds", result.Errors!.Keys);
        Assert.Empty(await db.Trips.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Hai_xe_phu_duoc_tan_suat_30_phut_tren_tuyen_chay_50_phut()
    {
        await using var db = TaoDb();
        var route = await SeedTuyenAsync(db, distanceKm: 20m);
        var bus1 = await SeedXeAsync(db, "29B-111.11");
        var bus2 = await SeedXeAsync(db, "29B-222.22");
        var service = new FleetTripGenerationService(db);

        var result = await service.GenerateAsync(
            route.Id,
            TaoRequest([bus1.Id, bus2.Id], GioBatDau, GioBatDau.AddMinutes(90), 30));

        Assert.True(result.Success);
        // 06:00, 06:30, 07:00, 07:30 — xen kẽ hai xe.
        Assert.Equal(
            new[] { bus1.Id, bus2.Id, bus1.Id, bus2.Id },
            result.Data!.Items.Select(item => item.BusId).ToArray());
        Assert.Equal(4, await db.Trips.CountAsync());
    }

    [Fact]
    public async Task Xe_dang_ban_o_tuyen_khac_cung_bi_coi_la_khong_ranh()
    {
        await using var db = TaoDb();
        var route = await SeedTuyenAsync(db, distanceKm: 5m);
        var otherRoute = await SeedTuyenAsync(db, code: "02", distanceKm: 5m);
        var bus = await SeedXeAsync(db, "29B-111.11");

        db.Trips.Add(new Trip
        {
            RouteId = otherRoute.Id,
            BusId = bus.Id,
            DepartureTime = GioBatDau.UtcDateTime,
            ArrivalTime = GioBatDau.AddMinutes(30).UtcDateTime,
        });
        await db.SaveChangesAsync();

        var service = new FleetTripGenerationService(db);
        var result = await service.GenerateAsync(
            route.Id,
            TaoRequest([bus.Id], GioBatDau, GioBatDau.AddMinutes(15), 15));

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorKind.Invalid, result.ErrorKind);
        Assert.Contains("busIds", result.Errors!.Keys);
        Assert.Single(await db.Trips.AsNoTracking().ToListAsync());
    }

    // =======================================================================================
    // 3. Trùng giờ trên cùng tuyến
    // =======================================================================================

    [Fact]
    public async Task Trung_dung_gio_khoi_hanh_cung_tuyen_bi_chan()
    {
        await using var db = TaoDb();
        var route = await SeedTuyenAsync(db, distanceKm: 5m);
        var bus1 = await SeedXeAsync(db, "29B-111.11");
        var bus2 = await SeedXeAsync(db, "29B-222.22");

        db.Trips.Add(new Trip
        {
            RouteId = route.Id,
            BusId = bus1.Id,
            DepartureTime = GioBatDau.AddMinutes(30).UtcDateTime,
            ArrivalTime = GioBatDau.AddMinutes(45).UtcDateTime,
        });
        await db.SaveChangesAsync();

        var service = new FleetTripGenerationService(db);
        var result = await service.GenerateAsync(
            route.Id,
            TaoRequest([bus2.Id], GioBatDau, GioBatDau.AddHours(1), 30));

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorKind.Conflict, result.ErrorKind);
        Assert.Contains("2026-10-17 06:30 UTC", result.Error);
        Assert.Single(await db.Trips.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Khac_gio_nhung_chong_khung_gio_cung_tuyen_van_cho_qua()
    {
        await using var db = TaoDb();
        var route = await SeedTuyenAsync(db, distanceKm: 20m); // 50 phút
        var bus1 = await SeedXeAsync(db, "29B-111.11");
        var bus2 = await SeedXeAsync(db, "29B-222.22");

        db.Trips.Add(new Trip
        {
            RouteId = route.Id,
            BusId = bus1.Id,
            DepartureTime = GioBatDau.UtcDateTime,
            ArrivalTime = GioBatDau.AddMinutes(50).UtcDateTime,
        });
        await db.SaveChangesAsync();

        // 06:25–07:15 chồng khung giờ với chuyến 06:00–06:50 nhưng KHÁC XE → lịch bình thường.
        var service = new FleetTripGenerationService(db);
        var result = await service.GenerateAsync(
            route.Id,
            TaoRequest([bus2.Id], GioBatDau.AddMinutes(25), GioBatDau.AddMinutes(35), 15));

        Assert.True(result.Success);
        Assert.Equal(2, await db.Trips.CountAsync());
    }

    // =======================================================================================
    // 4. Hai trần
    // =======================================================================================

    [Fact]
    public async Task Vuot_tran_200_chuyen_ngay_cua_tuyen_bi_chan()
    {
        await using var db = TaoDb();
        var route = await SeedTuyenAsync(db, distanceKm: 5m);
        var historyBus = await SeedXeAsync(db, "29B-000.00");
        var bus = await SeedXeAsync(db, "29B-111.11");

        var day = GioBatDau.UtcDateTime.Date;
        db.Trips.AddRange(Enumerable.Range(0, 199).Select(minute => new Trip
        {
            RouteId = route.Id,
            BusId = historyBus.Id,
            DepartureTime = day.AddMinutes(minute),
        }));
        await db.SaveChangesAsync();

        var service = new FleetTripGenerationService(db);
        var result = await service.GenerateAsync(
            route.Id,
            TaoRequest([bus.Id], GioBatDau, GioBatDau.AddMinutes(30), 30));

        // 199 + 2 = 201 > 200.
        Assert.False(result.Success);
        Assert.Equal(ServiceErrorKind.Invalid, result.ErrorKind);
        Assert.Contains("endTime", result.Errors!.Keys);
        Assert.Equal(199, await db.Trips.CountAsync());
    }

    [Fact]
    public async Task Vuot_tran_500_chuyen_mot_luot_bi_chan()
    {
        await using var db = TaoDb();
        var route = await SeedTuyenAsync(db, distanceKm: 5m);
        var bus = await SeedXeAsync(db, "29B-111.11");
        var service = new FleetTripGenerationService(db);

        // 24 giờ / 1 phút = 1.441 chuyến.
        var result = await service.GenerateAsync(
            route.Id,
            TaoRequest([bus.Id], GioBatDau, GioBatDau.AddDays(1), 1));

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorKind.Invalid, result.ErrorKind);
        Assert.Contains("endTime", result.Errors!.Keys);
        Assert.Empty(await db.Trips.AsNoTracking().ToListAsync());
    }

    // =======================================================================================
    // 5. Lỗi tham số — đúng field để màn hình gắn vào ô nhập
    // =======================================================================================

    [Fact]
    public async Task Danh_sach_xe_rong_bao_loi_busIds()
    {
        await using var db = TaoDb();
        var route = await SeedTuyenAsync(db);
        var service = new FleetTripGenerationService(db);

        var result = await service.GenerateAsync(
            route.Id,
            TaoRequest([], GioBatDau, GioBatDau.AddHours(1), 15));

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorKind.Invalid, result.ErrorKind);
        Assert.Contains("busIds", result.Errors!.Keys);
    }

    [Fact]
    public async Task Danh_sach_xe_trung_lap_bao_loi_busIds()
    {
        await using var db = TaoDb();
        var route = await SeedTuyenAsync(db);
        var bus = await SeedXeAsync(db);
        var service = new FleetTripGenerationService(db);

        var result = await service.GenerateAsync(
            route.Id,
            TaoRequest([bus.Id, bus.Id], GioBatDau, GioBatDau.AddHours(1), 15));

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorKind.Invalid, result.ErrorKind);
        Assert.Contains("busIds", result.Errors!.Keys);
    }

    [Fact]
    public async Task Qua_100_xe_mot_luot_bao_loi_busIds()
    {
        await using var db = TaoDb();
        var route = await SeedTuyenAsync(db);
        var service = new FleetTripGenerationService(db);

        var quaNhieuXe = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToList();
        var result = await service.GenerateAsync(
            route.Id,
            TaoRequest(quaNhieuXe, GioBatDau, GioBatDau.AddHours(1), 15));

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorKind.Invalid, result.ErrorKind);
        Assert.Contains("busIds", result.Errors!.Keys);
    }

    [Fact]
    public async Task Tuyen_ngung_khai_thac_tra_409()
    {
        await using var db = TaoDb();
        var route = await SeedTuyenAsync(db, status: RouteStatus.Inactive);
        var bus = await SeedXeAsync(db);
        var service = new FleetTripGenerationService(db);

        var result = await service.GenerateAsync(
            route.Id,
            TaoRequest([bus.Id], GioBatDau, GioBatDau.AddHours(1), 15));

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorKind.Conflict, result.ErrorKind);
    }

    [Fact]
    public async Task Tuyen_khong_ton_tai_tra_404()
    {
        await using var db = TaoDb();
        var bus = await SeedXeAsync(db);
        var service = new FleetTripGenerationService(db);

        var result = await service.GenerateAsync(
            Guid.NewGuid(),
            TaoRequest([bus.Id], GioBatDau, GioBatDau.AddHours(1), 15));

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorKind.NotFound, result.ErrorKind);
    }

    [Fact]
    public async Task Xe_bao_duong_tra_409()
    {
        await using var db = TaoDb();
        var route = await SeedTuyenAsync(db);
        var bus = await SeedXeAsync(db, status: BusStatus.Maintenance);
        var service = new FleetTripGenerationService(db);

        var result = await service.GenerateAsync(
            route.Id,
            TaoRequest([bus.Id], GioBatDau, GioBatDau.AddHours(1), 15));

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorKind.Conflict, result.ErrorKind);
        Assert.Contains("29B-123.45", result.Error);
    }

    [Fact]
    public async Task Xe_khong_ton_tai_tra_404()
    {
        await using var db = TaoDb();
        var route = await SeedTuyenAsync(db);
        var service = new FleetTripGenerationService(db);

        var result = await service.GenerateAsync(
            route.Id,
            TaoRequest([Guid.NewGuid()], GioBatDau, GioBatDau.AddHours(1), 15));

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorKind.NotFound, result.ErrorKind);
    }

    [Fact]
    public async Task Khoang_thoi_gian_nguoc_bao_loi_endTime()
    {
        await using var db = TaoDb();
        var route = await SeedTuyenAsync(db);
        var bus = await SeedXeAsync(db);
        var service = new FleetTripGenerationService(db);

        var result = await service.GenerateAsync(
            route.Id,
            TaoRequest([bus.Id], GioBatDau, GioBatDau, 15));

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorKind.Invalid, result.ErrorKind);
        Assert.Contains("endTime", result.Errors!.Keys);
    }

    [Fact]
    public async Task Tan_suat_ngoai_khoang_bao_loi_frequencyMinutes()
    {
        await using var db = TaoDb();
        var route = await SeedTuyenAsync(db);
        var bus = await SeedXeAsync(db);
        var service = new FleetTripGenerationService(db);

        var result = await service.GenerateAsync(
            route.Id,
            TaoRequest([bus.Id], GioBatDau, GioBatDau.AddHours(1), frequencyMinutes: 0));

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorKind.Invalid, result.ErrorKind);
        Assert.Contains("frequencyMinutes", result.Errors!.Keys);
    }

    // =======================================================================================
    // Helpers
    // =======================================================================================

    private static AppDbContext TaoDb()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"sinh-chuyen-da-xe-{Guid.NewGuid()}")
            .Options);

    private static GenerateFleetTripsRequest TaoRequest(
        IEnumerable<Guid> busIds,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        int frequencyMinutes)
        => new()
        {
            BusIds = busIds.ToList(),
            StartTime = startTime,
            EndTime = endTime,
            FrequencyMinutes = frequencyMinutes,
        };

    private static async Task<RouteEntity> SeedTuyenAsync(
        AppDbContext db,
        string code = "01",
        decimal distanceKm = 5m,
        RouteStatus status = RouteStatus.Active)
    {
        var route = new RouteEntity
        {
            Code = code,
            Name = $"Tuyến {code}",
            Origin = "Bến Thành",
            Destination = "Chợ Lớn",
            DistanceKm = distanceKm,
            Status = status,
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
}
