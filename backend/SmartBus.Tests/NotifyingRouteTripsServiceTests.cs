using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Trips;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test lớp bọc <see cref="NotifyingRouteTripsService"/> — chỗ nối giữa lượt sửa chuyến
/// <c>PUT /api/routes/{routeId}/trips/{id}</c> và bộ phận báo tin (task dòng 44 Sprint 4, Phùng Duy
/// Hoàng). Lớp bọc phải làm đúng BA việc, và mỗi việc có một bài ở đây:
///
/// <list type="number">
///   <item>chụp giờ cũ TRƯỚC khi lớp trong sửa dòng <c>Trips</c> — nếu chụp sau thì ảnh chụp đã là giờ
///   mới và không bao giờ phát hiện được thay đổi;</item>
///   <item>chỉ gọi notifier khi lượt sửa THÀNH CÔNG — PUT lỗi 404/400 không đổi giờ nào;</item>
///   <item>không đổi kết quả của lớp trong — controller vẫn nhận đúng <see cref="ServiceResult{T}"/>,
///   đúng mã 200/400/404 như trước.</item>
/// </list>
///
/// Không dựng host và không dùng hub thật: <see cref="RouteTripsService"/> chạy trên InMemory, còn bộ
/// phận báo tin được thay bằng <see cref="NotifierGhiNhan"/> — hành vi của bộ phận đó đã có
/// <see cref="TripScheduleChangeNotifierTests"/> lo. Chỗ nối DI thật nằm ở
/// <see cref="TripScheduleChangeWiringTests"/>.
/// </summary>
public class NotifyingRouteTripsServiceTests
{
    private static readonly DateTime GioCu = new(2026, 10, 17, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime GioToiCu = new(2026, 10, 17, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Sua_thanh_cong_thi_chuyen_gio_cu_cho_notifier_va_tra_ket_qua_cua_lop_trong()
    {
        await using var db = TaoDb();
        var (route, trip, bus) = await SeedChuyenAsync(db);
        var notifier = new NotifierGhiNhan();
        var service = TaoService(db, notifier);

        var result = await service.UpdateAsync(
            route.Id,
            trip.Id,
            new UpdateTripRequest
            {
                BusId = bus.Id,
                DepartureTime = new DateTimeOffset(GioCu.AddMinutes(30), TimeSpan.Zero),
                ArrivalTime = new DateTimeOffset(GioToiCu.AddMinutes(30), TimeSpan.Zero),
            });

        Assert.True(result.Success);
        Assert.Equal(GioCu.AddMinutes(30), result.Data!.DepartureTime);

        // Ảnh chụp phải là giờ CŨ. Nếu lớp bọc chụp sau lượt gọi (hoặc giữ tham chiếu entity thay vì
        // chiếu giá trị), cả hai mốc ở đây sẽ bằng giờ mới và notifier im lặng — đúng lỗi mà bài này
        // ghim.
        var lanGoi = Assert.Single(notifier.Calls);
        Assert.Equal(trip.Id, lanGoi.TripId);
        Assert.NotNull(lanGoi.Previous);
        Assert.Equal(GioCu, lanGoi.Previous.DepartureTime);
        Assert.Equal(GioToiCu, lanGoi.Previous.ArrivalTime);

        var daLuu = await db.Trips.AsNoTracking().SingleAsync(t => t.Id == trip.Id);
        Assert.Equal(GioCu.AddMinutes(30), daLuu.DepartureTime);
        Assert.Equal(GioToiCu.AddMinutes(30), daLuu.ArrivalTime);
    }

    [Fact]
    public async Task Sua_that_bai_404_thi_khong_bao()
    {
        await using var db = TaoDb();
        var (_, trip, bus) = await SeedChuyenAsync(db);
        var notifier = new NotifierGhiNhan();
        var service = TaoService(db, notifier);

        // Tuyến sai → lớp trong trả NotFound, giờ chưa hề đổi.
        var result = await service.UpdateAsync(
            Guid.NewGuid(),
            trip.Id,
            new UpdateTripRequest
            {
                BusId = bus.Id,
                DepartureTime = new DateTimeOffset(GioCu.AddMinutes(30), TimeSpan.Zero),
                ArrivalTime = new DateTimeOffset(GioToiCu, TimeSpan.Zero),
            });

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorKind.NotFound, result.ErrorKind);
        Assert.Empty(notifier.Calls);
    }

    [Fact]
    public async Task Sua_that_bai_400_thi_khong_bao()
    {
        await using var db = TaoDb();
        var (route, trip, bus) = await SeedChuyenAsync(db);
        var notifier = new NotifierGhiNhan();
        var service = TaoService(db, notifier);

        // Giờ tới trước giờ khởi hành → lớp trong trả Invalid TRƯỚC khi ghi bất cứ thứ gì.
        var result = await service.UpdateAsync(
            route.Id,
            trip.Id,
            new UpdateTripRequest
            {
                BusId = bus.Id,
                DepartureTime = new DateTimeOffset(GioCu.AddHours(2), TimeSpan.Zero),
                ArrivalTime = new DateTimeOffset(GioCu.AddHours(1), TimeSpan.Zero),
            });

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorKind.Invalid, result.ErrorKind);
        Assert.Empty(notifier.Calls);

        // Và dữ liệu phải còn nguyên giờ cũ — một lượt PUT hỏng không được để lại dấu vết.
        var daLuu = await db.Trips.AsNoTracking().SingleAsync(t => t.Id == trip.Id);
        Assert.Equal(GioCu, daLuu.DepartureTime);
        Assert.Equal(GioToiCu, daLuu.ArrivalTime);
    }

