using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Buses;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Thông tin phân công trên màn hình đội xe — <c>assignedRoutes</c> / <c>currentTrip</c> /
/// <c>nextTrip</c> / <c>seatSummary</c> của <see cref="BusResponse"/> (story 14, mở rộng
/// 17/10/2026).
///
/// Ghim đúng cửa sổ 7 ngày và cách suy tuyến từ <c>Trips</c>: hệ thống KHÔNG có bảng gán
/// xe ↔ tuyến, nên một lỗi ở đây (ví dụ tính cả chuyến Completed) sẽ khiến quản lý tưởng xe
/// đang phụ trách tuyến đã chạy xong từ tuần trước. Chạy trên InMemory + gọi thẳng service —
/// cùng lối <see cref="MonthlyPassExpiryServiceTests"/>, không dựng host.
/// </summary>
public class BusAssignmentTests
{
    private const string BusType = "Xe buýt 45 chỗ";

    [Fact]
    public async Task Xe_dang_chay_va_sap_chay_hien_dung_tuyen_chuyen_va_ghe()
    {
        await using var db = TaoDb();
        var (bus, driver, stop) = await SeedDuLieuAsync(db);

        var service = new BusService(db);
        var result = await service.ListAsync(new ListBusesRequest { Page = 1, PageSize = 10 });

        Assert.True(result.Success);
        var row = Assert.Single(result.Data!.Items, b => b.Id == bus.Id);

        // Hai tuyến: B10 có chuyến sắp chạy nên đứng trước, 01 chỉ còn chuyến đang chạy.
        Assert.Equal(new[] { "B10", "01" }, row.AssignedRoutes.Select(r => r.RouteCode));

        var tuyen01 = row.AssignedRoutes.Single(r => r.RouteCode == "01");
        Assert.Equal(1, tuyen01.TripCount);
        Assert.Null(tuyen01.NextDepartureTime);   // tuyến chỉ còn chuyến đang chạy

        var tuyenB10 = row.AssignedRoutes.Single(r => r.RouteCode == "B10");
        Assert.Equal(2, tuyenB10.TripCount);
        Assert.Equal(tuyenB10.NextDepartureTime, row.NextTrip!.DepartureTime);

        // Chuyến đang chạy: kèm tài xế + trạm vừa qua.
        Assert.NotNull(row.CurrentTrip);
        Assert.Equal("01", row.CurrentTrip!.RouteCode);
        Assert.Equal("Running", row.CurrentTrip.Status);
        Assert.Equal(driver.Id, row.CurrentTrip.DriverId);
        Assert.Equal(driver.FullName, row.CurrentTrip.DriverName);
        Assert.Equal(stop.Name, row.CurrentTrip.CurrentStopName);

        // Chuyến kế tiếp: chưa phân công tài xế, không có trạm hiện tại.
        Assert.NotNull(row.NextTrip);
        Assert.Equal("B10", row.NextTrip!.RouteCode);
        Assert.Equal("Scheduled", row.NextTrip.Status);
        Assert.Null(row.NextTrip.DriverId);
        Assert.Null(row.NextTrip.DriverName);
        Assert.Null(row.NextTrip.CurrentStopName);

        // Ghế: đã sinh đủ + sơ đồ của loại xe.
        Assert.Equal(45, row.SeatSummary.SeatsCreated);
        Assert.Equal(45, row.SeatSummary.Capacity);
        Assert.True(row.SeatSummary.IsComplete);
        Assert.NotNull(row.SeatSummary.Layout);
        Assert.Equal(1, row.SeatSummary.Layout!.NumberOfFloors);
        Assert.Equal(9, row.SeatSummary.Layout.RowsPerFloor);
        Assert.Equal(5, row.SeatSummary.Layout.ColumnsPerRow);
        Assert.Equal(45, row.SeatSummary.Layout.TotalSeats);
        Assert.Equal(2, row.SeatSummary.Layout.VipSeatCount);
    }

