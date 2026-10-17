using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Trips;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;
using RouteEntity = SmartBus.Api.Entities.Route;

namespace SmartBus.Tests;

/// <summary>
/// Test lớp bọc <see cref="ArrivalEstimatingRouteTripsService"/> — phần điền GIỜ ĐẾN cho các chuyến
/// vừa sinh hàng loạt (task "Hiện giờ đến + điểm đến ở màn cấu hình tần suất", Sprint 4 — Phùng Duy
/// Hoàng).
///
/// <para>
/// Bốn việc phải ghim, mỗi việc một nhóm bài:
/// <list type="number">
///   <item>công thức ước lượng đúng như seeder mẫu: 25 km/h, làm tròn LÊN bội số 5, tối thiểu 15
///   phút — sai công thức thì mọi chuyến sinh ra đều sai giờ;</item>
///   <item>giờ đến được ghi vào CSDL VÀ cập nhật vào DTO trả về — màn hình thấy ngay, không phải gọi
///   lại API;</item>
///   <item>lượt sinh lỗi thì không đụng dữ liệu và trả nguyên lỗi của lớp trong;</item>
///   <item>chuỗi DI thật (Notifying → ArrivalEstimating → RouteTripsService) vẫn chạy đúng, để một
///   lần đổi thứ tự đăng ký làm mất giờ đến lộ ra ngay.</item>
/// </list>
/// </para>
///
/// <para>
/// Không dựng HTTP host cho các bài đơn vị: <see cref="RouteTripsService"/> chạy trên InMemory, còn
/// lớp bọc được lắp tay đúng như Program.cs. Bài chuỗi DI dùng <see cref="TestAppFactory"/> để đi qua
/// đăng ký thật.
/// </para>
/// </summary>
public class ArrivalEstimatingRouteTripsServiceTests
{
    private static readonly DateTimeOffset GioBatDau = new(2026, 10, 17, 6, 0, 0, TimeSpan.Zero);

    // =======================================================================================
    // Công thức ước lượng
    // =======================================================================================

    /// <summary>
    /// 25 km/h, làm tròn LÊN bội số 5, tối thiểu 15 — công thức chép từ
    /// <c>SampleDataSeeder.ThoiGianChayUocLuong</c>. 20 km → 48 → 50; 5 km → 12 → 15 (sàn);
    /// 0 km → 0 → 15 (sàn, giờ đến không bao giờ trùng giờ đi).
    /// </summary>
    [Theory]
    [InlineData(0, 15)]
    [InlineData(1, 15)]
    [InlineData(5, 15)]
    [InlineData(10, 25)]
    [InlineData(20, 50)]
    [InlineData(25, 60)]
    [InlineData(50, 120)]
    public void Uoc_luong_thoi_gian_chay_theo_chieu_dai_tuyen(int distanceKm, int expectedMinutes)
    {
        Assert.Equal(
            expectedMinutes,
            ArrivalEstimatingRouteTripsService.EstimateTravelMinutes(distanceKm));
    }

    // =======================================================================================
    // Sinh chuyến: ghi giờ đến vào CSDL + DTO
    // =======================================================================================

    [Fact]
    public async Task Sinh_chuyen_thanh_cong_thi_dien_gio_den_cho_ca_DTO_lan_CSDL()
    {
        await using var db = TaoDb();
        var (route, bus) = await SeedRouteVaXeAsync(db, distanceKm: 20m);
        var service = TaoService(db);

        // 20 km → 50 phút; 06:00–07:00, tần suất 30 → đúng 3 chuyến (06:00, 06:30, 07:00).
        var result = await service.GenerateAsync(
            route.Id,
            new GenerateTripsRequest
            {
                BusId = bus.Id,
                StartTime = GioBatDau,
                EndTime = GioBatDau.AddHours(1),
                FrequencyMinutes = 30,
            });

        Assert.True(result.Success);
        var response = result.Data!;
        Assert.Equal(3, response.Total);

        // DTO phải mang giờ đến NGAY trong lượt trả — màn hình không gọi lại API chỉ để lấy cột này.
        Assert.All(
            response.Items,
            item => Assert.Equal(
                (DateTime?)item.DepartureTime.AddMinutes(50),
                item.ArrivalTime));

        // Và dòng trong CSDL cũng phải có giờ đến — job nhân bản ngày sau đọc từ đây.
        var daLuu = await db.Trips.AsNoTracking().OrderBy(trip => trip.DepartureTime).ToListAsync();
        Assert.Equal(3, daLuu.Count);
        Assert.All(
            daLuu,
            trip => Assert.Equal(
                (DateTime?)trip.DepartureTime.AddMinutes(50),
                trip.ArrivalTime));
    }

