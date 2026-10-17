using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;
using RouteEntity = SmartBus.Api.Entities.Route;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test tích hợp cho phần trạm lên xe của <c>POST /api/tickets/validate</c> — task *"API ghi nhận
/// lượt lên xe theo trạm"* (US 15, dòng 17 Sprint 4 — Nguyễn Duy Kiên).
///
/// Bài học quan trọng nhất nằm ở ca đầu: <c>TicketValidations.BoardingStopId</c> là trạm khách THỰC
/// SỰ bước lên, KHÁC <c>Tickets.BoardingStopId</c> (trạm khách chọn lúc mua — docs/34 §2.1). Vé mua
/// ở trạm A nhưng khách lên ở trạm B thì nhật ký ghi B, còn vé vẫn giữ A.
///
/// Dùng <see cref="TicketValidationTestFactory"/> (TestAppFactory bị sealed) — cùng lý do đã ghi ở
/// <c>TicketValidationApiTests</c> §4 của docs/41: bọc bằng <c>WithWebHostBuilder</c> sẽ ghi thẳng vào
/// PostgreSQL thật. Mỗi test một app + CSDL InMemory riêng; helper chép lại theo lệ của dự án.
/// </summary>
public class TicketBoardingStopApiTests
{
    // -------------------------------------------------------------- Luật 1: chỉ lượt Valid

