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
/// Test tích hợp cho ba báo cáo doanh thu phân rã — GET /api/reports/revenue/by-route,
/// /by-bus, /by-payment-method (US 19 "Doanh thu", Sprint 4 — Nguyễn Duy Kiên). Hợp đồng đầy đủ
/// ở mục "Báo cáo doanh thu — /reports/revenue" của docs/api-contract.md.
///
/// Trọng tâm: cùng nền Payments Success + cùng trục giờ Việt Nam với báo cáo thời gian (có test
/// đối chiếu tổng trực tiếp với GET /reports/revenue), gộp đúng theo tuyến/xe/phương thức, và
/// đẳng thức "tổng == cộng các dòng" trong từng phản hồi.
///
/// Mỗi test dựng một app riêng với CSDL InMemory riêng; helper seed chép lại trong lớp test,
/// không có lớp base chung (đúng lệ đang có của dự án).
/// </summary>
public class RevenueBreakdownApiTests
{
    private const string ByRoute = "/api/reports/revenue/by-route";
    private const string ByBus = "/api/reports/revenue/by-bus";
    private const string ByPaymentMethod = "/api/reports/revenue/by-payment-method";

    // -------------------------------------------------------------- Quyền và xác thực

    [Fact]
    public async Task Khong_gui_token_thi_tra_401()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(ByRoute)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(ByBus)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(ByPaymentMethod)).StatusCode);
    }

    [Fact]
    public async Task Vai_tro_khach_hang_tra_403()
    {
        using var factory = new TestAppFactory();
        var passenger = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Hành Khách Test");
        var client = ClientWith(factory, factory.CreateTokenFor(passenger));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(ByRoute)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(ByBus)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(ByPaymentMethod)).StatusCode);
    }

    [Fact]
    public async Task Quan_ly_xem_duoc_ca_ba_bao_cao()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(ByRoute)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(ByBus)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(ByPaymentMethod)).StatusCode);
    }

    // -------------------------------------------------------------- Theo tuyến

    [Fact]
    public async Task Theo_tuyen_gop_cac_chuyen_cung_tuyen_va_tra_ten_tuyen()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var routeA = await SeedRouteAsync(factory, "19", "Bến Thành — Suối Tiên");
        var routeB = await SeedRouteAsync(factory, "01", "Bến Thành — Chợ Lớn");
        var bus = await SeedBusAsync(factory, "29B-123.45");

        var tripA1 = await SeedTripAsync(factory, routeA.Id, bus.Id);
        var tripA2 = await SeedTripAsync(factory, routeA.Id, bus.Id);
        var tripB = await SeedTripAsync(factory, routeB.Id, bus.Id);

        await SeedPaymentAsync(factory, manager.Id, tripA1.Id, 10_000m, methodCode: "MoMo");
        await SeedPaymentAsync(factory, manager.Id, tripA2.Id, 30_000m, methodCode: "MoMo");
        await SeedPaymentAsync(factory, manager.Id, tripB.Id, 5_000m, methodCode: "VNPay");

        var body = JsonDocument.Parse(await (await client.GetAsync(
            $"{ByRoute}?from=2026-10-14&to=2026-10-14")).Content.ReadAsStringAsync());

        var routes = body.RootElement.GetProperty("routes").EnumerateArray().ToArray();

        Assert.Equal(2, routes.Length);
        Assert.Equal(45_000m, body.RootElement.GetProperty("totalRevenue").GetDecimal());
        Assert.Equal(3, body.RootElement.GetProperty("totalPayments").GetInt32());

        // Thứ tự cố định: doanh thu giảm dần — tuyến A (40k) trước tuyến B (5k).
        Assert.Equal(routeA.Id, routes[0].GetProperty("routeId").GetGuid());
        Assert.Equal("19", routes[0].GetProperty("routeCode").GetString());
        Assert.Equal("Bến Thành — Suối Tiên", routes[0].GetProperty("routeName").GetString());
        Assert.Equal(40_000m, routes[0].GetProperty("revenue").GetDecimal());
        Assert.Equal(2, routes[0].GetProperty("payments").GetInt32());

        Assert.Equal(routeB.Id, routes[1].GetProperty("routeId").GetGuid());
        Assert.Equal(5_000m, routes[1].GetProperty("revenue").GetDecimal());
        Assert.Equal(1, routes[1].GetProperty("payments").GetInt32());
    }

    [Fact]
    public async Task Theo_tuyen_hoa_doanh_thu_thi_sap_theo_ma_tuyen()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        // Cùng doanh thu → mã tuyến tăng dần (Ordinal): "01" trước "B10".
        var routeB10 = await SeedRouteAsync(factory, "B10");
        var route01 = await SeedRouteAsync(factory, "01");
        var bus = await SeedBusAsync(factory, "29B-123.45");

        var tripB10 = await SeedTripAsync(factory, routeB10.Id, bus.Id);
        var trip01 = await SeedTripAsync(factory, route01.Id, bus.Id);

        await SeedPaymentAsync(factory, manager.Id, tripB10.Id, 10_000m);
        await SeedPaymentAsync(factory, manager.Id, trip01.Id, 10_000m);

        var body = JsonDocument.Parse(await (await client.GetAsync(
            $"{ByRoute}?from=2026-10-14&to=2026-10-14")).Content.ReadAsStringAsync());

        var routes = body.RootElement.GetProperty("routes").EnumerateArray().ToArray();

        Assert.Equal("01", routes[0].GetProperty("routeCode").GetString());
        Assert.Equal("B10", routes[1].GetProperty("routeCode").GetString());
    }

    [Fact]
    public async Task Theo_tuyen_chi_tra_tuyen_co_giao_dich_thanh_cong()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var coDoanhThu = await SeedRouteAsync(factory, "19");
        var khongBanDuoc = await SeedRouteAsync(factory, "99");
        var bus = await SeedBusAsync(factory, "29B-123.45");

        var tripCo = await SeedTripAsync(factory, coDoanhThu.Id, bus.Id);
        var tripKhong = await SeedTripAsync(factory, khongBanDuoc.Id, bus.Id);

        await SeedPaymentAsync(factory, manager.Id, tripCo.Id, 10_000m);
        await SeedPaymentAsync(factory, manager.Id, tripKhong.Id, 99_000m, status: PaymentStatus.Pending);

        var body = JsonDocument.Parse(await (await client.GetAsync(
            $"{ByRoute}?from=2026-10-14&to=2026-10-14")).Content.ReadAsStringAsync());

        var route = Assert.Single(body.RootElement.GetProperty("routes").EnumerateArray());
        Assert.Equal(coDoanhThu.Id, route.GetProperty("routeId").GetGuid());
        Assert.Equal(10_000m, route.GetProperty("revenue").GetDecimal());
    }

    // -------------------------------------------------------------- Theo xe

    [Fact]
    public async Task Theo_xe_gop_doanh_thu_cac_chuyen_do_xe_do_chay_va_tra_bien_so()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var routeA = await SeedRouteAsync(factory, "19");
        var routeB = await SeedRouteAsync(factory, "01");
        var xe45Cho = await SeedBusAsync(factory, "29B-123.45", "Xe buýt 45 chỗ");
        var xeDien = await SeedBusAsync(factory, "51C-678.90", "Xe buýt điện");

        var tripXe45A = await SeedTripAsync(factory, routeA.Id, xe45Cho.Id);
        var tripXe45B = await SeedTripAsync(factory, routeB.Id, xe45Cho.Id);
        var tripXeDien = await SeedTripAsync(factory, routeA.Id, xeDien.Id);

        await SeedPaymentAsync(factory, manager.Id, tripXe45A.Id, 10_000m);
        await SeedPaymentAsync(factory, manager.Id, tripXe45B.Id, 5_000m);
        await SeedPaymentAsync(factory, manager.Id, tripXeDien.Id, 30_000m);

        var body = JsonDocument.Parse(await (await client.GetAsync(
            $"{ByBus}?from=2026-10-14&to=2026-10-14")).Content.ReadAsStringAsync());

        var buses = body.RootElement.GetProperty("buses").EnumerateArray().ToArray();

        Assert.Equal(2, buses.Length);
        Assert.Equal(45_000m, body.RootElement.GetProperty("totalRevenue").GetDecimal());
        Assert.Equal(3, body.RootElement.GetProperty("totalPayments").GetInt32());

        // Xe điện 30k đứng trước xe 45 chỗ 15k (doanh thu giảm dần); xe 45 chỗ gộp hai tuyến.
        Assert.Equal(xeDien.Id, buses[0].GetProperty("busId").GetGuid());
        Assert.Equal("51C-678.90", buses[0].GetProperty("licensePlate").GetString());
        Assert.Equal("Xe buýt điện", buses[0].GetProperty("busType").GetString());
        Assert.Equal(30_000m, buses[0].GetProperty("revenue").GetDecimal());

        Assert.Equal(xe45Cho.Id, buses[1].GetProperty("busId").GetGuid());
        Assert.Equal("29B-123.45", buses[1].GetProperty("licensePlate").GetString());
        Assert.Equal(15_000m, buses[1].GetProperty("revenue").GetDecimal());
        Assert.Equal(2, buses[1].GetProperty("payments").GetInt32());
    }

    // -------------------------------------------------------------- Theo phương thức thanh toán

    [Fact]
    public async Task Theo_phuong_thuc_gop_theo_method_code()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var bus = await SeedBusAsync(factory, "29B-123.45");
        var trip = await SeedTripAsync(factory, route.Id, bus.Id);

        await SeedPaymentAsync(factory, manager.Id, trip.Id, 10_000m, methodCode: "MoMo");
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 20_000m, methodCode: "MoMo");
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 40_000m, methodCode: "VNPay");
        // Success nhưng thiếu PaidAt: mốc thời gian là CreatedAt — vẫn phải vào báo cáo.
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 7_000m, methodCode: "ZaloPay",
            coPaidAt: false, createdAt: new DateTime(2026, 10, 14, 3, 0, 0, DateTimeKind.Utc));
        // Phương thức có giao dịch nhưng chưa thu được tiền — không vào báo cáo.
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 99_000m, methodCode: "BankCard",
            status: PaymentStatus.Failed);

        var body = JsonDocument.Parse(await (await client.GetAsync(
            $"{ByPaymentMethod}?from=2026-10-14&to=2026-10-14")).Content.ReadAsStringAsync());

        var methods = body.RootElement.GetProperty("paymentMethods").EnumerateArray().ToArray();

        // Chỉ ba phương thức có giao dịch Success — BankCard không hiện thành dòng 0.
        Assert.Equal(3, methods.Length);
        Assert.Equal(77_000m, body.RootElement.GetProperty("totalRevenue").GetDecimal());
        Assert.Equal(4, body.RootElement.GetProperty("totalPayments").GetInt32());

        Assert.Equal("VNPay", methods[0].GetProperty("methodCode").GetString());
        Assert.Equal(40_000m, methods[0].GetProperty("revenue").GetDecimal());

        Assert.Equal("MoMo", methods[1].GetProperty("methodCode").GetString());
        Assert.Equal(30_000m, methods[1].GetProperty("revenue").GetDecimal());
        Assert.Equal(2, methods[1].GetProperty("payments").GetInt32());

        Assert.Equal("ZaloPay", methods[2].GetProperty("methodCode").GetString());
        Assert.Equal(7_000m, methods[2].GetProperty("revenue").GetDecimal());
    }

    // -------------------------------------------------------------- Đẳng thức tổng

    [Fact]
    public async Task Tong_cua_moi_bao_cao_bang_cong_cac_dong()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var routeA = await SeedRouteAsync(factory, "19");
        var routeB = await SeedRouteAsync(factory, "01");
        var bus1 = await SeedBusAsync(factory, "29B-123.45");
        var bus2 = await SeedBusAsync(factory, "51C-678.90");

        var tripA1 = await SeedTripAsync(factory, routeA.Id, bus1.Id);
        var tripA2 = await SeedTripAsync(factory, routeA.Id, bus2.Id);
        var tripB = await SeedTripAsync(factory, routeB.Id, bus1.Id);

        await SeedPaymentAsync(factory, manager.Id, tripA1.Id, 10_000m, methodCode: "MoMo");
        await SeedPaymentAsync(factory, manager.Id, tripA2.Id, 20_000m, methodCode: "VNPay");
        await SeedPaymentAsync(factory, manager.Id, tripB.Id, 40_000m, methodCode: "BankCard");

        foreach (var (url, listName) in new[] { (ByRoute, "routes"), (ByBus, "buses"), (ByPaymentMethod, "paymentMethods") })
        {
            var body = JsonDocument.Parse(await (await client.GetAsync(
                $"{url}?from=2026-10-14&to=2026-10-14")).Content.ReadAsStringAsync());

            // Tổng tính bằng truy vấn gộp riêng trong CSDL, dòng gộp từ dữ liệu thô — lệch là
            // test này đỏ ngay, không tự che mất (cùng lý do chốt 3 của RevenueReportService).
            var totalRevenue = body.RootElement.GetProperty("totalRevenue").GetDecimal();
            var totalPayments = body.RootElement.GetProperty("totalPayments").GetInt32();
            var rows = body.RootElement.GetProperty(listName).EnumerateArray().ToArray();

            Assert.Equal(70_000m, totalRevenue);
            Assert.Equal(3, totalPayments);
            Assert.Equal(totalRevenue, rows.Sum(r => r.GetProperty("revenue").GetDecimal()));
            Assert.Equal(totalPayments, rows.Sum(r => r.GetProperty("payments").GetInt32()));
        }
    }

    [Fact]
    public async Task Tong_khop_voi_bao_cao_doanh_thu_theo_thoi_gian_cung_khoang()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var bus = await SeedBusAsync(factory, "29B-123.45");
        var trip = await SeedTripAsync(factory, route.Id, bus.Id);

        await SeedPaymentAsync(factory, manager.Id, trip.Id, 10_000m);
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 20_000m);
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 30_000m);

        var timeReport = JsonDocument.Parse(await (await client.GetAsync(
            "/api/reports/revenue?from=2026-10-14&to=2026-10-14")).Content.ReadAsStringAsync());

        using var byRouteBody = JsonDocument.Parse(await (await client.GetAsync(
            $"{ByRoute}?from=2026-10-14&to=2026-10-14")).Content.ReadAsStringAsync());

        // Hai endpoint dùng chung định nghĩa doanh thu; cùng dữ liệu thì cùng con số tổng.
        Assert.Equal(
            timeReport.RootElement.GetProperty("totalRevenue").GetDecimal(),
            byRouteBody.RootElement.GetProperty("totalRevenue").GetDecimal());
        Assert.Equal(
            timeReport.RootElement.GetProperty("totalPayments").GetInt32(),
            byRouteBody.RootElement.GetProperty("totalPayments").GetInt32());
    }

    // -------------------------------------------------------------- Trục thời gian giờ Việt Nam

    [Fact]
    public async Task Bien_ngay_theo_gio_Viet_Nam_chu_khong_phai_UTC()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var bus = await SeedBusAsync(factory, "29B-123.45");
        var trip = await SeedTripAsync(factory, route.Id, bus.Id);

        // Ngày báo cáo duy nhất: 15/10/2026 (giờ VN) = [14/10 17:00Z, 15/10 17:00Z).
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 10_000m,
            paidAt: new DateTime(2026, 10, 14, 16, 59, 59, DateTimeKind.Utc));   // 23:59:59 ngày 14 VN — ngoài
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 20_000m,
            paidAt: new DateTime(2026, 10, 14, 17, 0, 0, DateTimeKind.Utc));    // 00:00 ngày 15 VN — trong
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 5_000m,
            paidAt: new DateTime(2026, 10, 15, 16, 59, 59, DateTimeKind.Utc));  // 23:59:59 ngày 15 VN — trong
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 99_000m,
            paidAt: new DateTime(2026, 10, 15, 17, 0, 0, DateTimeKind.Utc));    // 00:00 ngày 16 VN — ngoài

        var body = JsonDocument.Parse(await (await client.GetAsync(
            $"{ByRoute}?from=2026-10-15&to=2026-10-15")).Content.ReadAsStringAsync());

        Assert.Equal(25_000m, body.RootElement.GetProperty("totalRevenue").GetDecimal());
        Assert.Equal(2, body.RootElement.GetProperty("totalPayments").GetInt32());
    }

    [Fact]
    public async Task Bo_trong_from_to_lay_30_ngay_gan_nhat()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var body = JsonDocument.Parse(await (await client.GetAsync(ByBus)).Content.ReadAsStringAsync());

        var from = DateOnly.Parse(body.RootElement.GetProperty("from").GetString()!);
        var to = DateOnly.Parse(body.RootElement.GetProperty("to").GetString()!);

        Assert.Equal(to.AddDays(-29), from);

        var todayVietnam = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        Assert.Equal(todayVietnam, to);
    }

    // -------------------------------------------------------------- Dữ liệu đầu vào

    [Fact]
    public async Task To_truoc_from_tra_400_va_loi_o_ca_hai_o_ngay()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        foreach (var url in new[] { ByRoute, ByBus, ByPaymentMethod })
        {
            var response = await client.GetAsync($"{url}?from=2026-10-15&to=2026-10-14");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var errors = body.RootElement.GetProperty("errors");
            Assert.True(errors.TryGetProperty("from", out _));
            Assert.True(errors.TryGetProperty("to", out _));
        }
    }

    [Fact]
    public async Task Khoang_qua_366_ngay_tra_400()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        // 367 ngày — vượt trần 366 đúng một ngày (2025-01-01 → 2026-01-02).
        var response = await client.GetAsync($"{ByRoute}?from=2025-01-01&to=2026-01-02");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Ky_khong_co_giao_dich_tra_mang_rong_va_tong_0()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        foreach (var (url, listName) in new[] { (ByRoute, "routes"), (ByBus, "buses"), (ByPaymentMethod, "paymentMethods") })
        {
            var body = JsonDocument.Parse(await (await client.GetAsync(
                $"{url}?from=2026-10-01&to=2026-10-02")).Content.ReadAsStringAsync());

            Assert.Equal(0m, body.RootElement.GetProperty("totalRevenue").GetDecimal());
            Assert.Equal(0, body.RootElement.GetProperty("totalPayments").GetInt32());
            Assert.Empty(body.RootElement.GetProperty(listName).EnumerateArray());
        }
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

    private static async Task<RouteEntity> SeedRouteAsync(
        TestAppFactory factory,
        string code,
        string? name = null)
    {
        var route = new RouteEntity
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name ?? $"Tuyến {code}",
            Origin = "Bến Thành",
            Destination = "Chợ Lớn",
        };

        await factory.SeedAsync(db => db.Routes.Add(route));

        return route;
    }

    private static async Task<Bus> SeedBusAsync(
        TestAppFactory factory,
        string licensePlate,
        string busType = "Xe buýt 45 chỗ")
    {
        var bus = new Bus
        {
            Id = Guid.NewGuid(),
            LicensePlate = licensePlate,
            BusType = busType,
            Capacity = 45,
        };

        await factory.SeedAsync(db => db.Buses.Add(bus));

        return bus;
    }

    /// <summary>Chuyến chỉ có khoá ngoại, không gán navigation (đúng lệ "seed dòng mồ côi").</summary>
    private static async Task<Trip> SeedTripAsync(TestAppFactory factory, Guid routeId, Guid busId)
    {
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            BusId = busId,
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
        string methodCode = "MoMo",
        DateTime? paidAt = null,
        DateTime? createdAt = null,
        bool coPaidAt = true)
    {
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            PaymentCode = "PM-" + Guid.NewGuid().ToString("N")[..8],
            MethodCode = methodCode,
            Amount = amount,
            Status = status,
            // coPaidAt=false mô phỏng dữ liệu cũ/lỗi: Success mà chưa ghi PaidAt.
            PaidAt = coPaidAt ? paidAt ?? new DateTime(2026, 10, 14, 1, 0, 0, DateTimeKind.Utc) : null,
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