    [Fact]
    public async Task Chi_tinh_chuyen_dang_chay_va_scheduled_trong_7_ngay()
    {
        await using var db = TaoDb();
        var (bus, _, _) = await SeedDuLieuAsync(db);

        var service = new BusService(db);
        var result = await service.ListAsync(new ListBusesRequest { Page = 1, PageSize = 10 });
        var row = result.Data!.Items.Single(b => b.Id == bus.Id);

        // 99 chạy xong, 77 đã huỷ, 88 ở 30 ngày tới — cả ba đều KHÔNG phải phân công hiện tại.
        Assert.DoesNotContain(row.AssignedRoutes, r => r.RouteCode is "99" or "77" or "88");
    }

    [Fact]
    public async Task Xe_chua_xep_chuyen_tra_mang_rong_va_ghi_chu_chua_sinh_du_ghe()
    {
        await using var db = TaoDb();
        await SeedDuLieuAsync(db);

        var busChuaChay = new Bus
        {
            LicensePlate = "51B-000.01",
            BusType = BusType,
            Capacity = 45,
        };
        db.Buses.Add(busChuaChay);
        await db.SaveChangesAsync();

        var service = new BusService(db);
        var result = await service.GetByIdAsync(busChuaChay.Id);

        Assert.True(result.Success);
        var row = result.Data!;

        Assert.Empty(row.AssignedRoutes);
        Assert.Null(row.CurrentTrip);
        Assert.Null(row.NextTrip);
        Assert.Equal(0, row.SeatSummary.SeatsCreated);
        Assert.False(row.SeatSummary.IsComplete);
        // Sơ đồ của loại xe vẫn trả về dù xe chưa sinh ghế — đây là dữ liệu để cấu hình.
        Assert.NotNull(row.SeatSummary.Layout);
    }

    [Fact]
    public async Task Thong_tin_phan_cong_tra_ve_ca_o_hai_dau_get_danh_sach_va_get_chi_tiet()
    {
        await using var db = TaoDb();
        var (bus, _, _) = await SeedDuLieuAsync(db);

        var service = new BusService(db);
        var danhSach = await service.ListAsync(new ListBusesRequest { Page = 1, PageSize = 10 });
        var chiTiet = await service.GetByIdAsync(bus.Id);

        var tuDanhSach = danhSach.Data!.Items.Single(b => b.Id == bus.Id);
        var tuChiTiet = chiTiet.Data!;

        Assert.Equal(
            tuDanhSach.AssignedRoutes.Select(r => r.RouteId),
            tuChiTiet.AssignedRoutes.Select(r => r.RouteId));
        Assert.Equal(tuDanhSach.CurrentTrip?.TripId, tuChiTiet.CurrentTrip?.TripId);
        Assert.Equal(tuDanhSach.NextTrip?.TripId, tuChiTiet.NextTrip?.TripId);
        Assert.Equal(tuDanhSach.SeatSummary.SeatsCreated, tuChiTiet.SeatSummary.SeatsCreated);
    }

    // ---------------------------------------------------------------------------------------
    // Hạ tầng
    // ---------------------------------------------------------------------------------------

