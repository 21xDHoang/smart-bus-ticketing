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
/// Test tích hợp cho API so sánh doanh thu giữa các kỳ — GET /api/reports/revenue/comparison
/// (US 19 "Doanh thu", Sprint 4 — Phùng Duy Hoàng). Hợp đồng đầy đủ ở mục
/// "Báo cáo doanh thu — /reports/revenue" của docs/api-contract.md.
///
/// Mỗi test dựng một app riêng với CSDL InMemory riêng để không lẫn dữ liệu sang nhau.
/// Helper seed chép lại trong từng lớp test, không có lớp base chung (đúng lệ đang có của dự án).
/// </summary>
public class RevenueComparisonApiTests
{
    // -------------------------------------------------------------- Quyền và xác thực

    [Fact]
    public async Task Khong_gui_token_thi_tra_401()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/reports/revenue/comparison")).StatusCode);
    }

    [Fact]
    public async Task Vai_tro_khach_hang_tra_403()
    {
        using var factory = new TestAppFactory();
        var passenger = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Hành Khách Test");
        var client = ClientWith(factory, factory.CreateTokenFor(passenger));

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/reports/revenue/comparison")).StatusCode);
    }

    [Fact]
    public async Task Quan_ly_xem_duoc_so_sanh()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync("/api/reports/revenue/comparison")).StatusCode);
    }

    // -------------------------------------------------------------- Ranh giới hai kỳ

    [Fact]
    public async Task Ky_truoc_cung_do_dai_va_ket_thuc_ngay_truoc_from()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var body = JsonDocument.Parse(await (await client.GetAsync(
            "/api/reports/revenue/comparison?from=2026-10-11&to=2026-10-17")).Content.ReadAsStringAsync());

        // 11/10 → 17/10 là 7 ngày; kỳ trước phải là 7 ngày liền trước: 04/10 → 10/10.
        Assert.Equal("2026-10-11", body.RootElement.GetProperty("from").GetString());
        Assert.Equal("2026-10-17", body.RootElement.GetProperty("to").GetString());
        Assert.Equal("2026-10-04", body.RootElement.GetProperty("previousFrom").GetString());
        Assert.Equal("2026-10-10", body.RootElement.GetProperty("previousTo").GetString());
    }

    [Fact]
    public async Task Bien_ngay_giua_hai_ky_theo_gio_Viet_Nam()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var trip = await SeedTripAsync(factory, route.Id);

        // Kỳ hiện tại 15/10 → 17/10, kỳ trước 12/10 → 14/10. Biên là 00:00 giờ VN ngày 15/10
        // (= 17:00 UTC ngày 14): 16:59:59Z vẫn thuộc kỳ TRƯỚC, 17:00:00Z đã sang kỳ HIỆN TẠI.
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 10_000m,
            paidAt: new DateTime(2026, 10, 14, 16, 59, 59, DateTimeKind.Utc));
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 20_000m,
            paidAt: new DateTime(2026, 10, 14, 17, 0, 0, DateTimeKind.Utc));

        var body = JsonDocument.Parse(await (await client.GetAsync(
            "/api/reports/revenue/comparison?from=2026-10-15&to=2026-10-17")).Content.ReadAsStringAsync());

        Assert.Equal(20_000m, body.RootElement.GetProperty("currentRevenue").GetDecimal());
        Assert.Equal(10_000m, body.RootElement.GetProperty("previousRevenue").GetDecimal());
        Assert.Equal(10_000m, body.RootElement.GetProperty("revenueChange").GetDecimal());
    }

    // -------------------------------------------------------------- Tăng / giảm

    [Fact]
    public async Task Doanh_thu_tang_tra_chenh_lech_duong_va_phan_tram()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var trip = await SeedTripAsync(factory, route.Id);

        // Kỳ trước 100.000đ / 1 giao dịch; kỳ hiện tại 150.000đ / 2 giao dịch.
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 100_000m, paidAt: Ngay(5));
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 100_000m, paidAt: Ngay(12));
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 50_000m, paidAt: Ngay(13));

        var body = JsonDocument.Parse(await (await client.GetAsync(
            "/api/reports/revenue/comparison?from=2026-10-11&to=2026-10-17")).Content.ReadAsStringAsync());

        Assert.Equal(150_000m, body.RootElement.GetProperty("currentRevenue").GetDecimal());
        Assert.Equal(100_000m, body.RootElement.GetProperty("previousRevenue").GetDecimal());
        Assert.Equal(50_000m, body.RootElement.GetProperty("revenueChange").GetDecimal());
        Assert.Equal(50m, body.RootElement.GetProperty("revenueChangePercent").GetDecimal());

        Assert.Equal(2, body.RootElement.GetProperty("currentPayments").GetInt32());
        Assert.Equal(1, body.RootElement.GetProperty("previousPayments").GetInt32());
        Assert.Equal(1, body.RootElement.GetProperty("paymentsChange").GetInt32());
        Assert.Equal(100m, body.RootElement.GetProperty("paymentsChangePercent").GetDecimal());
    }

    [Fact]
    public async Task Doanh_thu_giam_tra_chenh_lech_am()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var trip = await SeedTripAsync(factory, route.Id);

        await SeedPaymentAsync(factory, manager.Id, trip.Id, 100_000m, paidAt: Ngay(5));
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 50_000m, paidAt: Ngay(12));

        var body = JsonDocument.Parse(await (await client.GetAsync(
            "/api/reports/revenue/comparison?from=2026-10-11&to=2026-10-17")).Content.ReadAsStringAsync());

        Assert.Equal(50_000m, body.RootElement.GetProperty("currentRevenue").GetDecimal());
        Assert.Equal(-50_000m, body.RootElement.GetProperty("revenueChange").GetDecimal());
        Assert.Equal(-50m, body.RootElement.GetProperty("revenueChangePercent").GetDecimal());
        // Mỗi kỳ vẫn đúng 1 giao dịch — số giao dịch không đổi dù tiền giảm.
        Assert.Equal(0, body.RootElement.GetProperty("paymentsChange").GetInt32());
    }

    [Fact]
    public async Task Khong_doi_thi_chenh_lech_va_phan_tram_bang_khong()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var trip = await SeedTripAsync(factory, route.Id);

        await SeedPaymentAsync(factory, manager.Id, trip.Id, 100_000m, paidAt: Ngay(5));
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 100_000m, paidAt: Ngay(12));

        var body = JsonDocument.Parse(await (await client.GetAsync(
            "/api/reports/revenue/comparison?from=2026-10-11&to=2026-10-17")).Content.ReadAsStringAsync());

        Assert.Equal(0m, body.RootElement.GetProperty("revenueChange").GetDecimal());
        Assert.Equal(0m, body.RootElement.GetProperty("revenueChangePercent").GetDecimal());
        Assert.Equal(0, body.RootElement.GetProperty("paymentsChange").GetInt32());
        Assert.Equal(0m, body.RootElement.GetProperty("paymentsChangePercent").GetDecimal());
    }

    [Fact]
    public async Task Ky_truoc_bang_khong_thi_phan_tram_null_con_chenh_lech_van_co()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var trip = await SeedTripAsync(factory, route.Id);

        // Chỉ kỳ hiện tại có doanh thu — không có nền để chia %.
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 100_000m, paidAt: Ngay(12));

        var body = JsonDocument.Parse(await (await client.GetAsync(
            "/api/reports/revenue/comparison?from=2026-10-11&to=2026-10-17")).Content.ReadAsStringAsync());

        Assert.Equal(0m, body.RootElement.GetProperty("previousRevenue").GetDecimal());
        Assert.Equal(100_000m, body.RootElement.GetProperty("revenueChange").GetDecimal());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("revenueChangePercent").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("paymentsChangePercent").ValueKind);
    }

    [Fact]
    public async Task Ca_hai_ky_bang_khong_thi_phan_tram_null()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var body = JsonDocument.Parse(await (await client.GetAsync(
            "/api/reports/revenue/comparison?from=2026-10-11&to=2026-10-17")).Content.ReadAsStringAsync());

        Assert.Equal(0m, body.RootElement.GetProperty("currentRevenue").GetDecimal());
        Assert.Equal(0m, body.RootElement.GetProperty("previousRevenue").GetDecimal());
        Assert.Equal(0m, body.RootElement.GetProperty("revenueChange").GetDecimal());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("revenueChangePercent").ValueKind);
    }

    [Fact]
    public async Task Phan_tram_lam_tron_hai_chu_so_thap_phan()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var trip = await SeedTripAsync(factory, route.Id);

        // (100 − 3) / 3 × 100 = 3233,333… → 3233,33.
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 3m, paidAt: Ngay(5));
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 100m, paidAt: Ngay(12));

        var body = JsonDocument.Parse(await (await client.GetAsync(
            "/api/reports/revenue/comparison?from=2026-10-11&to=2026-10-17")).Content.ReadAsStringAsync());

        Assert.Equal(3233.33m, body.RootElement.GetProperty("revenueChangePercent").GetDecimal());
    }

    // -------------------------------------------------------------- Nền số liệu (cùng báo cáo gốc)

    [Fact]
    public async Task Chi_tinh_giao_dich_Success()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var trip = await SeedTripAsync(factory, route.Id);

        await SeedPaymentAsync(factory, manager.Id, trip.Id, 100_000m, paidAt: Ngay(5));
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 50_000m, status: PaymentStatus.Pending, paidAt: Ngay(5));
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 100_000m, paidAt: Ngay(12));
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 20_000m, status: PaymentStatus.Failed, paidAt: Ngay(12));

        var body = JsonDocument.Parse(await (await client.GetAsync(
            "/api/reports/revenue/comparison?from=2026-10-11&to=2026-10-17")).Content.ReadAsStringAsync());

        Assert.Equal(100_000m, body.RootElement.GetProperty("currentRevenue").GetDecimal());
        Assert.Equal(100_000m, body.RootElement.GetProperty("previousRevenue").GetDecimal());
        Assert.Equal(1, body.RootElement.GetProperty("currentPayments").GetInt32());
        Assert.Equal(1, body.RootElement.GetProperty("previousPayments").GetInt32());
    }

    [Fact]
    public async Task Giao_dich_thieu_PaidAt_thi_dung_CreatedAt()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var trip = await SeedTripAsync(factory, route.Id);

        // Cả hai đều chưa thanh toán xong nên PaidAt null; mốc rơi vào CreatedAt.
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 70_000m,
            createdAt: new DateTime(2026, 10, 5, 3, 0, 0, DateTimeKind.Utc));
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 40_000m,
            createdAt: new DateTime(2026, 10, 12, 3, 0, 0, DateTimeKind.Utc));

        var body = JsonDocument.Parse(await (await client.GetAsync(
            "/api/reports/revenue/comparison?from=2026-10-11&to=2026-10-17")).Content.ReadAsStringAsync());

        Assert.Equal(40_000m, body.RootElement.GetProperty("currentRevenue").GetDecimal());
        Assert.Equal(70_000m, body.RootElement.GetProperty("previousRevenue").GetDecimal());
    }

    // -------------------------------------------------------------- Lọc tuyến

    [Fact]
    public async Task Loc_theo_tuyen_ap_dung_cho_ca_hai_ky()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var routeA = await SeedRouteAsync(factory, "A");
        var routeB = await SeedRouteAsync(factory, "B");
        var tripA = await SeedTripAsync(factory, routeA.Id);
        var tripB = await SeedTripAsync(factory, routeB.Id);

        await SeedPaymentAsync(factory, manager.Id, tripA.Id, 10_000m, paidAt: Ngay(5));
        await SeedPaymentAsync(factory, manager.Id, tripB.Id, 90_000m, paidAt: Ngay(5));
        await SeedPaymentAsync(factory, manager.Id, tripA.Id, 30_000m, paidAt: Ngay(12));
        await SeedPaymentAsync(factory, manager.Id, tripB.Id, 70_000m, paidAt: Ngay(12));

        var body = JsonDocument.Parse(await (await client.GetAsync(
            $"/api/reports/revenue/comparison?from=2026-10-11&to=2026-10-17&routeId={routeA.Id}"))
            .Content.ReadAsStringAsync());

        Assert.Equal(30_000m, body.RootElement.GetProperty("currentRevenue").GetDecimal());
        Assert.Equal(10_000m, body.RootElement.GetProperty("previousRevenue").GetDecimal());
        Assert.Equal(20_000m, body.RootElement.GetProperty("revenueChange").GetDecimal());
    }

    [Fact]
    public async Task Tuyen_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var response = await client.GetAsync(
            $"/api/reports/revenue/comparison?routeId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Không tìm thấy tuyến đường.", body.RootElement.GetProperty("message").GetString());
    }

    // -------------------------------------------------------------- Tham số sai

    [Fact]
    public async Task From_sau_to_tra_400_o_ca_hai_truong()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var response = await client.GetAsync(
            "/api/reports/revenue/comparison?from=2026-10-20&to=2026-10-10");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var errors = JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("errors");

        Assert.NotEqual(JsonValueKind.Undefined, errors.GetProperty("from").ValueKind);
        Assert.NotEqual(JsonValueKind.Undefined, errors.GetProperty("to").ValueKind);
    }

    [Fact]
    public async Task Khoang_qua_366_ngay_tra_400()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var response = await client.GetAsync(
            "/api/reports/revenue/comparison?from=2025-01-01&to=2026-10-17");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("366", body.RootElement.GetProperty("message").GetString()!);
    }

    // -------------------------------------------------------------- Mặc định

    [Fact]
    public async Task Bo_trong_tham_so_thi_mac_dinh_30_ngay_gan_nhat()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var expectedFrom = today.AddDays(-29);

        var body = JsonDocument.Parse(await (await client.GetAsync(
            "/api/reports/revenue/comparison")).Content.ReadAsStringAsync());

        Assert.Equal(expectedFrom.ToString("yyyy-MM-dd"), body.RootElement.GetProperty("from").GetString());
        Assert.Equal(today.ToString("yyyy-MM-dd"), body.RootElement.GetProperty("to").GetString());
        Assert.Equal(expectedFrom.AddDays(-30).ToString("yyyy-MM-dd"),
            body.RootElement.GetProperty("previousFrom").GetString());
        Assert.Equal(expectedFrom.AddDays(-1).ToString("yyyy-MM-dd"),
            body.RootElement.GetProperty("previousTo").GetString());
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
    private static async Task<Trip> SeedTripAsync(TestAppFactory factory, Guid routeId)
    {
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            BusId = Guid.NewGuid(),
            DepartureTime = Ngay(1),
        };

        await factory.SeedAsync(db => db.Trips.Add(trip));

        return trip;
    }

    private static async Task<Payment> SeedPaymentAsync(
        TestAppFactory factory,
        Guid userId,
        Guid tripId,
        decimal amount,
        PaymentStatus status = PaymentStatus.Success,
        DateTime? paidAt = null,
        DateTime? createdAt = null)
    {
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            PaymentCode = "PM-" + Guid.NewGuid().ToString("N")[..8],
            MethodCode = "MoMo",
            Amount = amount,
            Status = status,
            PaidAt = paidAt,
            CreatedAt = createdAt ?? Ngay(1),
            UserId = userId,
            TripId = tripId,
        };

        await factory.SeedAsync(db => db.Payments.Add(payment));

        return payment;
    }

    /// <summary>Mốc UTC trong quá khứ xa — `day` càng lớn càng gần hiện tại.</summary>
    private static DateTime Ngay(int day)
        => new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc).AddDays(day);
}
