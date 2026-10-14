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
/// Test tích hợp cho API báo cáo doanh thu theo ngày / tuần / tháng — GET /api/reports/revenue
/// (US 19 "Doanh thu", Sprint 4 — Trần Trung Hiếu). Hợp đồng đầy đủ ở mục
/// "Báo cáo doanh thu — /reports/revenue" của docs/api-contract.md.
///
/// Mỗi test dựng một app riêng với CSDL InMemory riêng để không lẫn dữ liệu sang nhau.
/// Helper seed chép lại trong từng lớp test, không có lớp base chung (đúng lệ đang có của dự án).
/// </summary>
public class RevenueReportApiTests
{
    // -------------------------------------------------------------- Quyền và xác thực

    [Fact]
    public async Task Khong_gui_token_thi_tra_401()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/reports/revenue")).StatusCode);
    }

    [Fact]
    public async Task Vai_tro_khach_hang_tra_403()
    {
        using var factory = new TestAppFactory();
        var passenger = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Hành Khách Test");
        var client = ClientWith(factory, factory.CreateTokenFor(passenger));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/reports/revenue")).StatusCode);
    }

    [Fact]
    public async Task Quan_ly_xem_duoc_bao_cao()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/reports/revenue")).StatusCode);
    }

    // -------------------------------------------------------------- Trục thời gian (giờ Việt Nam)

    [Fact]
    public async Task Giao_dich_tinh_vao_ngay_gio_Viet_Nam_khong_phai_ngay_UTC()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var trip = await SeedTripAsync(factory, route.Id);

        // Ba mốc quanh biên 00:00 giờ Việt Nam (= 17:00 UTC ngày hôm trước):
        // 16:59:59Z → 23:59:59 ngày 14/10 VN · 17:00:00Z → 00:00 ngày 15/10 VN · 18:30Z → 01:30 15/10 VN.
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 10_000m, paidAt: new DateTime(2026, 10, 14, 16, 59, 59, DateTimeKind.Utc));
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 20_000m, paidAt: new DateTime(2026, 10, 14, 17, 0, 0, DateTimeKind.Utc));
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 30_000m, paidAt: new DateTime(2026, 10,14, 18, 30, 0, DateTimeKind.Utc));

        var body = JsonDocument.Parse(await (await client.GetAsync(
            "/api/reports/revenue?granularity=day&from=2026-10-14&to=2026-10-15")).Content.ReadAsStringAsync());

        var buckets = body.RootElement.GetProperty("buckets").EnumerateArray().ToArray();
        Assert.Equal(2, buckets.Length);

        // Giao dịch 23:59:59 giờ VN vẫn thuộc ngày 14 — chỉ 00:00 trở đi mới sang ngày 15.
        Assert.Equal("2026-10-14", buckets[0].GetProperty("periodStart").GetString());
        Assert.Equal(10_000m, buckets[0].GetProperty("revenue").GetDecimal());
        Assert.Equal(1, buckets[0].GetProperty("payments").GetInt32());

        Assert.Equal("2026-10-15", buckets[1].GetProperty("periodStart").GetString());
        Assert.Equal(50_000m, buckets[1].GetProperty("revenue").GetDecimal());
        Assert.Equal(2, buckets[1].GetProperty("payments").GetInt32());
    }

    [Fact]
    public async Task Tuan_bat_dau_thu_hai()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var trip = await SeedTripAsync(factory, route.Id);

        // 12/10/2026 là thứ Hai, 18/10 là Chủ nhật — hai giao dịch đầu–cuối tuần phải chung một
        // bucket mang nhãn thứ Hai.
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 10_000m, paidAt: new DateTime(2026, 10, 12, 1, 0, 0, DateTimeKind.Utc));
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 20_000m, paidAt: new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc));

        var body = JsonDocument.Parse(await (await client.GetAsync(
            "/api/reports/revenue?granularity=week&from=2026-10-12&to=2026-10-18")).Content.ReadAsStringAsync());

        var bucket = Assert.Single(body.RootElement.GetProperty("buckets").EnumerateArray());

        Assert.Equal("2026-10-12", bucket.GetProperty("periodStart").GetString());
        Assert.Equal(30_000m, bucket.GetProperty("revenue").GetDecimal());
        Assert.Equal(2, bucket.GetProperty("payments").GetInt32());
    }

    [Fact]
    public async Task Thang_gop_theo_lich_va_bucket_dau_co_the_truoc_from()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var trip = await SeedTripAsync(factory, route.Id);

        // 31/01 thuộc tháng 1, 01/02 thuộc tháng 2 — nhãn bucket là ngày 1 mỗi tháng.
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 10_000m, paidAt: new DateTime(2026, 1, 31, 12, 0, 0, DateTimeKind.Utc));
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 20_000m, paidAt: new DateTime(2026, 2, 1, 1, 0, 0, DateTimeKind.Utc));

        var body = JsonDocument.Parse(await (await client.GetAsync(
            "/api/reports/revenue?granularity=month&from=2026-01-30&to=2026-02-03")).Content.ReadAsStringAsync());

        var buckets = body.RootElement.GetProperty("buckets").EnumerateArray().ToArray();

        // Bucket đầu bắt đầu 01/01 — TRƯỚC from — để khối tháng vẽ trọn vẹn trên biểu đồ.
        Assert.Equal(2, buckets.Length);
        Assert.Equal("2026-01-01", buckets[0].GetProperty("periodStart").GetString());
        Assert.Equal(10_000m, buckets[0].GetProperty("revenue").GetDecimal());
        Assert.Equal("2026-02-01", buckets[1].GetProperty("periodStart").GetString());
        Assert.Equal(20_000m, buckets[1].GetProperty("revenue").GetDecimal());
    }

    // -------------------------------------------------------------- Nền số liệu

    [Fact]
    public async Task Chi_tinh_giao_dich_Success()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var trip = await SeedTripAsync(factory, route.Id);

        await SeedPaymentAsync(factory, manager.Id, trip.Id, 10_000m, status: PaymentStatus.Success, paidAt: new DateTime(2026, 10, 14, 1, 0, 0, DateTimeKind.Utc));
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 99_000m, status: PaymentStatus.Pending, createdAt: new DateTime(2026, 10, 14, 1, 0, 0, DateTimeKind.Utc));
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 88_000m, status: PaymentStatus.Failed, createdAt: new DateTime(2026, 10, 14, 1, 0, 0, DateTimeKind.Utc));

        var body = JsonDocument.Parse(await (await client.GetAsync(
            "/api/reports/revenue?granularity=day&from=2026-10-14&to=2026-10-14")).Content.ReadAsStringAsync());

        // Pending/Failed không phải tiền đã thu — không vào báo cáo.
        Assert.Equal(10_000m, body.RootElement.GetProperty("totalRevenue").GetDecimal());
        Assert.Equal(1, body.RootElement.GetProperty("totalPayments").GetInt32());
    }

    [Fact]
    public async Task Giao_dich_Success_thieu_paidAt_tinh_theo_createdAt()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var trip = await SeedTripAsync(factory, route.Id);

        // Dữ liệu cũ/lỗi: Success mà chưa ghi PaidAt — tiền đã thu không được biến mất khỏi báo
        // cáo, mốc thời gian lấy CreatedAt.
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 15_000m, status: PaymentStatus.Success, paidAt: null, createdAt: new DateTime(2026, 10, 14, 3, 0, 0, DateTimeKind.Utc));

        var body = JsonDocument.Parse(await (await client.GetAsync(
            "/api/reports/revenue?granularity=day&from=2026-10-14&to=2026-10-14")).Content.ReadAsStringAsync());

        Assert.Equal(15_000m, body.RootElement.GetProperty("totalRevenue").GetDecimal());
        Assert.Equal(1, body.RootElement.GetProperty("totalPayments").GetInt32());
    }

    // -------------------------------------------------------------- Hình dạng dãy bucket

    [Fact]
    public async Task Bucket_lien_tuc_ngay_rong_van_hien_dien_voi_so_0()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var trip = await SeedTripAsync(factory, route.Id);

        await SeedPaymentAsync(factory, manager.Id, trip.Id, 10_000m, paidAt: new DateTime(2026, 10, 12, 1, 0, 0, DateTimeKind.Utc));

        var body = JsonDocument.Parse(await (await client.GetAsync(
            "/api/reports/revenue?granularity=day&from=2026-10-12&to=2026-10-14")).Content.ReadAsStringAsync());

        var buckets = body.RootElement.GetProperty("buckets").EnumerateArray().ToArray();

        // Trục thời gian đầy đủ: hai ngày không giao dịch vẫn là hai cột 0, không bị bỏ.
        Assert.Equal(3, buckets.Length);
        Assert.Equal(10_000m, buckets[0].GetProperty("revenue").GetDecimal());
        Assert.Equal(0m, buckets[1].GetProperty("revenue").GetDecimal());
        Assert.Equal(0, buckets[1].GetProperty("payments").GetInt32());
        Assert.Equal(0m, buckets[2].GetProperty("revenue").GetDecimal());
    }

    [Fact]
    public async Task Tong_khop_voi_tong_cac_bucket()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var trip = await SeedTripAsync(factory, route.Id);

        await SeedPaymentAsync(factory, manager.Id, trip.Id, 10_000m, paidAt: new DateTime(2026, 10, 14, 1, 0, 0, DateTimeKind.Utc));
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 20_000m, paidAt: new DateTime(2026, 10, 14, 18, 30, 0, DateTimeKind.Utc));   // → 15/10 VN
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 40_000m, paidAt: new DateTime(2026, 10, 16, 2, 0, 0, DateTimeKind.Utc));

        var body = JsonDocument.Parse(await (await client.GetAsync(
            "/api/reports/revenue?granularity=day&from=2026-10-14&to=2026-10-16")).Content.ReadAsStringAsync());

        // Đẳng thức là phép kiểm THẬT: tổng truy vấn riêng trong CSDL, bucket gộp từ dòng thô —
        // phép gộp lệch (rơi dòng ở biên múi giờ) là test này đỏ ngay.
        var totalRevenue = body.RootElement.GetProperty("totalRevenue").GetDecimal();
        var totalPayments = body.RootElement.GetProperty("totalPayments").GetInt32();

        var bucketSum = body.RootElement.GetProperty("buckets").EnumerateArray()
            .Sum(b => b.GetProperty("revenue").GetDecimal());
        var bucketCount = body.RootElement.GetProperty("buckets").EnumerateArray()
            .Sum(b => b.GetProperty("payments").GetInt32());

        Assert.Equal(70_000m, totalRevenue);
        Assert.Equal(3, totalPayments);
        Assert.Equal(totalRevenue, bucketSum);
        Assert.Equal(totalPayments, bucketCount);
    }

    [Fact]
    public async Task Bo_trong_from_to_lay_30_ngay_gan_nhat()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var body = JsonDocument.Parse(await (await client.GetAsync(
            "/api/reports/revenue")).Content.ReadAsStringAsync());

        // Mặc định: 30 ngày gần nhất tính cả hôm nay (giờ VN), gộp theo ngày.
        Assert.Equal("day", body.RootElement.GetProperty("granularity").GetString());

        var from = DateOnly.Parse(body.RootElement.GetProperty("from").GetString()!);
        var to = DateOnly.Parse(body.RootElement.GetProperty("to").GetString()!);

        Assert.Equal(to.AddDays(-29), from);
        Assert.Equal(30, body.RootElement.GetProperty("buckets").GetArrayLength());

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

        var routeA = await SeedRouteAsync(factory, "19A");
        var routeB = await SeedRouteAsync(factory, "19B");
        var tripA = await SeedTripAsync(factory, routeA.Id);
        var tripB = await SeedTripAsync(factory, routeB.Id);

        await SeedPaymentAsync(factory, manager.Id, tripA.Id, 10_000m, paidAt: new DateTime(2026, 10, 14, 1, 0, 0, DateTimeKind.Utc));
        await SeedPaymentAsync(factory, manager.Id, tripB.Id, 99_000m, paidAt: new DateTime(2026, 10, 14, 2, 0, 0, DateTimeKind.Utc));

        var body = JsonDocument.Parse(await (await client.GetAsync(
            $"/api/reports/revenue?granularity=day&from=2026-10-14&to=2026-10-14&routeId={routeA.Id}")).Content.ReadAsStringAsync());

        Assert.Equal(10_000m, body.RootElement.GetProperty("totalRevenue").GetDecimal());
        Assert.Equal(1, body.RootElement.GetProperty("totalPayments").GetInt32());
    }

    [Fact]
    public async Task Loc_theo_tuyen_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var response = await client.GetAsync(
            $"/api/reports/revenue?granularity=day&from=2026-10-14&to=2026-10-14&routeId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // -------------------------------------------------------------- Dữ liệu đầu vào

    [Fact]
    public async Task Granularity_la_tra_400_theo_truong()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var response = await client.GetAsync("/api/reports/revenue?granularity=nam");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("granularity", out _));
    }

    [Fact]
    public async Task To_truoc_from_tra_400_va_loi_o_ca_hai_o_ngay()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var response = await client.GetAsync("/api/reports/revenue?from=2026-10-15&to=2026-10-14");

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
        var response = await client.GetAsync("/api/reports/revenue?from=2025-01-01&to=2026-01-02");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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