    [Fact]
    public async Task Gui_tram_thuoc_tuyen_ghi_dung_tram_thuc_te_khac_tram_tren_ve()
    {
        using var factory = NewFactory();
        var driver = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế Test");
        var client = ClientWith(factory, factory.CreateTokenFor(driver));

        var route = await SeedRouteAsync(factory, "01");
        var tramTrenVe = await SeedStopAsync(factory, "Bến Thành");
        var tramThucTe = await SeedStopAsync(factory, "Chợ Lớn");
        await SeedRouteStopAsync(factory, route.Id, tramTrenVe.Id, stopOrder: 1);
        await SeedRouteStopAsync(factory, route.Id, tramThucTe.Id, stopOrder: 2);

        var trip = await SeedTripAsync(factory, route.Id, driver.Id, TuongLai(1));
        var ticket = await SeedTicketAsync(
            factory, trip.Id, boardingStopId: tramTrenVe.Id);

        var response = await client.PostAsJsonAsync("/api/tickets/validate", new
        {
            code = ticket.Code,
            tripId = trip.Id,
            boardingStopId = tramThucTe.Id,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("valid").GetBoolean());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.TicketValidations.SingleAsync();

        // Trạm THỰC TẾ vào nhật ký; trạm khách chọn lúc mua trên vé KHÔNG bị sửa.
        Assert.Equal(tramThucTe.Id, row.BoardingStopId);
        Assert.Equal(tramTrenVe.Id, db.Tickets.Single(t => t.Id == ticket.Id).BoardingStopId);
    }

    [Fact]
    public async Task Luot_Expired_khong_ghi_tram_du_tram_hop_le()
    {
        using var factory = NewFactory();
        var driver = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế Test");
        var client = ClientWith(factory, factory.CreateTokenFor(driver));

        var route = await SeedRouteAsync(factory, "02");
        var stop = await SeedStopAsync(factory, "Bến Thành");
        await SeedRouteStopAsync(factory, route.Id, stop.Id);

        // Chuyến đã khởi hành — vé không còn hiệu lực, nên đây KHÔNG phải lượt lên xe.
        var trip = await SeedTripAsync(
            factory, route.Id, driver.Id, DateTime.UtcNow.AddHours(-1));
        var ticket = await SeedTicketAsync(factory, trip.Id);

        var response = await client.PostAsJsonAsync("/api/tickets/validate", new
        {
            code = ticket.Code,
            tripId = trip.Id,
            boardingStopId = stop.Id,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("valid").GetBoolean());
        Assert.Equal("Expired", body.RootElement.GetProperty("reason").GetString());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.TicketValidations.SingleAsync();

        Assert.Equal(TicketValidationResult.Expired, row.Result);
        Assert.Null(row.BoardingStopId);
    }

    [Fact]
    public async Task Khong_gui_tram_thi_nhat_ky_de_null()
    {
        using var factory = NewFactory();
        var driver = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế Test");
        var client = ClientWith(factory, factory.CreateTokenFor(driver));

        var route = await SeedRouteAsync(factory, "03");
        var stop = await SeedStopAsync(factory, "Bến Thành");
        await SeedRouteStopAsync(factory, route.Id, stop.Id);
        var trip = await SeedTripAsync(factory, route.Id, driver.Id, TuongLai(1));
        var ticket = await SeedTicketAsync(factory, trip.Id);

        var response = await client.PostAsJsonAsync("/api/tickets/validate", new
        {
            code = ticket.Code,
            tripId = trip.Id,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.TicketValidations.SingleAsync();

        Assert.Equal(TicketValidationResult.Valid, row.Result);
        Assert.Null(row.BoardingStopId);
    }

    [Fact]
    public async Task Ma_rac_kem_tram_tra_NotFound_va_khong_ghi_tram()
    {
        using var factory = NewFactory();
        var driver = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế Test");
        var client = ClientWith(factory, factory.CreateTokenFor(driver));

        // Mã rác: không có vé/chuyến nào để đối chiếu tuyến — trạm gửi kèm bị bỏ qua, KHÔNG phải 400.
        var response = await client.PostAsJsonAsync("/api/tickets/validate", new
        {
            code = "chuoi-rac",
            boardingStopId = Guid.NewGuid(),
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("NotFound", body.RootElement.GetProperty("reason").GetString());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.TicketValidations.SingleAsync();

        Assert.Equal(TicketValidationResult.NotFound, row.Result);
        Assert.Null(row.BoardingStopId);
    }

    // -------------------------------------------------------------- Luật 2: trạm phải thuộc tuyến

    [Fact]
    public async Task Tram_cua_tuyen_khac_tra_400_kem_errors_boardingStopId_va_khong_ghi_dong_nao()
    {
        using var factory = NewFactory();
        var driver = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế Test");
        var client = ClientWith(factory, factory.CreateTokenFor(driver));

        var routeCuaChuyen = await SeedRouteAsync(factory, "04");
        var routeKhac = await SeedRouteAsync(factory, "05");
        var tramCuaTuyenKhac = await SeedStopAsync(factory, "Trạm Tuyến Khác");
        await SeedRouteStopAsync(factory, routeKhac.Id, tramCuaTuyenKhac.Id);

        var trip = await SeedTripAsync(factory, routeCuaChuyen.Id, driver.Id, TuongLai(1));
        var ticket = await SeedTicketAsync(factory, trip.Id);

        var response = await client.PostAsJsonAsync("/api/tickets/validate", new
        {
            code = ticket.Code,
            tripId = trip.Id,
            boardingStopId = tramCuaTuyenKhac.Id,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("boardingStopId", out _));

        // Lỗi phía gọi (gửi sai trạm) chứ không phải một lượt soát: không có dòng nhật ký nào —
        // cùng lối các lỗi 400 khác của recorder.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Empty(db.TicketValidations);
    }

    [Fact]
    public async Task Tram_khong_ton_tai_tra_400_kem_errors_boardingStopId()
    {
        using var factory = NewFactory();
        var driver = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế Test");
        var client = ClientWith(factory, factory.CreateTokenFor(driver));

        var route = await SeedRouteAsync(factory, "06");
        var trip = await SeedTripAsync(factory, route.Id, driver.Id, TuongLai(1));
        var ticket = await SeedTicketAsync(factory, trip.Id);

        var response = await client.PostAsJsonAsync("/api/tickets/validate", new
        {
            code = ticket.Code,
            tripId = trip.Id,
            boardingStopId = Guid.NewGuid(),   // trạm lạ, không có trong RouteStops
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("boardingStopId", out _));
    }

    // -------------------------------------------------------------- Luật 3: gửi lại không xét lại trạm

    [Fact]
    public async Task Gui_lai_cung_clientScanId_giu_tram_lan_dau_va_khong_de_them_dong()
    {
        using var factory = NewFactory();
        var driver = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế Test");
        var client = ClientWith(factory, factory.CreateTokenFor(driver));

        var route = await SeedRouteAsync(factory, "07");
        var tramDau = await SeedStopAsync(factory, "Bến Thành");
        var tramSau = await SeedStopAsync(factory, "Chợ Lớn");
        await SeedRouteStopAsync(factory, route.Id, tramDau.Id, stopOrder: 1);
        await SeedRouteStopAsync(factory, route.Id, tramSau.Id, stopOrder: 2);
        var trip = await SeedTripAsync(factory, route.Id, driver.Id, TuongLai(1));
        var ticket = await SeedTicketAsync(factory, trip.Id);
        var clientScanId = Guid.NewGuid();

        var first = await client.PostAsJsonAsync("/api/tickets/validate", new
        {
            code = ticket.Code,
            tripId = trip.Id,
            clientScanId,
            boardingStopId = tramDau.Id,
        });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Hàng đợi ngoại tuyến gửi lại cùng lượt (docs/39 §2.4): dòng đã ghi đứng vững, trạm của
        // lần gửi sau không được ghi đè.
        var second = await client.PostAsJsonAsync("/api/tickets/validate", new
        {
            code = ticket.Code,
            tripId = trip.Id,
            clientScanId,
            boardingStopId = tramSau.Id,
        });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.TicketValidations.SingleAsync();

        Assert.Equal(TicketValidationResult.Valid, row.Result);
        Assert.Equal(tramDau.Id, row.BoardingStopId);
    }

    [Fact]
    public async Task Gui_lai_cung_clientScanId_voi_tram_sai_tuyen_van_tra_200()
    {
        using var factory = NewFactory();
        var driver = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế Test");
        var client = ClientWith(factory, factory.CreateTokenFor(driver));

        var route = await SeedRouteAsync(factory, "08");
        var routeKhac = await SeedRouteAsync(factory, "09");
        var tramHopLe = await SeedStopAsync(factory, "Bến Thành");
        var tramSaiTuyen = await SeedStopAsync(factory, "Trạm Tuyến Khác");
        await SeedRouteStopAsync(factory, route.Id, tramHopLe.Id);
        await SeedRouteStopAsync(factory, routeKhac.Id, tramSaiTuyen.Id);

        var trip = await SeedTripAsync(factory, route.Id, driver.Id, TuongLai(1));
        var ticket = await SeedTicketAsync(factory, trip.Id);
        var clientScanId = Guid.NewGuid();

        var first = await client.PostAsJsonAsync("/api/tickets/validate", new
        {
            code = ticket.Code,
            tripId = trip.Id,
            clientScanId,
            boardingStopId = tramHopLe.Id,
        });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Trạm sai tuyến ở LẦN GỬI LẠI không được biến lượt đã thành công thành 400 — recorder đọc
        // trước và trả dòng gốc (docs/39 §2.4 mục 2), endpoint bỏ qua phép kiểm trạm.
        var second = await client.PostAsJsonAsync("/api/tickets/validate", new
        {
            code = ticket.Code,
            tripId = trip.Id,
            clientScanId,
            boardingStopId = tramSaiTuyen.Id,
        });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        using var body = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("valid").GetBoolean());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.TicketValidations.SingleAsync();

        Assert.Equal(tramHopLe.Id, row.BoardingStopId);
    }

    // =======================================================================================
    // Helper — mỗi lớp test tự chép, không có lớp base chung (lệ đang có của dự án)
    // =======================================================================================

    private static TicketValidationTestFactory NewFactory() => new();

    private static HttpClient ClientWith(TicketValidationTestFactory factory, string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    private static async Task<UserEntity> SeedUserAsync(
        TicketValidationTestFactory factory,
        Guid roleId,
        string roleCode,
        string fullName)
    {
        var role = new Role { Id = roleId, Code = roleCode, Name = roleCode };

        await factory.SeedAsync(db =>
        {
            if (!db.Roles.Any(r => r.Id == role.Id || r.Code == role.Code))
            {
                db.Roles.Add(role);
            }
        });

        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "09" + Random.Shared.Next(10_000_000, 99_999_999),
            FullName = fullName,
            PasswordHash = "hash-khong-dung-toi-trong-test",
            IsActive = true,
            RoleId = role.Id,
            UserRoles = [new UserRole { RoleId = role.Id }],
        };

        await factory.SeedAsync(db => db.Users.Add(user));

        return user;
    }

    private static async Task<RouteEntity> SeedRouteAsync(TicketValidationTestFactory factory, string code)
    {
        var route = new RouteEntity
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = $"Tuyến {code}",
            Origin = "Bến Thành",
            Destination = "Chợ Lớn",
        };

        await factory.SeedAsync(db => db.Routes.Add(route));

        return route;
    }

    private static async Task<Stop> SeedStopAsync(TicketValidationTestFactory factory, string name)
    {
        var stop = new Stop
        {
            Id = Guid.NewGuid(),
            Name = name,
            Address = $"Địa chỉ {name}",
            Latitude = 21.0307,
            Longitude = 105.8034,
        };

        await factory.SeedAsync(db => db.Stops.Add(stop));

        return stop;
    }

    private static async Task SeedRouteStopAsync(
        TicketValidationTestFactory factory,
        Guid routeId,
        Guid stopId,
        int stopOrder = 1)
    {
        await factory.SeedAsync(db => db.RouteStops.Add(new RouteStop
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            StopId = stopId,
            StopOrder = stopOrder,
            DistanceKm = 0m,
        }));
    }

    private static async Task<Seat> SeedSeatAsync(TicketValidationTestFactory factory, string seatNumber)
    {
        var seat = new Seat
        {
            Id = Guid.NewGuid(),
            BusId = Guid.NewGuid(),
            SeatLayoutId = Guid.NewGuid(),
            SeatNumber = seatNumber,
        };

        await factory.SeedAsync(db => db.Seats.Add(seat));

        return seat;
    }

    private static async Task<Trip> SeedTripAsync(
        TicketValidationTestFactory factory,
        Guid routeId,
        Guid driverId,
        DateTime departureTime)
    {
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            BusId = Guid.NewGuid(),
            DriverId = driverId,
            Status = TripStatus.Scheduled,
            DepartureTime = departureTime,
        };

        await factory.SeedAsync(db => db.Trips.Add(trip));

        return trip;
    }

    private static async Task<Ticket> SeedTicketAsync(
        TicketValidationTestFactory factory,
        Guid tripId,
        Guid? boardingStopId = null)
    {
        var seat = await SeedSeatAsync(factory, "B" + Random.Shared.Next(1, 99));
        var passenger = new UserEntity
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "09" + Random.Shared.Next(10_000_000, 99_999_999),
            FullName = "Hành Khách Test",
            PasswordHash = "hash-khong-dung-toi-trong-test",
            IsActive = true,
            RoleId = RoleIds.Passenger,
        };

        await factory.SeedAsync(db => db.Users.Add(passenger));

        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Code = string.Empty,
            TripId = tripId,
            SeatId = seat.Id,
            UserId = passenger.Id,
            BoardingStopId = boardingStopId,
            Price = 12_000,
            Status = TicketStatus.Paid,
        };

        await factory.SeedAsync(db => db.Tickets.Add(ticket));

        // Vé phát hành thật mang mã ký SẴN trong cột Code (docs/30 §3.1) — test quét chính mã này.
        using var scope = factory.Services.CreateScope();
        var qr = scope.ServiceProvider.GetRequiredService<ITicketQrService>();
        var code = qr.GenerateCode(ticket.Id);

        ticket.Code = code;

        await factory.SeedAsync(db =>
        {
            db.Tickets.Single(t => t.Id == ticket.Id).Code = code;
        });

        return ticket;
    }

    /// <summary>Mốc UTC trong tương lai — `day` càng lớn càng xa hiện tại.</summary>
    private static DateTime TuongLai(int day) => DateTime.UtcNow.AddDays(day);
}