    [Fact]
    public async Task Cac_luot_doc_chuyen_di_thang_khong_qua_notifier()
    {
        await using var db = TaoDb();
        var (route, trip, _) = await SeedChuyenAsync(db);
        var notifier = new NotifierGhiNhan();
        var service = TaoService(db, notifier);

        var theoId = await service.GetByIdAsync(route.Id, trip.Id);
        var danhSach = await service.ListAsync(route.Id, new ListTripsRequest());

        // Năm lượt còn lại của interface chỉ chuyển tiếp — ghim hai lượt đọc đại diện để một lần
        // "tối ưu" quên chuyển tiếp (trả null/NotImplemented) lộ ra ngay. Không lượt đọc nào được
        // sinh thông báo.
        Assert.True(theoId.Success);
        Assert.True(danhSach.Success);
        Assert.Single(danhSach.Data!.Items);
        Assert.Empty(notifier.Calls);
    }

    // ── Hạ tầng dùng chung của lớp test ────────────────────────────────────────────────────────

    private static AppDbContext TaoDb()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"lop-boc-bao-tre-{Guid.NewGuid()}")
            .Options);

    private static NotifyingRouteTripsService TaoService(AppDbContext db, ITripScheduleChangeNotifier notifier)
        => new(
            new RouteTripsService(db),
            notifier,
            db,
            NullLogger<NotifyingRouteTripsService>.Instance);

    /// <summary>
    /// Tuyến + xe + chuyến Scheduled đủ để <see cref="RouteTripsService.UpdateAsync"/> đi hết nhánh
    /// thành công: nó cần <c>Bus</c> nạp kèm (không đổi xe thì dùng lại quan hệ đang có).
    /// </summary>
    private static async Task<(Route Route, Trip Trip, Bus Bus)> SeedChuyenAsync(AppDbContext db)
    {
        var route = new Route
        {
            Code = "01",
            Name = "Bến Thành — Chợ Lớn",
            Origin = "Bến Thành",
            Destination = "Chợ Lớn",
        };

        var bus = new Bus
        {
            LicensePlate = "29B-123.45",
            BusType = "Xe buýt 45 chỗ",
            Capacity = 45,
            Status = BusStatus.Active,
        };

        var trip = new Trip
        {
            RouteId = route.Id,
            Route = route,
            BusId = bus.Id,
            Bus = bus,
            DepartureTime = GioCu,
            ArrivalTime = GioToiCu,
            Status = TripStatus.Scheduled,
        };

        db.Trips.Add(trip);
        await db.SaveChangesAsync();
        return (route, trip, bus);
    }

    private sealed class NotifierGhiNhan : ITripScheduleChangeNotifier
    {
        public List<(Guid TripId, TripScheduleSnapshot? Previous)> Calls { get; } = [];

        public Task NotifyAsync(Guid tripId, TripScheduleSnapshot? previous)
        {
            Calls.Add((tripId, previous));
            return Task.CompletedTask;
        }
    }
}