    private static AppDbContext TaoDb()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"doi-xe-{Guid.NewGuid()}")
            .Options);

    /// <summary>
    /// Một xe 45 chỗ đủ dữ liệu cho mọi nhánh: chuyến đang chạy, hai chuyến sắp chạy cùng tuyến,
    /// và ba chuyến phải bị loại (Completed, Cancelled, ngoài 7 ngày). Dùng mốc thời gian tương
    /// đối so với lúc chạy test — không ghim ngày tuyệt đối để test không hết hạn theo lịch.
    /// </summary>
    private static async Task<(Bus Bus, User Driver, Stop Stop)> SeedDuLieuAsync(AppDbContext db)
    {
        var now = DateTime.UtcNow;

        var role = new Role { Code = "Driver", Name = "Tài xế" };
        var driver = new User
        {
            PhoneNumber = "0900000003",
            FullName = "Tài xế mẫu",
            PasswordHash = "khong-dung-de-dang-nhap",
            RoleId = role.Id,
        };

        var stop = new Stop
        {
            Name = "Trạm Bến Thành",
            Address = "TP.HCM",
            Latitude = 10.77,
            Longitude = 106.70,
        };

        var bus = new Bus
        {
            LicensePlate = "29B-123.45",
            BusType = BusType,
            Capacity = 45,
        };

        // Xe khác có chuyến cùng khung giờ — chứng minh bộ lọc theo BusId, không lẫn sang xe này.
        var busKhac = new Bus
        {
            LicensePlate = "51B-999.99",
            BusType = BusType,
            Capacity = 45,
        };

        var layout = new SeatLayout
        {
            BusType = BusType,
            NumberOfFloors = 1,
            RowsPerFloor = 9,
            ColumnsPerRow = 5,
            TotalSeats = 45,
            VipSeatPositions = "1-1-1;1-1-2",
        };

        var tuyen01 = new Route { Code = "01", Name = "Bến Thành — Chợ Lớn" };
        var tuyenB10 = new Route { Code = "B10", Name = "Bến xe Mỹ Đình — Bến xe Gia Lâm" };
        var tuyenDaChay = new Route { Code = "99", Name = "Tuyến đã chạy xong" };
        var tuyenDaHuy = new Route { Code = "77", Name = "Tuyến đã huỷ" };
        var tuyenXa = new Route { Code = "88", Name = "Tuyến của tháng sau" };

        db.Roles.Add(role);
        db.Users.Add(driver);
        db.Stops.Add(stop);
        db.Buses.AddRange(bus, busKhac);
        db.SeatLayouts.Add(layout);
        db.Routes.AddRange(tuyen01, tuyenB10, tuyenDaChay, tuyenDaHuy, tuyenXa);

        for (var i = 1; i <= 45; i++)
        {
            db.Seats.Add(new Seat
            {
                BusId = bus.Id,
                SeatLayoutId = layout.Id,
                SeatNumber = $"A{i}",
                Floor = 1,
                RowIndex = (i - 1) / 5 + 1,
                ColumnIndex = (i - 1) % 5 + 1,
            });
        }

        db.Trips.AddRange(
            new Trip
            {
                RouteId = tuyen01.Id,
                BusId = bus.Id,
                DriverId = driver.Id,
                DepartureTime = now.AddHours(-1),
                Status = TripStatus.Running,
                CurrentStopId = stop.Id,
                PositionUpdatedAt = now.AddMinutes(-5),
            },
            new Trip
            {
                RouteId = tuyenB10.Id,
                BusId = bus.Id,
                DepartureTime = now.AddHours(2),
                Status = TripStatus.Scheduled,
            },
            new Trip
            {
                RouteId = tuyenB10.Id,
                BusId = bus.Id,
                DepartureTime = now.AddHours(3),
                Status = TripStatus.Scheduled,
            },
            new Trip
            {
                RouteId = tuyenDaChay.Id,
                BusId = bus.Id,
                DepartureTime = now.AddHours(-2),
                Status = TripStatus.Completed,
            },
            new Trip
            {
                RouteId = tuyenDaHuy.Id,
                BusId = bus.Id,
                DepartureTime = now.AddHours(1),
                Status = TripStatus.Cancelled,
            },
            new Trip
            {
                RouteId = tuyenXa.Id,
                BusId = bus.Id,
                DepartureTime = now.AddDays(30),
                Status = TripStatus.Scheduled,
            },
            new Trip
            {
                RouteId = tuyen01.Id,
                BusId = busKhac.Id,
                DepartureTime = now.AddHours(1),
                Status = TripStatus.Scheduled,
            });

        await db.SaveChangesAsync();

        return (bus, driver, stop);
    }
}
