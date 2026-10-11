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
/// Test tích hợp cho API tra cứu vé của tôi — GET /api/tickets/me và GET /api/tickets/{id}
/// (US 4, dòng 45 — Trần Trung Hiếu). Hợp đồng đầy đủ ở mục "Vé điện tử" của docs/api-contract.md.
///
/// Mỗi test dựng một app riêng với CSDL InMemory riêng để không lẫn dữ liệu sang nhau.
/// Helper seed chép lại trong từng lớp test, không có lớp base chung (đúng lệ đang có của dự án).
/// </summary>
public class MyTicketsApiTests
{
    // -------------------------------------------------------------- Quyền và xác thực

    [Fact]
    public async Task Khong_gui_token_thi_tra_401()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/tickets/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/tickets/{Guid.NewGuid()}")).StatusCode);
    }

    // -------------------------------------------------------------- Danh sách vé của tôi

    [Fact]
    public async Task Chua_co_ve_nao_thi_tra_mang_rong_khong_phai_404()
    {
        using var factory = new TestAppFactory();
        var client = await ClientAsPassengerAsync(factory);

        var response = await client.GetAsync("/api/tickets/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, body.RootElement.ValueKind);
        Assert.Equal(0, body.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Danh_sach_chi_tra_ve_cua_chinh_nguoi_goi()
    {
        using var factory = new TestAppFactory();
        var chuVe = await SeedUserAsync(factory);
        var nguoiKhac = await SeedUserAsync(factory);
        var client = ClientWith(factory, factory.CreateTokenFor(chuVe));

        var route = await SeedRouteAsync(factory, "01");
        var trip = await SeedTripAsync(factory, route.Id);
        var seat = await SeedSeatAsync(factory, "A12");

        var veMoiNhat = await SeedTicketAsync(factory, chuVe.Id, trip.Id, seat.Id, code: "SBT1-AAA", createdAt: Ngay(3));
        await SeedTicketAsync(factory, chuVe.Id, trip.Id, seat.Id, code: "SBT1-BBB", createdAt: Ngay(1));
        await SeedTicketAsync(factory, nguoiKhac.Id, trip.Id, seat.Id, code: "SBT1-CCC", createdAt: Ngay(2));

        var body = JsonDocument.Parse(await (await client.GetAsync("/api/tickets/me")).Content.ReadAsStringAsync());
        var items = body.RootElement.EnumerateArray().ToArray();

        // Đúng hai vé của chủ — vé của người khác không lọt vào, và không có endpoint nào hỏi theo
        // người khác được (phạm vi "của tôi" nằm trong truy vấn).
        Assert.Equal(2, items.Length);
        Assert.DoesNotContain(items, item => item.GetProperty("code").GetString() == "SBT1-CCC");

        // Service sắp CreatedAt giảm dần để kết quả ổn định giữa các lượt gọi — hợp đồng không hứa
        // thứ tự, nhưng ca này ghim hành vi thật lại để ai đổi cũng thấy ngay.
        Assert.Equal("SBT1-AAA", items[0].GetProperty("code").GetString());
        Assert.Equal(veMoiNhat.Id, items[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Ve_tra_day_du_truong_hien_thi_theo_bang_truong_hop_dong()
    {
        using var factory = new TestAppFactory();
        var chuVe = await SeedUserAsync(factory);
        var client = ClientWith(factory, factory.CreateTokenFor(chuVe));

        var route = await SeedRouteAsync(factory, "B10");
        var trip = await SeedTripAsync(factory, route.Id, departure: Ngay(5));
        var seat = await SeedSeatAsync(factory, "D02");
        var tramLen = await SeedStopAsync(factory, "Bến xe Yên Nghĩa");

        await SeedTicketAsync(
            factory,
            chuVe.Id,
            trip.Id,
            seat.Id,
            code: "SBT-B10-3E4F5A",
            price: 15_000m,
            status: TicketStatus.Paid,
            boardingStopId: tramLen.Id);

        var body = JsonDocument.Parse(await (await client.GetAsync("/api/tickets/me")).Content.ReadAsStringAsync());
        var ve = Assert.Single(body.RootElement.EnumerateArray());

        // Khớp đúng bảng trường "Hình dạng Ticket" của hợp đồng: tên tuyến/điểm đi đến/ghế nối sẵn
        // để màn "Vé của tôi" vẽ một dòng vé không phải gọi thêm endpoint nào.
        Assert.Equal("B10", ve.GetProperty("routeCode").GetString());
        Assert.Equal("Tuyến B10", ve.GetProperty("routeName").GetString());
        Assert.Equal("Bến Thành", ve.GetProperty("origin").GetString());
        Assert.Equal("Chợ Lớn", ve.GetProperty("destination").GetString());
        Assert.Equal("D02", ve.GetProperty("seatNumber").GetString());
        Assert.Equal(15_000m, ve.GetProperty("price").GetDecimal());
        Assert.Equal("Paid", ve.GetProperty("status").GetString());

        // Trạm lên có giá trị, trạm xuống không chọn — null đúng như bảng trường đã đổi.
        Assert.Equal("Bến xe Yên Nghĩa", ve.GetProperty("boardingStopName").GetString());
        Assert.Equal(JsonValueKind.Null, ve.GetProperty("alightingStopName").ValueKind);

        // Vé chưa soát thì usedAt null; createdAt là ảnh chụp lúc phát hành.
        Assert.Equal(JsonValueKind.Null, ve.GetProperty("usedAt").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, ve.GetProperty("createdAt").ValueKind);
    }

    [Fact]
    public async Task Ve_da_dung_tra_usedAt_con_ve_huy_thi_khong()
    {
        using var factory = new TestAppFactory();
        var chuVe = await SeedUserAsync(factory);
        var client = ClientWith(factory, factory.CreateTokenFor(chuVe));

        var route = await SeedRouteAsync(factory, "08");
        var trip = await SeedTripAsync(factory, route.Id);
        var seat = await SeedSeatAsync(factory, "A01");

        await SeedTicketAsync(factory, chuVe.Id, trip.Id, seat.Id, code: "SBT-08-USED", status: TicketStatus.Used, usedAt: Ngay(1));
        await SeedTicketAsync(factory, chuVe.Id, trip.Id, seat.Id, code: "SBT-08-CANC", status: TicketStatus.Cancelled);

        var body = JsonDocument.Parse(await (await client.GetAsync("/api/tickets/me")).Content.ReadAsStringAsync());
        var items = body.RootElement.EnumerateArray().ToArray();

        var daDung = Assert.Single(items, item => item.GetProperty("status").GetString() == "Used");
        var daHuy = Assert.Single(items, item => item.GetProperty("status").GetString() == "Cancelled");

        // usedAt chỉ khác null khi vé đã soát — đúng doc-comment của Ticket.UsedAt.
        Assert.NotEqual(JsonValueKind.Null, daDung.GetProperty("usedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, daHuy.GetProperty("usedAt").ValueKind);
    }

    // -------------------------------------------------------------- Chi tiết một vé

    [Fact]
    public async Task Chi_tiet_ve_cua_minh_tra_200()
    {
        using var factory = new TestAppFactory();
        var chuVe = await SeedUserAsync(factory);
        var client = ClientWith(factory, factory.CreateTokenFor(chuVe));

        var route = await SeedRouteAsync(factory, "01");
        var trip = await SeedTripAsync(factory, route.Id);
        var seat = await SeedSeatAsync(factory, "A12");
        var ve = await SeedTicketAsync(factory, chuVe.Id, trip.Id, seat.Id, code: "SBT1-CHITIET");

        var response = await client.GetAsync($"/api/tickets/{ve.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(ve.Id, body.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("SBT1-CHITIET", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Chi_tiet_ve_cua_nguoi_khac_tra_404_khong_phai_403()
    {
        using var factory = new TestAppFactory();
        var chuVe = await SeedUserAsync(factory);
        var nguoiKhac = await SeedUserAsync(factory);
        var client = ClientWith(factory, factory.CreateTokenFor(chuVe));

        var route = await SeedRouteAsync(factory, "01");
        var trip = await SeedTripAsync(factory, route.Id);
        var seat = await SeedSeatAsync(factory, "A12");
        var veNguoiKhac = await SeedTicketAsync(factory, nguoiKhac.Id, trip.Id, seat.Id, code: "SBT1-NGUOIKHAC");

        // 404 chứ không phải 403: không để lộ vé nào đang tồn tại — cùng lối phiên giữ chỗ và phản ánh.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/tickets/{veNguoiKhac.Id}")).StatusCode);
    }

    [Fact]
    public async Task Chi_tiet_ve_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var chuVe = await SeedUserAsync(factory);
        var client = ClientWith(factory, factory.CreateTokenFor(chuVe));

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/tickets/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Id_khong_phai_guid_thi_ra_404_do_rang_buoc_route()
    {
        using var factory = new TestAppFactory();
        var chuVe = await SeedUserAsync(factory);
        var client = ClientWith(factory, factory.CreateTokenFor(chuVe));

        // Ràng buộc {id:guid} chặn từ routing — "abc" không khớp route chi tiết nên 404,
        // không chui vào endpoint (bề mặt api/tickets sẽ còn thêm đoạn literal ở Sprint sau).
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/tickets/abc")).StatusCode);
    }

    // =======================================================================================
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // =======================================================================================

    private static HttpClient ClientWith(TestAppFactory factory, string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    private static async Task<HttpClient> ClientAsPassengerAsync(TestAppFactory factory)
        => ClientWith(factory, factory.CreateTokenFor(await SeedUserAsync(factory)));

    private static async Task<UserEntity> SeedUserAsync(TestAppFactory factory)
    {
        var role = new Role { Id = RoleIds.Passenger, Code = RoleCodes.Passenger, Name = RoleCodes.Passenger };

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
            FullName = "Hành Khách Test",
            PasswordHash = PasswordService.HashPassword("matkhau123"),
            IsActive = true,
            RoleId = role.Id,
            UserRoles = [new UserRole { RoleId = role.Id }],
        };

        await factory.SeedAsync(db => db.Users.Add(user));

        return user;
    }

    private static async Task<RouteEntity> SeedRouteAsync(TestAppFactory factory, string code)
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

    private static async Task<Stop> SeedStopAsync(TestAppFactory factory, string name)
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

    private static async Task<Seat> SeedSeatAsync(TestAppFactory factory, string seatNumber)
    {
        var seat = new Seat
        {
            Id = Guid.NewGuid(),
            BusId = Guid.NewGuid(),
            SeatLayoutId = Guid.NewGuid(),
            Floor = 1,
            RowIndex = 1,
            ColumnIndex = 1,
            SeatNumber = seatNumber,
        };

        await factory.SeedAsync(db => db.Seats.Add(seat));

        return seat;
    }

    /// <summary>
    /// Chuyến chỉ có khoá ngoại, không gán navigation — InMemory không cưỡng chế khoá ngoại nên
    /// không cần seed Route/Bus cho từng chuyến (đúng lệ "seed dòng mồ côi" của AuditLogCoverageTests).
    /// </summary>
    private static async Task<Trip> SeedTripAsync(TestAppFactory factory, Guid routeId, DateTime? departure = null)
    {
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            BusId = Guid.NewGuid(),
            DepartureTime = departure ?? Ngay(1),
        };

        await factory.SeedAsync(db => db.Trips.Add(trip));

        return trip;
    }

    private static async Task<Ticket> SeedTicketAsync(
        TestAppFactory factory,
        Guid userId,
        Guid tripId,
        Guid seatId,
        string code,
        decimal price = 12_000m,
        TicketStatus status = TicketStatus.Paid,
        DateTime? createdAt = null,
        DateTime? usedAt = null,
        Guid? boardingStopId = null,
        Guid? alightingStopId = null)
    {
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Code = code,
            TripId = tripId,
            SeatId = seatId,
            UserId = userId,
            BoardingStopId = boardingStopId,
            AlightingStopId = alightingStopId,
            Price = price,
            Status = status,
            CreatedAt = createdAt ?? Ngay(1),
            UsedAt = usedAt,
        };

        await factory.SeedAsync(db => db.Tickets.Add(ticket));

        return ticket;
    }

    /// <summary>Mốc UTC trong quá khứ xa — `day` càng lớn càng gần hiện tại.</summary>
    private static DateTime Ngay(int day)
        => new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc).AddDays(day);
}
