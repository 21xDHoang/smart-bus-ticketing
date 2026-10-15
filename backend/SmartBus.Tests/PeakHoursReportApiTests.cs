using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;
using RouteEntity = SmartBus.Api.Entities.Route;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test tích hợp cho API phân tích khung giờ cao điểm theo mật độ hành khách —
/// GET /api/reports/peak-hours (US 20 "Tỷ lệ lấp đầy", Sprint 4 — Trần Trung Hiếu). Hợp đồng đầy
/// đủ ở mục "Khung giờ cao điểm — /reports/peak-hours" của docs/api-contract.md.
///
/// Mỗi test dựng một app riêng với CSDL InMemory riêng để không lẫn dữ liệu sang nhau.
/// Helper seed chép lại trong từng lớp test, không có lớp base chung (đúng lệ đang có của dự án).
/// </summary>
public class PeakHoursReportApiTests
{
    // -------------------------------------------------------------- Quyền và xác thực

    [Fact]
    public async Task Khong_gui_token_thi_tra_401()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/reports/peak-hours")).StatusCode);
    }

    [Fact]
    public async Task Vai_tro_khach_hang_tra_403()
    {
        using var factory = new TestAppFactory();
        var passenger = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Hành Khách Test");
        var client = ClientWith(factory, factory.CreateTokenFor(passenger));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/reports/peak-hours")).StatusCode);
    }

    [Fact]
    public async Task Quan_ly_xem_duoc_bao_cao()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/reports/peak-hours")).StatusCode);
    }

    // -------------------------------------------------------------- Trục thời gian (giờ Việt Nam)

    [Fact]
    public async Task Gio_tinh_theo_gio_Viet_Nam_khong_phai_gio_UTC()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "20");
        var trip = await SeedTripAsync(factory, route.Id, departureTime: new DateTime(2026, 10, 14, 16, 59, 59, DateTimeKind.Utc));
        await SeedTicketAsync(factory, manager.Id, trip.Id);

        // Biên 17:00 UTC = 00:00 giờ Việt Nam NGÀY HÔM SAU — chuyến 16:59:59Z (23:59:59 VN)
        // phải rơi vào giờ 23 của ngày 14/10, không phải giờ 16 theo UTC.
        var body = JsonDocument.Parse(await (await client.GetAsync(
            "/api/reports/peak-hours?from=2026-10-14&to=2026-10-15")).Content.ReadAsStringAsync());

        var hour23 = FindHour(body, 23);
        Assert.Equal(1, hour23.GetProperty("passengers").GetInt32());
        Assert.Equal(1, hour23.GetProperty("trips").GetInt32());

        var hour16 = FindHour(body, 16);
        Assert.Equal(0, hour16.GetProperty("passengers").GetInt32());
    }

    [Fact]
    public async Task Chuyen_23h59_VN_va_00h00_VN_khac_bucket()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "20");

        // 16:59Z → 23:59 ngày 14/10 VN (giờ 23) · 17:00Z → 00:00 ngày 15/10 VN (giờ 0).
        var tripA = await SeedTripAsync(factory, route.Id, departureTime: new DateTime(2026, 10, 14, 16, 59, 0, DateTimeKind.Utc));
        var tripB = await SeedTripAsync(factory, route.Id, departureTime: new DateTime(2026, 10, 14, 17, 0, 0, DateTimeKind.Utc));
        await SeedTicketAsync(factory, manager.Id, tripA.Id);
        await SeedTicketAsync(factory, manager.Id, tripB.Id);

        var body = JsonDocument.Parse(await (await client.GetAsync(
            "/api/reports/peak-hours?from=2026-10-14&to=2026-10-15")).Content.ReadAsStringAsync());

        Assert.Equal(1, FindHour(body, 23).GetProperty("passengers").GetInt32());
        Assert.Equal(1, FindHour(body, 0).GetProperty("passengers").GetInt32());
    }

    // -------------------------------------------------------------- Nền số liệu

    [Fact]
    public async Task Chuyen_khoi_hanh_ngoai_khoang_khong_duoc_tinh()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "20");

        // Chuyến 00:30 ngày 14/10 VN nằm TRONG khoảng; chuyến 23:30 ngày 13/10 VN nằm NGOÀI.
        var inside = await SeedTripAsync(factory, route.Id, departureTime: new DateTime(2026, 10, 13, 17, 30, 0, DateTimeKind.Utc));
        var outside = await SeedTripAsync(factory, route.Id, departureTime: new DateTime(2026, 10, 13, 16, 30, 0, DateTimeKind.Utc));
        await SeedTicketAsync(factory, manager.Id, inside.Id);
        await SeedTicketAsync(factory, manager.Id, outside.Id);
        await SeedTicketAsync(factory, manager.Id, outside.Id);

        var body = JsonDocument.Parse(await (await client.GetAsync(
            "/api/reports/peak-hours?from=2026-10-14&to=2026-10-14")).Content.ReadAsStringAsync());

        Assert.Equal(1, body.RootElement.GetProperty("totalPassengers").GetInt32());
        Assert.Equal(1, body.RootElement.GetProperty("totalTrips").GetInt32());
    }

    [Fact]
    public async Task Ve_huy_khong_duoc_tinh_con_Paid_va_Used_deu_tinh()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "20");
        var trip = await SeedTripAsync(factory, route.Id, departureTime: new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc));   // 07:00 VN

        await SeedTicketAsync(factory, manager.Id, trip.Id, TicketStatus.Paid);
        await SeedTicketAsync(factory, manager.Id, trip.Id, TicketStatus.Used);
        await SeedTicketAsync(factory, manager.Id, trip.Id, TicketStatus.Cancelled);

        var body = JsonDocument.Parse(await (await client.GetAsync(
            "/api/reports/peak-hours?from=2026-10-14&to=2026-10-14")).Content.ReadAsStringAsync());

        // Vé huỷ = khách không lên xe — không vào mật độ; vé đã soát vẫn là khách ĐÃ ĐI thật.
        Assert.Equal(2, body.RootElement.GetProperty("totalPassengers").GetInt32());
        Assert.Equal(2, FindHour(body, 7).GetProperty("passengers").GetInt32());
    }

    // -------------------------------------------------------------- Quy tắc cao điểm

    [Fact]
    public async Task Gio_co_mat_do_tren_trung_binh_moi_la_cao_diem()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "20");
        var trip7 = await SeedTripAsync(factory, route.Id, departureTime: new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc));    // 07:00 VN
        var trip8 = await SeedTripAsync(factory, route.Id, departureTime: new DateTime(2026, 10, 14, 1, 0, 0, DateTimeKind.Utc));    // 08:00 VN
        var trip9 = await SeedTripAsync(factory, route.Id, departureTime: new DateTime(2026, 10, 14, 2, 0, 0, DateTimeKind.Utc));    // 09:00 VN

        // Mật độ: giờ 7 = 20 khách/chuyến · giờ 8 = 15 · giờ 9 = 5 → trung bình 13.33.
        for (var i = 0; i < 20; i++) await SeedTicketAsync(factory, manager.Id, trip7.Id);
        for (var i = 0; i < 15; i++) await SeedTicketAsync(factory, manager.Id, trip8.Id);
        for (var i = 0; i < 5; i++) await SeedTicketAsync(factory, manager.Id, trip9.Id);

        var body = JsonDocument.Parse(await (await client.GetAsync(
            "/api/reports/peak-hours?from=2026-10-14&to=2026-10-14")).Content.ReadAsStringAsync());

        var peakHours = body.RootElement.GetProperty("peakHours").EnumerateArray()
            .Select(h => h.GetInt32()).ToArray();

        Assert.Equal([7, 8], peakHours);
        Assert.True(FindHour(body, 7).GetProperty("isPeak").GetBoolean());
        Assert.True(FindHour(body, 8).GetProperty("isPeak").GetBoolean());
        Assert.False(FindHour(body, 9).GetProperty("isPeak").GetBoolean());
        Assert.False(FindHour(body, 0).GetProperty("isPeak").GetBoolean());
    }

    [Fact]
    public async Task Gio_khong_co_chuyen_khong_bao_gio_la_cao_diem()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "20");
        var trip = await SeedTripAsync(factory, route.Id, departureTime: new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc));   // 07:00 VN
        await SeedTicketAsync(factory, manager.Id, trip.Id);

        var body = JsonDocument.Parse(await (await client.GetAsync(
            "/api/reports/peak-hours?from=2026-10-14&to=2026-10-14")).Content.ReadAsStringAsync());

        // 23 giờ còn lại không có chuyến — dù mật độ hiển thị 0 cũng không được đánh dấu.
        Assert.All(
            body.RootElement.GetProperty("hours").EnumerateArray().Where(h => h.GetProperty("hour").GetInt32() != 7),
            h => Assert.False(h.GetProperty("isPeak").GetBoolean()));
    }

    [Fact]
    public async Task Ca_ky_khong_co_khach_thi_khong_co_gio_cao_diem()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "20");
        await SeedTripAsync(factory, route.Id, departureTime: new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc));
        await SeedTripAsync(factory, route.Id, departureTime: new DateTime(2026, 10, 14, 1, 0, 0, DateTimeKind.Utc));

        var body = JsonDocument.Parse(await (await client.GetAsync(
            "/api/reports/peak-hours?from=2026-10-14&to=2026-10-14")).Content.ReadAsStringAsync());

        // Có chuyến nhưng chưa bán được vé nào — ngưỡng trung bình bằng 0, đánh dấu cả là vô nghĩa.
        Assert.Empty(body.RootElement.GetProperty("peakHours").EnumerateArray());
        Assert.Equal(0, body.RootElement.GetProperty("averagePassengersPerTrip").GetDouble());
        Assert.All(body.RootElement.GetProperty("hours").EnumerateArray(),
            h => Assert.False(h.GetProperty("isPeak").GetBoolean()));
    }

    [Fact]
    public async Task Mot_gio_duy_nhat_co_chuyen_la_cao_diem()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "20");
        var trip = await SeedTripAsync(factory, route.Id, departureTime: new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc));   // 07:00 VN
        for (var i = 0; i < 10; i++) await SeedTicketAsync(factory, manager.Id, trip.Id);

        var body = JsonDocument.Parse(await (await client.GetAsync(
            "/api/reports/peak-hours?from=2026-10-14&to=2026-10-14")).Content.ReadAsStringAsync());

        // Mật độ giờ 7 BẰNG trung bình cả kỳ (10.0 = 10.0) — quy tắc so theo ≥ nên vẫn là cao điểm.
        Assert.Equal(7, Assert.Single(body.RootElement.GetProperty("peakHours").EnumerateArray()).GetInt32());
    }

    // -------------------------------------------------------------- Hình dạng trục giờ

    [Fact]
    public async Task Truc_24_gio_luon_day_du_gio_rong_van_hien_dien()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "20");
        var trip = await SeedTripAsync(factory, route.Id, departureTime: new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc));   // 07:00 VN
        await SeedTicketAsync(factory, manager.Id, trip.Id);

        var body = JsonDocument.Parse(await (await client.GetAsync(
            "/api/reports/peak-hours?from=2026-10-14&to=2026-10-14")).Content.ReadAsStringAsync());

        var hours = body.RootElement.GetProperty("hours").EnumerateArray().ToArray();

        // Trục cố định: đủ 24 cột từ 0 đến 23, giờ rỗng vẫn hiện diện với số 0.
        Assert.Equal(24, hours.Length);
        Assert.Equal(Enumerable.Range(0, 24), hours.Select(h => h.GetProperty("hour").GetInt32()));

        var empty = FindHour(body, 10);
        Assert.Equal(0, empty.GetProperty("passengers").GetInt32());
        Assert.Equal(0, empty.GetProperty("trips").GetInt32());
        Assert.Equal(0, empty.GetProperty("averagePassengersPerTrip").GetDouble());
    }

    [Fact]
    public async Task Nhieu_chuyen_cung_gio_gop_chung()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "20");
        var tripA = await SeedTripAsync(factory, route.Id, departureTime: new DateTime(2026, 10, 14, 0, 5, 0, DateTimeKind.Utc));   // 07:05 VN
        var tripB = await SeedTripAsync(factory, route.Id, departureTime: new DateTime(2026, 10, 14, 0, 50, 0, DateTimeKind.Utc));  // 07:50 VN

        for (var i = 0; i < 3; i++) await SeedTicketAsync(factory, manager.Id, tripA.Id);
        for (var i = 0; i < 4; i++) await SeedTicketAsync(factory, manager.Id, tripB.Id);

        var body = JsonDocument.Parse(await (await client.GetAsync(
            "/api/reports/peak-hours?from=2026-10-14&to=2026-10-14")).Content.ReadAsStringAsync());

        var hour7 = FindHour(body, 7);

        Assert.Equal(2, hour7.GetProperty("trips").GetInt32());
        Assert.Equal(7, hour7.GetProperty("passengers").GetInt32());
        Assert.Equal(3.5, hour7.GetProperty("averagePassengersPerTrip").GetDouble());
    }

    [Fact]
    public async Task Tong_khop_voi_tong_cac_bucket()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "20");
        var trip7 = await SeedTripAsync(factory, route.Id, departureTime: new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc));    // 07:00 VN
        var trip17 = await SeedTripAsync(factory, route.Id, departureTime: new DateTime(2026, 10, 14, 10, 0, 0, DateTimeKind.Utc));   // 17:00 VN

        for (var i = 0; i < 10; i++) await SeedTicketAsync(factory, manager.Id, trip7.Id);
        for (var i = 0; i < 7; i++) await SeedTicketAsync(factory, manager.Id, trip17.Id);

        var body = JsonDocument.Parse(await (await client.GetAsync(
            "/api/reports/peak-hours?from=2026-10-14&to=2026-10-14")).Content.ReadAsStringAsync());

        // Đẳng thức là phép kiểm THẬT: tổng truy vấn riêng trong CSDL, bucket gộp từ dòng thô —
        // phép gộp lệch (rơi dòng ở biên múi giờ) là test này đỏ ngay.
        var totalPassengers = body.RootElement.GetProperty("totalPassengers").GetInt32();
        var totalTrips = body.RootElement.GetProperty("totalTrips").GetInt32();
        var hours = body.RootElement.GetProperty("hours").EnumerateArray();

        Assert.Equal(17, totalPassengers);
        Assert.Equal(2, totalTrips);
        Assert.Equal(totalPassengers, hours.Sum(h => h.GetProperty("passengers").GetInt32()));
        Assert.Equal(totalTrips, hours.Sum(h => h.GetProperty("trips").GetInt32()));
    }

    [Fact]
    public async Task Bo_trong_tham_so_lay_30_ngay_gan_nhat()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var body = JsonDocument.Parse(await (await client.GetAsync(
            "/api/reports/peak-hours")).Content.ReadAsStringAsync());

        // Mặc định: 30 ngày gần nhất tính cả hôm nay (giờ VN), trục vẫn đủ 24 giờ.
        var from = DateOnly.Parse(body.RootElement.GetProperty("from").GetString()!);
        var to = DateOnly.Parse(body.RootElement.GetProperty("to").GetString()!);

        Assert.Equal(to.AddDays(-29), from);
        Assert.Equal(24, body.RootElement.GetProperty("hours").GetArrayLength());

        var todayVietnam = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        Assert.Equal(todayVietnam, to);
    }

    // -------------------------------------------------------------- Lọc theo tuyến

    [Fact]
    public async Task Loc_theo_tuyen_chi_tinh_chuyen_cua_tuyen_do()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var routeA = await SeedRouteAsync(factory, "20A");
        var routeB = await SeedRouteAsync(factory, "20B");
        var tripA = await SeedTripAsync(factory, routeA.Id, departureTime: new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc));
        var tripB = await SeedTripAsync(factory, routeB.Id, departureTime: new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc));

        await SeedTicketAsync(factory, manager.Id, tripA.Id);
        await SeedTicketAsync(factory, manager.Id, tripB.Id);
        await SeedTicketAsync(factory, manager.Id, tripB.Id);

        var body = JsonDocument.Parse(await (await client.GetAsync(
            $"/api/reports/peak-hours?from=2026-10-14&to=2026-10-14&routeId={routeA.Id}")).Content.ReadAsStringAsync());

        Assert.Equal(1, body.RootElement.GetProperty("totalPassengers").GetInt32());
        Assert.Equal(1, body.RootElement.GetProperty("totalTrips").GetInt32());
    }

    [Fact]
    public async Task Loc_theo_tuyen_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var response = await client.GetAsync(
            $"/api/reports/peak-hours?from=2026-10-14&to=2026-10-14&routeId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // -------------------------------------------------------------- Dữ liệu đầu vào

    [Fact]
    public async Task To_truoc_from_tra_400_va_loi_o_ca_hai_o_ngay()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var response = await client.GetAsync("/api/reports/peak-hours?from=2026-10-15&to=2026-10-14");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = body.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("from", out _));
        Assert.True(errors.TryGetProperty("to", out _));
    }

    [Fact]
    public async Task Khoang_qua_366_ngay_tra_400()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        // 367 ngày — vượt trần 366 đúng một ngày (2025-01-01 → 2026-01-02).
        var response = await client.GetAsync("/api/reports/peak-hours?from=2025-01-01&to=2026-01-02");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // =======================================================================================
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // =======================================================================================

    private static JsonElement FindHour(JsonDocument body, int hour)
        => body.RootElement.GetProperty("hours").EnumerateArray()
            .Single(h => h.GetProperty("hour").GetInt32() == hour);

    private static HttpClient ClientWith(TestAppFactory factory, string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    private static async Task<UserEntity> SeedUserAsync(
        TestAppFactory factory,
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

    /// <summary>Chuyến chỉ có khoá ngoại, không gán navigation (đúng lệ "seed dòng mồ côi").</summary>
    private static async Task<Trip> SeedTripAsync(
        TestAppFactory factory,
        Guid routeId,
        DateTime departureTime)
    {
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            BusId = Guid.NewGuid(),
            DepartureTime = departureTime,
        };

        await factory.SeedAsync(db => db.Trips.Add(trip));

        return trip;
    }

    /// <summary>Vé mồ côi kiểu revenue: SeatId là Guid rác — InMemory không kiểm khoá ngoại.</summary>
    private static async Task<Ticket> SeedTicketAsync(
        TestAppFactory factory,
        Guid userId,
        Guid tripId,
        TicketStatus status = TicketStatus.Paid)
    {
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Code = "TKT-" + Guid.NewGuid().ToString("N")[..8],
            TripId = tripId,
            SeatId = Guid.NewGuid(),
            UserId = userId,
            Price = 50_000m,
            Status = status,
            CreatedAt = Ngay(1),
        };

        await factory.SeedAsync(db => db.Tickets.Add(ticket));

        return ticket;
    }

    /// <summary>Mốc UTC trong quá khứ xa — `day` càng lớn càng gần hiện tại.</summary>
    private static DateTime Ngay(int day)
        => new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc).AddDays(day);
}
