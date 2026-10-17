using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SmartBus.Api.Entities;
using RouteEntity = SmartBus.Api.Entities.Route;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test tích hợp cho bộ nhớ đệm của <c>GET /api/reports/revenue</c>
/// (task *"Cache kết quả báo cáo theo kỳ để tăng tốc"* — Vàng Thị Dăm).
///
/// Cách chứng minh "đệm có thật sự đứng trước endpoint": gọi một lượt cho nóng đệm, rồi **ghi
/// thẳng một giao dịch mới vào CSDL** và gọi lại đúng URL. Không có đệm thì lượt sau phải thấy
/// giao dịch mới; có đệm thì vẫn thấy số cũ. Khoá KHÁC (đổi ngày kết thúc) phải đọc tươi — đó là
/// phép đối chứng cho thấy đệm không dùng chung kết quả giữa hai kỳ.
///
/// Không có ngưỡng "phổ biến" như đệm tìm chuyến, nên lượt ĐẦU đã lưu kết quả — khác với
/// <see cref="TripSearchCacheApiTests"/> phải gọi hai lượt mới làm nóng.
/// </summary>
public class RevenueReportCacheApiTests
{
    [Fact]
    public async Task Luot_goi_sau_tra_ket_qua_cu_tu_bo_nho_dem()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "43");
        var trip = await SeedTripAsync(factory, route.Id);
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 10_000m,
            paidAt: new DateTime(2026, 10, 14, 1, 0, 0, DateTimeKind.Utc));

        const string url = "/api/reports/revenue?granularity=day&from=2026-10-14&to=2026-10-14";

        // Lượt đầu: đọc CSDL (10.000) và lưu vào đệm.
        Assert.Equal(10_000m, await ReadTotalRevenueAsync(client, url));

        // Giao dịch thứ hai ghi thẳng vào CSDL — bộ đệm không được biết về nó.
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 25_000m,
            paidAt: new DateTime(2026, 10, 14, 2, 0, 0, DateTimeKind.Utc));

        // Vẫn đúng số cũ ⇒ có bộ đệm đứng trước endpoint thật.
        Assert.Equal(10_000m, await ReadTotalRevenueAsync(client, url));

        // Đối chứng: khoá khác (kéo tới 15/10) là truy vấn khác → đọc tươi, thấy đủ 35.000.
        Assert.Equal(35_000m, await ReadTotalRevenueAsync(client,
            "/api/reports/revenue?granularity=day&from=2026-10-14&to=2026-10-15"));
    }

    [Fact]
    public async Task Ket_qua_404_khong_vao_dem()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        // Tuyến CHƯA tồn tại — lỗi 404 phải trả lời tươi, không được đệm.
        var routeId = Guid.NewGuid();
        var url = $"/api/reports/revenue?from=2026-10-14&to=2026-10-14&routeId={routeId}";

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(url)).StatusCode);

        // Tạo đúng tuyến đó rồi gọi lại cùng URL: phải thấy ngay, không bị kết quả 404 cũ chặn.
        await factory.SeedAsync(db => db.Routes.Add(new RouteEntity
        {
            Id = routeId,
            Code = "43",
            Name = "Tuyến 43",
            Origin = "Bến Thành",
            Destination = "Chợ Lớn",
        }));

        var trip = await SeedTripAsync(factory, routeId);
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 10_000m,
            paidAt: new DateTime(2026, 10, 14, 1, 0, 0, DateTimeKind.Utc));

        Assert.Equal(10_000m, await ReadTotalRevenueAsync(client, url));
    }

    private static async Task<decimal> ReadTotalRevenueAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return body.RootElement.GetProperty("totalRevenue").GetDecimal();
    }

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
            DepartureTime = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc),
        };

        await factory.SeedAsync(db => db.Trips.Add(trip));

        return trip;
    }

    private static async Task<Payment> SeedPaymentAsync(
        TestAppFactory factory,
        Guid userId,
        Guid tripId,
        decimal amount,
        DateTime paidAt)
    {
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            PaymentCode = "PM-" + Guid.NewGuid().ToString("N")[..8],
            MethodCode = "MoMo",
            Amount = amount,
            Status = PaymentStatus.Success,
            PaidAt = paidAt,
            CreatedAt = paidAt,
            UserId = userId,
            TripId = tripId,
        };

        await factory.SeedAsync(db => db.Payments.Add(payment));

        return payment;
    }
}
