using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test service quyết định trạm lên xe — <see cref="TicketBoardingStopService"/>
/// (US 15, task *"API ghi nhận lượt lên xe theo trạm"* — Nguyễn Duy Kiên, dòng 17 Sprint 4).
///
/// Ba luật đang ghim ở đây khớp docs/34 §2.1 và mục "Soát vé QR" của docs/api-contract.md:
/// (1) chỉ lượt <c>Valid</c> mới có trạm; (2) trạm phải thuộc tuyến của chuyến TRÊN VÉ, KHÔNG so với
/// <c>Tickets.BoardingStopId</c> (trạm khách chọn lúc mua); (3) không khai trạm thì ghi <c>null</c>.
///
/// Gọi thẳng service, KHÔNG dựng host: service chỉ đọc <c>Trips</c> + <c>RouteStops</c>. Dùng
/// provider InMemory như phần còn lại của bộ test — nó không cưỡng chế khoá ngoại, nên helper seed
/// chỉ cần đúng các dòng service đọc.
/// </summary>
public class TicketBoardingStopServiceTests
{
    private const string SaiTuyenMessage = "Trạm lên xe không thuộc tuyến của chuyến.";

    // -------------------------------------------------------------- Hai nhánh hợp lệ

    [Fact]
    public async Task Khong_khai_tram_tra_null()
    {
        await using var db = TaoDb();

        var ketQua = await new TicketBoardingStopService(db).ResolveAsync(
            requestedStopId: null,
            ticketTripId: Guid.NewGuid(),
            TicketValidationResult.Valid);

        Assert.True(ketQua.Success);
        Assert.Null(ketQua.Data);
    }

    [Fact]
    public async Task Luot_Valid_tram_thuoc_tuyen_tra_dung_tram()
    {
        await using var db = TaoDb();
        var routeId = Guid.NewGuid();
        var stopId = Guid.NewGuid();
        var trip = await SeedTripAsync(db, routeId);
        await SeedRouteStopAsync(db, routeId, stopId);

        var ketQua = await new TicketBoardingStopService(db).ResolveAsync(
            stopId, trip.Id, TicketValidationResult.Valid);

        Assert.True(ketQua.Success);
        Assert.Equal(stopId, ketQua.Data);
    }

    [Fact]
    public async Task Nhieu_tram_tren_tuyen_nhan_dung_tram_duoc_khai()
    {
        await using var db = TaoDb();
        var routeId = Guid.NewGuid();
        var tramDau = Guid.NewGuid();
        var tramGiua = Guid.NewGuid();
        var trip = await SeedTripAsync(db, routeId);
        await SeedRouteStopAsync(db, routeId, tramDau, stopOrder: 1);
        await SeedRouteStopAsync(db, routeId, tramGiua, stopOrder: 2);

        var ketQua = await new TicketBoardingStopService(db).ResolveAsync(
            tramGiua, trip.Id, TicketValidationResult.Valid);

        Assert.True(ketQua.Success);
        Assert.Equal(tramGiua, ketQua.Data);
    }

    // -------------------------------------------------------------- Ba nhánh từ chối

    [Fact]
    public async Task Tram_cua_tuyen_khac_bao_loi_theo_truong_boardingStopId()
    {
        await using var db = TaoDb();
        var tuyenCuaChuyen = Guid.NewGuid();
        var tuyenKhac = Guid.NewGuid();
        var tramCuaTuyenKhac = Guid.NewGuid();
        var trip = await SeedTripAsync(db, tuyenCuaChuyen);
        await SeedRouteStopAsync(db, tuyenKhac, tramCuaTuyenKhac);

        var ketQua = await new TicketBoardingStopService(db).ResolveAsync(
            tramCuaTuyenKhac, trip.Id, TicketValidationResult.Valid);

        Assert.False(ketQua.Success);
        Assert.Equal(ServiceErrorKind.Invalid, ketQua.ErrorKind);
        Assert.Equal(SaiTuyenMessage, ketQua.Error);
        Assert.True(ketQua.Errors!.ContainsKey("boardingStopId"));
        Assert.Equal(new[] { SaiTuyenMessage }, ketQua.Errors["boardingStopId"]);
        Assert.Null(ketQua.Data);
    }