    [Fact]
    public async Task Sinh_chuyen_that_bai_thi_khong_tao_gi_va_tra_nguyen_loi()
    {
        await using var db = TaoDb();
        var (route, _) = await SeedRouteVaXeAsync(db);
        var service = TaoService(db);

        // Xe không tồn tại → lớp trong trả NotFound TRƯỚC khi ghi bất cứ thứ gì.
        var result = await service.GenerateAsync(
            route.Id,
            new GenerateTripsRequest
            {
                BusId = Guid.NewGuid(),
                StartTime = GioBatDau,
                EndTime = GioBatDau.AddHours(1),
                FrequencyMinutes = 30,
            });

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorKind.NotFound, result.ErrorKind);
        Assert.Empty(await db.Trips.ToListAsync());
    }

    [Fact]
    public async Task Cac_luot_doc_chuyen_di_thang_khong_bi_lop_boc_can_thiep()
    {
        await using var db = TaoDb();
        var (route, bus) = await SeedRouteVaXeAsync(db);
        var service = TaoService(db);

        var generated = await service.GenerateAsync(
            route.Id,
            new GenerateTripsRequest
            {
                BusId = bus.Id,
                StartTime = GioBatDau,
                EndTime = GioBatDau.AddMinutes(30),
                FrequencyMinutes = 30,
            });

        Assert.True(generated.Success);
        var tripId = generated.Data!.Items[0].Id;

        var theoId = await service.GetByIdAsync(route.Id, tripId);
        var danhSach = await service.ListAsync(route.Id, new ListTripsRequest());

        // Năm lượt còn lại của interface chỉ được chuyển tiếp — ghim hai lượt đọc đại diện để một
        // lần "tối ưu" quên chuyển tiếp (trả null/NotImplemented) lộ ra ngay.
        Assert.True(theoId.Success);
        Assert.True(danhSach.Success);
        Assert.Equal(2, danhSach.Data!.Items.Count);
    }

    // =======================================================================================
    // Chuỗi DI thật — Notifying ngoài cùng, ArrivalEstimating ở giữa
    // =======================================================================================

    /// <summary>
    /// Đi qua đăng ký thật trong Program.cs: <c>IRouteTripsService</c> phải resolve ra
    /// <see cref="NotifyingRouteTripsService"/> (yêu cầu cũ của
    /// <see cref="TripScheduleChangeWiringTests"/>), và bên trong nó phải có lớp ước lượng để lượt
    /// sinh trả về giờ đến. Nếu ai đổi thứ tự đăng ký, một trong hai khẳng định sẽ đỏ.
    /// </summary>
    [Fact]
    public async Task Chuoi_DI_that_sinh_chuyen_co_gio_den()
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
        var bus = new Bus
        {
            Id = Guid.NewGuid(),
            LicensePlate = "29B-123.45",
            BusType = "Xe buýt 45 chỗ",
            Capacity = 45,
            Status = BusStatus.Active,
        };

        await factory.SeedAsync(db =>
        {
            db.Routes.Add(route);
            db.Buses.Add(bus);
        });

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IRouteTripsService>();

        Assert.IsType<NotifyingRouteTripsService>(service);

        var result = await service.GenerateAsync(
            route.Id,
            new GenerateTripsRequest
            {
                BusId = bus.Id,
                StartTime = GioBatDau,
                EndTime = GioBatDau.AddMinutes(30),
                FrequencyMinutes = 30,
            });

        Assert.True(result.Success);
        Assert.All(
            result.Data!.Items,
            item => Assert.Equal(
                (DateTime?)item.DepartureTime.AddMinutes(50),
                item.ArrivalTime));

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var daLuu = await db.Trips.AsNoTracking().ToListAsync();
        Assert.NotEmpty(daLuu);
        Assert.All(daLuu, trip => Assert.NotNull(trip.ArrivalTime));
    }

    // ── Hạ tầng dùng chung của lớp test ────────────────────────────────────────────────────────

    private static AppDbContext TaoDb()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"uoc-luong-gio-den-{Guid.NewGuid()}")
            .Options);

    private static ArrivalEstimatingRouteTripsService TaoService(AppDbContext db)
        => new(
            new RouteTripsService(db),
            db,
            NullLogger<ArrivalEstimatingRouteTripsService>.Instance);

    private static async Task<(RouteEntity Route, Bus Bus)> SeedRouteVaXeAsync(
        AppDbContext db,
        decimal distanceKm = 20m)
    {
        var route = new RouteEntity
        {
            Id = Guid.NewGuid(),
            Code = "01",
            Name = "Bến xe Mỹ Đình — Bến xe Gia Lâm",
            Origin = "Bến xe Mỹ Đình",
            Destination = "Bến xe Gia Lâm",
            DistanceKm = distanceKm,
        };

        var bus = new Bus
        {
            Id = Guid.NewGuid(),
            LicensePlate = "29B-123.45",
            BusType = "Xe buýt 45 chỗ",
            Capacity = 45,
            Status = BusStatus.Active,
        };

        db.Routes.Add(route);
        db.Buses.Add(bus);
        await db.SaveChangesAsync();

        return (route, bus);
    }
}
