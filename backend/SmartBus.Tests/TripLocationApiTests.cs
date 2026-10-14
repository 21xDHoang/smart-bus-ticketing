using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;
using RouteEntity = SmartBus.Api.Entities.Route;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test tích hợp cho API cập nhật vị trí chuyến — PUT /api/trips/{id}/location
/// (US 10 "Thông báo trạm", Sprint 4 — Trần Trung Hiếu). Hợp đồng đầy đủ ở mục
/// "PUT /trips/{id}/location" của docs/api-contract.md.
///
/// Mỗi test dựng một app riêng với CSDL InMemory riêng để không lẫn dữ liệu sang nhau.
/// Helper seed chép lại trong từng lớp test, không có lớp base chung (đúng lệ đang có của dự án).
/// </summary>
public class TripLocationApiTests
{
    // -------------------------------------------------------------- Quyền và xác thực

    [Fact]
    public async Task Khong_gui_token_thi_tra_401()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/trips/{Guid.NewGuid()}/location",
            new { currentLat = 21.0285, currentLng = 105.8542 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Vai_tro_khach_hang_tra_403()
    {
        using var factory = new TestAppFactory();
        var passenger = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Hành Khách Test");
        var client = ClientWith(factory, factory.CreateTokenFor(passenger));

        var response = await client.PutAsJsonAsync(
            $"/api/trips/{Guid.NewGuid()}/location",
            new { currentLat = 21.0285, currentLng = 105.8542 });

        // [Authorize(Roles = Driver)] — vai trò không phải Tài xế bị middleware chặn trước khi
        // vào service, kể cả khi chuyến tồn tại.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Tai_xe_khac_tra_404_khong_phai_403()
    {
        using var factory = new TestAppFactory();
        var taiXeA = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế A");
        var taiXeB = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế B");
        var client = ClientWith(factory, factory.CreateTokenFor(taiXeB));

        var route = await SeedRouteAsync(factory, "10");
        var trip = await SeedTripAsync(factory, route.Id, driverId: taiXeA.Id);

        var response = await client.PutAsJsonAsync(
            $"/api/trips/{trip.Id}/location",
            new { currentLat = 21.0285, currentLng = 105.8542 });

        // 404 chứ không phải 403: một câu cho "không tồn tại" và "không phải chuyến của tài xế
        // này" — không để lộ chuyến nào đang có tài xế khác điều khiển (cùng lối vé của người
        // khác trả 404).
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Chuyen_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var driver = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế Test");
        var client = ClientWith(factory, factory.CreateTokenFor(driver));

        var response = await client.PutAsJsonAsync(
            $"/api/trips/{Guid.NewGuid()}/location",
            new { currentLat = 21.0285, currentLng = 105.8542 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // -------------------------------------------------------------- Cập nhật thành công

    [Fact]
    public async Task Bao_vi_tri_thanh_cong_tra_200_va_luu_du_bo_ba()
    {
        using var factory = new TestAppFactory();
        var driver = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế Test");
        var client = ClientWith(factory, factory.CreateTokenFor(driver));

        var route = await SeedRouteAsync(factory, "10");
        var trip = await SeedTripAsync(factory, route.Id, driver.Id);
        var tram = await SeedStopAsync(factory, "Bến xe Yên Nghĩa");
        await SeedRouteStopAsync(factory, route.Id, tram.Id, 1);

        var before = DateTime.UtcNow;

        var response = await client.PutAsJsonAsync(
            $"/api/trips/{trip.Id}/location",
            new { currentLat = 21.0285, currentLng = 105.8542, currentStopId = tram.Id });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Ảnh chụp vị trí VỪA LƯU — khớp đúng bảng trường của hợp đồng.
        Assert.Equal(trip.Id, body.RootElement.GetProperty("tripId").GetGuid());
        Assert.Equal(21.0285, body.RootElement.GetProperty("currentLat").GetDouble());
        Assert.Equal(105.8542, body.RootElement.GetProperty("currentLng").GetDouble());
        Assert.Equal(tram.Id, body.RootElement.GetProperty("currentStopId").GetGuid());

        // positionUpdatedAt là giờ server nhận request — không sớm hơn trước khi gọi.
        var positionUpdatedAt = body.RootElement.GetProperty("positionUpdatedAt").GetDateTime();
        Assert.True(positionUpdatedAt >= before.AddSeconds(-1));

        // Dữ liệu thật sự nằm trong CSDL.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.Trips.SingleAsync(t => t.Id == trip.Id);

        Assert.Equal(21.0285, saved.CurrentLat!.Value);
        Assert.Equal(105.8542, saved.CurrentLng!.Value);
        Assert.Equal(tram.Id, saved.CurrentStopId);
        Assert.NotNull(saved.PositionUpdatedAt);
        Assert.NotNull(saved.UpdatedAt);
    }

    [Fact]
    public async Task Bao_vi_tri_moi_thay_the_vi_tri_cu()
    {
        using var factory = new TestAppFactory();
        var driver = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế Test");
        var client = ClientWith(factory, factory.CreateTokenFor(driver));

        var route = await SeedRouteAsync(factory, "10");
        var trip = await SeedTripAsync(factory, route.Id, driver.Id);
        var tram = await SeedStopAsync(factory, "Bến xe Yên Nghĩa");
        await SeedRouteStopAsync(factory, route.Id, tram.Id, 1);

        await client.PutAsJsonAsync(
            $"/api/trips/{trip.Id}/location",
            new { currentLat = 21.0285, currentLng = 105.8542, currentStopId = tram.Id });

        var response = await client.PutAsJsonAsync(
            $"/api/trips/{trip.Id}/location",
            new { currentLat = 21.5000, currentLng = 105.5000, currentStopId = tram.Id });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(21.5000, body.RootElement.GetProperty("currentLat").GetDouble());
        Assert.Equal(105.5000, body.RootElement.GetProperty("currentLng").GetDouble());
    }

    [Fact]
    public async Task Bao_vi_tri_khong_kem_tram_giu_nguyen_tram_cu()
    {
        using var factory = new TestAppFactory();
        var driver = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế Test");
        var client = ClientWith(factory, factory.CreateTokenFor(driver));

        var route = await SeedRouteAsync(factory, "10");
        var tram = await SeedStopAsync(factory, "Bến xe Yên Nghĩa");
        await SeedRouteStopAsync(factory, route.Id, tram.Id, 1);
        var trip = await SeedTripAsync(factory, route.Id, driver.Id, currentStopId: tram.Id);

        // Thiết bị không xác định được trạm — bỏ trống currentStopId: chỉ đổi toạ độ, trạm giữ
        // nguyên (nếu đặt null thì phép "cách trạm kế tiếp bao xa" của service thông báo mất neo).
        var response = await client.PutAsJsonAsync(
            $"/api/trips/{trip.Id}/location",
            new { currentLat = 21.5000, currentLng = 105.5000 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(tram.Id, body.RootElement.GetProperty("currentStopId").GetGuid());
    }

    [Fact]
    public async Task Chuyen_Scheduled_van_bao_duoc_vi_tri()
    {
        using var factory = new TestAppFactory();
        var driver = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế Test");
        var client = ClientWith(factory, factory.CreateTokenFor(driver));

        var route = await SeedRouteAsync(factory, "10");
        var trip = await SeedTripAsync(factory, route.Id, driver.Id, status: TripStatus.Scheduled);

        // Cố ý nhận cả Scheduled: chưa có endpoint "bắt đầu chuyến" nào đổi trạng thái sang
        // Running — chặn Scheduled thì thiết bị không báo được vị trí trước giờ chạy.
        var response = await client.PutAsJsonAsync(
            $"/api/trips/{trip.Id}/location",
            new { currentLat = 21.0285, currentLng = 105.8542 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(TripStatus.Completed)]
    [InlineData(TripStatus.Cancelled)]
    public async Task Chuyen_da_hoan_thanh_hoac_da_huy_tra_409(TripStatus status)
    {
        using var factory = new TestAppFactory();
        var driver = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế Test");
        var client = ClientWith(factory, factory.CreateTokenFor(driver));

        var route = await SeedRouteAsync(factory, "10");
        var trip = await SeedTripAsync(factory, route.Id, driver.Id, status: status);

        var response = await client.PutAsJsonAsync(
            $"/api/trips/{trip.Id}/location",
            new { currentLat = 21.0285, currentLng = 105.8542 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // -------------------------------------------------------------- Trạm đi kèm

    [Fact]
    public async Task Tram_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var driver = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế Test");
        var client = ClientWith(factory, factory.CreateTokenFor(driver));

        var route = await SeedRouteAsync(factory, "10");
        var trip = await SeedTripAsync(factory, route.Id, driver.Id);

        var response = await client.PutAsJsonAsync(
            $"/api/trips/{trip.Id}/location",
            new { currentLat = 21.0285, currentLng = 105.8542, currentStopId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Tram_khong_thuoc_tuyen_tra_400_va_loi_theo_truong()
    {
        using var factory = new TestAppFactory();
        var driver = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế Test");
        var client = ClientWith(factory, factory.CreateTokenFor(driver));

        var route = await SeedRouteAsync(factory, "10");
        var trip = await SeedTripAsync(factory, route.Id, driver.Id);

        // Trạm TỒN TẠI nhưng không nằm trong RouteStops của tuyến — trạm lạ sẽ làm hỏng phép
        // "cách trạm kế tiếp bao xa" của service thông báo, chặn ngay từ nguồn.
        var tramLac = await SeedStopAsync(factory, "Trạm của tuyến khác");

        var response = await client.PutAsJsonAsync(
            $"/api/trips/{trip.Id}/location",
            new { currentLat = 21.0285, currentLng = 105.8542, currentStopId = tramLac.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = body.RootElement.GetProperty("errors");
        Assert.Contains("Trạm không thuộc tuyến của chuyến.",
            errors.GetProperty("currentStopId").EnumerateArray().Select(e => e.GetString()));
    }

    // -------------------------------------------------------------- Dữ liệu đầu vào

    [Theory]
    [InlineData(91)]
    [InlineData(-91)]
    [InlineData(181)]
    [InlineData(-181)]
    public async Task Toa_do_sai_khoang_tra_400(double value)
    {
        using var factory = new TestAppFactory();
        var driver = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế Test");
        var client = ClientWith(factory, factory.CreateTokenFor(driver));

        var route = await SeedRouteAsync(factory, "10");
        var trip = await SeedTripAsync(factory, route.Id, driver.Id);

        var isLat = value is > 90 or < -90;

        var response = await client.PutAsJsonAsync(
            $"/api/trips/{trip.Id}/location",
            new
            {
                currentLat = isLat ? value : 21.0,
                currentLng = isLat ? 105.0 : value,
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = body.RootElement.GetProperty("errors");

        // Lỗi gắn đúng trường để form gắn vào đúng ô input.
        Assert.True(isLat ? errors.TryGetProperty("currentLat", out _) : errors.TryGetProperty("currentLng", out _));
    }

    [Fact]
    public async Task Thieu_toa_do_tra_400()
    {
        using var factory = new TestAppFactory();
        var driver = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế Test");
        var client = ClientWith(factory, factory.CreateTokenFor(driver));

        var route = await SeedRouteAsync(factory, "10");
        var trip = await SeedTripAsync(factory, route.Id, driver.Id);

        // double? + [Required]: thiếu hẳn trường thì null, [Required] bắt được — nếu dùng double
        // thì thiếu trường nhận mặc định 0.0 (vĩ độ CÓ THẬT) và lỗi trôi qua im lặng.
        var thieuViDo = await client.PutAsJsonAsync(
            $"/api/trips/{trip.Id}/location",
            new { currentLng = 105.8542 });

        var thieuKinhDo = await client.PutAsJsonAsync(
            $"/api/trips/{trip.Id}/location",
            new { currentLat = 21.0285 });

        Assert.Equal(HttpStatusCode.BadRequest, thieuViDo.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, thieuKinhDo.StatusCode);

        using var bodyThieuViDo = JsonDocument.Parse(await thieuViDo.Content.ReadAsStringAsync());
        Assert.True(bodyThieuViDo.RootElement.GetProperty("errors").TryGetProperty("currentLat", out _));

        using var bodyThieuKinhDo = JsonDocument.Parse(await thieuKinhDo.Content.ReadAsStringAsync());
        Assert.True(bodyThieuKinhDo.RootElement.GetProperty("errors").TryGetProperty("currentLng", out _));
    }

    // -------------------------------------------------------------- Nhật ký kiểm toán

    [Fact]
    public async Task Cap_nhat_thanh_cong_ghi_nhat_ky_kiem_toan()
    {
        using var factory = new TestAppFactory();
        var driver = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế Test");
        var client = ClientWith(factory, factory.CreateTokenFor(driver));

        var route = await SeedRouteAsync(factory, "10");
        var trip = await SeedTripAsync(factory, route.Id, driver.Id);
        var tram = await SeedStopAsync(factory, "Bến xe Yên Nghĩa");
        await SeedRouteStopAsync(factory, route.Id, tram.Id, 1);

        var response = await client.PutAsJsonAsync(
            $"/api/trips/{trip.Id}/location",
            new { currentLat = 21.0285, currentLng = 105.8542, currentStopId = tram.Id });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Middleware ghi tự động — endpoint không tự gọi log tay: một dòng Update, Target
        // "Trips:{id}", kèm đúng người thực hiện.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var log = await db.AuditLogs.SingleOrDefaultAsync(a => a.Target == $"Trips:{trip.Id}");

        Assert.NotNull(log);
        Assert.Equal(AuditAction.Update, log.Action);
        Assert.Equal(driver.Id, log.UserId);
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

    /// <summary>
    /// Tài khoản kèm vai trò. Cố ý KHÔNG gán <c>Role</c> navigation: vai trò lúc phân quyền do
    /// JwtMiddleware nạp lại từ CSDL (CollectRoleCodes gộp RoleId + UserRoles) chứ không đọc từ
    /// token; gán navigation còn khiến EF thêm Role lần nữa vào InMemory (key cố định RoleIds
    /// trùng) — MyTicketsApiTests seed y hệt cách này.
    /// </summary>
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

    private static async Task SeedRouteStopAsync(TestAppFactory factory, Guid routeId, Guid stopId, int order)
    {
        await factory.SeedAsync(db => db.RouteStops.Add(new RouteStop
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            StopId = stopId,
            StopOrder = order,
            DistanceKm = order == 1 ? 0 : 10,
        }));
    }

    /// <summary>
    /// Chuyến chỉ có khoá ngoại, không gán navigation — InMemory không cưỡng chế khoá ngoại nên
    /// không cần seed Route/Bus cho từng chuyến (đúng lệ "seed dòng mồ côi" của AuditLogCoverageTests).
    /// </summary>
    private static async Task<Trip> SeedTripAsync(
        TestAppFactory factory,
        Guid routeId,
        Guid driverId,
        TripStatus status = TripStatus.Running,
        Guid? currentStopId = null)
    {
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            BusId = Guid.NewGuid(),
            DriverId = driverId,
            Status = status,
            DepartureTime = Ngay(1),
            CurrentStopId = currentStopId,
        };

        await factory.SeedAsync(db => db.Trips.Add(trip));

        return trip;
    }

    /// <summary>Mốc UTC trong quá khứ xa — `day` càng lớn càng gần hiện tại.</summary>
    private static DateTime Ngay(int day)
        => new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc).AddDays(day);
}