    [Fact]
    public async Task Tram_khong_ton_tai_bao_loi_cung_cau()
    {
        await using var db = TaoDb();
        var trip = await SeedTripAsync(db, Guid.NewGuid());

        // Không seed dòng RouteStops nào — trạm lạ hoàn toàn (InMemory không cưỡng chế FK).
        var ketQua = await new TicketBoardingStopService(db).ResolveAsync(
            Guid.NewGuid(), trip.Id, TicketValidationResult.Valid);

        Assert.False(ketQua.Success);
        Assert.Equal(SaiTuyenMessage, ketQua.Error);
        Assert.True(ketQua.Errors!.ContainsKey("boardingStopId"));
    }

    [Fact]
    public async Task Chuyen_tren_ve_khong_tra_duoc_bao_loi()
    {
        await using var db = TaoDb();

        // Vé Valid nhưng chuyến không có trong store — chỉ xảy ra với dòng mồ côi của tầng test.
        var ketQua = await new TicketBoardingStopService(db).ResolveAsync(
            Guid.NewGuid(), Guid.NewGuid(), TicketValidationResult.Valid);

        Assert.False(ketQua.Success);
        Assert.Equal(SaiTuyenMessage, ketQua.Error);
    }

    [Fact]
    public async Task Khong_co_chuyen_tren_ve_bao_loi()
    {
        await using var db = TaoDb();

        var ketQua = await new TicketBoardingStopService(db).ResolveAsync(
            Guid.NewGuid(), ticketTripId: null, TicketValidationResult.Valid);

        Assert.False(ketQua.Success);
        Assert.Equal(SaiTuyenMessage, ketQua.Error);
    }

    // -------------------------------------------------------------- Chỉ lượt Valid mới có trạm

    [Theory]
    [InlineData(TicketValidationResult.NotFound)]
    [InlineData(TicketValidationResult.Cancelled)]
    [InlineData(TicketValidationResult.AlreadyUsed)]
    [InlineData(TicketValidationResult.WrongTrip)]
    [InlineData(TicketValidationResult.Expired)]
    public async Task Luot_bi_tu_choi_khong_ghi_tram_du_tram_hop_le(TicketValidationResult result)
    {
        await using var db = TaoDb();
        var routeId = Guid.NewGuid();
        var stopId = Guid.NewGuid();
        var trip = await SeedTripAsync(db, routeId);
        await SeedRouteStopAsync(db, routeId, stopId);

        var ketQua = await new TicketBoardingStopService(db).ResolveAsync(
            stopId, trip.Id, result);

        // Không phải lỗi: phụ xe vẫn quét được vé hỏng; chỉ là lượt đó KHÔNG phải lượt lên xe.
        Assert.True(ketQua.Success);
        Assert.Null(ketQua.Data);
    }

    // =======================================================================================
    // Helper — mỗi lớp test tự chép, không có lớp base chung (lệ đang có của dự án)
    // =======================================================================================

    private static AppDbContext TaoDb()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task<Trip> SeedTripAsync(AppDbContext db, Guid routeId)
    {
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            BusId = Guid.NewGuid(),
            DriverId = Guid.NewGuid(),
            Status = TripStatus.Scheduled,
            DepartureTime = DateTime.UtcNow.AddDays(1),
        };

        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        return trip;
    }

    private static async Task SeedRouteStopAsync(
        AppDbContext db,
        Guid routeId,
        Guid stopId,
        int stopOrder = 1)
    {
        db.RouteStops.Add(new RouteStop
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            StopId = stopId,
            StopOrder = stopOrder,
            DistanceKm = 0m,
        });

        await db.SaveChangesAsync();
    }
}
