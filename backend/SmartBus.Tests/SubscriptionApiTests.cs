using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Subscriptions;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;
using RouteEntity = SmartBus.Api.Entities.Route;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test tích hợp cho API đăng ký / huỷ theo dõi thông báo — GET/POST/DELETE /api/subscriptions
/// (US 10, Sprint 5 dòng 17 — Trần Trung Hiếu). Hợp đồng đầy đủ ở mục "Theo dõi thông báo —
/// /subscriptions" của docs/api-contract.md; thiết kế bảng ở
/// docs/37-csdl-mau-thong-bao-va-theo-doi.md.
///
/// ⚠️ Provider InMemory KHÔNG cưỡng chế chỉ mục duy nhất lẫn khoá ngoại (docs/37 §7): ca trùng đăng
/// ký ở đây đi qua nhánh kiểm-trước của service; nhánh "lượt thua cuộc đua" (DbUpdateException từ
/// chỉ mục duy nhất) chỉ sống thật ở PostgreSQL — test mô phỏng lỗi đó bằng DbContext con ném lỗi,
/// và ca cuối file ghim hai chỉ mục duy nhất bộ phận trên MÔ HÌNH (đúng lối RefundClaimConcurrencyTests).
/// </summary>
public class SubscriptionApiTests
{
    // -------------------------------------------------------------- Quyền và xác thực

    [Fact]
    public async Task Khong_gui_token_thi_ca_ba_endpoint_deu_tra_401()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/subscriptions")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/subscriptions", new { routeId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync($"/api/subscriptions/{Guid.NewGuid()}")).StatusCode);
    }

    // -------------------------------------------------------------- GET — danh sách của tôi

    [Fact]
    public async Task Chua_theo_doi_gi_thi_tra_mang_rong_khong_phai_404()
    {
        using var factory = new TestAppFactory();
        var client = await ClientAsPassengerAsync(factory);

        var response = await client.GetAsync("/api/subscriptions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, body.RootElement.ValueKind);
        Assert.Equal(0, body.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Danh_sach_chi_tra_luot_theo_doi_cua_chinh_nguoi_goi()
    {
        using var factory = new TestAppFactory();
        var chu = await SeedUserAsync(factory);
        var nguoiKhac = await SeedUserAsync(factory);
        var client = ClientWith(factory, factory.CreateTokenFor(chu));

        var route = await SeedRouteAsync(factory, "08");
        var tram = await SeedStopAsync(factory, "Bờ Hồ");
        await SeedRouteStopAsync(factory, route.Id, tram.Id, stopOrder: 1);

        await SeedSubscriptionAsync(factory, chu.Id, route.Id, tram.Id);
        await SeedSubscriptionAsync(factory, nguoiKhac.Id, route.Id, tram.Id);

        var body = JsonDocument.Parse(await (await client.GetAsync("/api/subscriptions")).Content.ReadAsStringAsync());

        // Đúng một lượt của chủ — lượt của người khác không lọt vào, và không có endpoint nào hỏi
        // theo người khác được (phạm vi "của tôi" nằm trong truy vấn).
        Assert.Single(body.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task Danh_sach_tra_du_truong_hien_thi_va_moi_nhat_truoc()
    {
        using var factory = new TestAppFactory();
        var chu = await SeedUserAsync(factory);
        var client = ClientWith(factory, factory.CreateTokenFor(chu));

        var route = await SeedRouteAsync(factory, "01");
        var tram = await SeedStopAsync(factory, "Cầu Giấy");
        await SeedRouteStopAsync(factory, route.Id, tram.Id, stopOrder: 1);

        var cu = await SeedSubscriptionAsync(factory, chu.Id, route.Id, tram.Id, Ngay(1));
        var moi = await SeedSubscriptionAsync(factory, chu.Id, route.Id, stopId: null, Ngay(2));

        var body = JsonDocument.Parse(await (await client.GetAsync("/api/subscriptions")).Content.ReadAsStringAsync());
        var items = body.RootElement.EnumerateArray().ToArray();

        // Mới nhất trước; dòng theo cả tuyến có stopId/stopName null — hai nghĩa phân biệt bằng NULL.
        Assert.Equal(2, items.Length);
        Assert.Equal(moi.Id, items[0].GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("stopId").ValueKind);
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("stopName").ValueKind);

        // Dòng theo trạm kèm đủ ba trường hiển thị chép từ bảng Routes/Stops.
        var dongTram = items[1];
        Assert.Equal(cu.Id, dongTram.GetProperty("id").GetGuid());
        Assert.Equal(route.Id, dongTram.GetProperty("routeId").GetGuid());
        Assert.Equal("01", dongTram.GetProperty("routeCode").GetString());
        Assert.Equal("Tuyến 01", dongTram.GetProperty("routeName").GetString());
        Assert.Equal(tram.Id, dongTram.GetProperty("stopId").GetGuid());
        Assert.Equal("Cầu Giấy", dongTram.GetProperty("stopName").GetString());
        Assert.Equal(Ngay(1), dongTram.GetProperty("createdAt").GetDateTime());
    }

    // -------------------------------------------------------------- POST — đăng ký

    [Fact]
    public async Task Dang_ky_theo_tram_tra_201_voi_dong_vua_tao()
    {
        using var factory = new TestAppFactory();
        var client = await ClientAsPassengerAsync(factory);

        var route = await SeedRouteAsync(factory, "08");
        var tram = await SeedStopAsync(factory, "Bờ Hồ");
        await SeedRouteStopAsync(factory, route.Id, tram.Id, stopOrder: 2);

        var response = await client.PostAsJsonAsync("/api/subscriptions", new { routeId = route.Id, stopId = tram.Id });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.NotEqual(Guid.Empty, body.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(route.Id, body.RootElement.GetProperty("routeId").GetGuid());
        Assert.Equal("08", body.RootElement.GetProperty("routeCode").GetString());
        Assert.Equal("Tuyến 08", body.RootElement.GetProperty("routeName").GetString());
        Assert.Equal(tram.Id, body.RootElement.GetProperty("stopId").GetGuid());
        Assert.Equal("Bờ Hồ", body.RootElement.GetProperty("stopName").GetString());
    }

    [Fact]
    public async Task Dang_ky_ca_tuyen_khi_stopId_bo_trong()
    {
        using var factory = new TestAppFactory();
        var client = await ClientAsPassengerAsync(factory);

        var route = await SeedRouteAsync(factory, "08");

        var response = await client.PostAsJsonAsync("/api/subscriptions", new { routeId = route.Id });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("stopId").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("stopName").ValueKind);
    }

    [Fact]
    public async Task Thieu_routeId_tra_400_theo_truong()
    {
        using var factory = new TestAppFactory();
        var client = await ClientAsPassengerAsync(factory);

        var response = await client.PostAsJsonAsync("/api/subscriptions", new { stopId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("routeId", out _));
    }

    [Fact]
    public async Task Tuyen_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await ClientAsPassengerAsync(factory);

        var response = await client.PostAsJsonAsync("/api/subscriptions", new { routeId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Tram_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await ClientAsPassengerAsync(factory);

        var route = await SeedRouteAsync(factory, "08");

        var response = await client.PostAsJsonAsync("/api/subscriptions", new { routeId = route.Id, stopId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Tram_khong_thuoc_tuyen_tra_400_theo_truong_stopId()
    {
        // Khoá ngoại chỉ nói trạm CÓ TỒN TẠI, không nói nó nằm trên tuyến đã chọn (docs/37 §3.1) —
        // việc kiểm thuộc RouteStops là của endpoint này.
        using var factory = new TestAppFactory();
        var client = await ClientAsPassengerAsync(factory);

        var route = await SeedRouteAsync(factory, "08");
        var tramKhacTuyen = await SeedStopAsync(factory, "Trạm lạc");

        var response = await client.PostAsJsonAsync("/api/subscriptions", new { routeId = route.Id, stopId = tramKhacTuyen.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("không thuộc tuyến", body.RootElement.GetProperty("message").GetString());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("stopId", out _));
    }

    [Fact]
    public async Task Dang_ky_lan_hai_cung_tram_tra_409_khong_tao_dong_thu_hai()
    {
        using var factory = new TestAppFactory();
        var chu = await SeedUserAsync(factory);
        var client = ClientWith(factory, factory.CreateTokenFor(chu));

        var route = await SeedRouteAsync(factory, "08");
        var tram = await SeedStopAsync(factory, "Bờ Hồ");
        await SeedRouteStopAsync(factory, route.Id, tram.Id, stopOrder: 1);

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/subscriptions", new { routeId = route.Id, stopId = tram.Id })).StatusCode);

        var lanHai = await client.PostAsJsonAsync("/api/subscriptions", new { routeId = route.Id, stopId = tram.Id });

        Assert.Equal(HttpStatusCode.Conflict, lanHai.StatusCode);
        Assert.Single(JsonDocument.Parse(await (await client.GetAsync("/api/subscriptions")).Content.ReadAsStringAsync()).RootElement.EnumerateArray());
    }

    [Fact]
    public async Task Dang_ky_ca_tuyen_hai_lan_tra_409()
    {
        // Ca "theo dõi cả tuyến" hai lần — đúng ca mà chỉ mục duy nhất THƯỜNG không chặn được vì
        // PostgreSQL coi hai NULL là khác nhau (docs/37 §3.3). Trên InMemory đi qua nhánh kiểm-trước.
        using var factory = new TestAppFactory();
        var client = await ClientAsPassengerAsync(factory);

        var route = await SeedRouteAsync(factory, "08");

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/subscriptions", new { routeId = route.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/subscriptions", new { routeId = route.Id })).StatusCode);
    }

    [Fact]
    public async Task Vua_theo_doi_ca_tuyen_vua_theo_doi_mot_tram_khong_xung_dot()
    {
        // Hai dòng khác nhau, không trùng khoá nào — docs/37 §7: khách được phép theo dõi cả hai
        // mức chi tiết cùng lúc; service bắn tin (Kiên) phải tự khử trùng.
        using var factory = new TestAppFactory();
        var client = await ClientAsPassengerAsync(factory);

        var route = await SeedRouteAsync(factory, "08");
        var tram = await SeedStopAsync(factory, "Bờ Hồ");
        await SeedRouteStopAsync(factory, route.Id, tram.Id, stopOrder: 1);

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/subscriptions", new { routeId = route.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/subscriptions", new { routeId = route.Id, stopId = tram.Id })).StatusCode);

        Assert.Equal(2, JsonDocument.Parse(await (await client.GetAsync("/api/subscriptions")).Content.ReadAsStringAsync()).RootElement.GetArrayLength());
    }

    // -------------------------------------------------------------- DELETE — huỷ theo dõi

    [Fact]
    public async Task Huy_theo_doi_xoa_dong_va_goi_lai_cung_id_tra_404()
    {
        using var factory = new TestAppFactory();
        var chu = await SeedUserAsync(factory);
        var client = ClientWith(factory, factory.CreateTokenFor(chu));

        var route = await SeedRouteAsync(factory, "08");
        var luot = await SeedSubscriptionAsync(factory, chu.Id, route.Id, stopId: null);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/subscriptions/{luot.Id}")).StatusCode);
        Assert.Empty(JsonDocument.Parse(await (await client.GetAsync("/api/subscriptions")).Content.ReadAsStringAsync()).RootElement.EnumerateArray());

        // Xoá lần hai: không còn gì để xoá — 404 là câu trả lời đúng, không idempotent (hợp đồng).
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/subscriptions/{luot.Id}")).StatusCode);
    }

    [Fact]
    public async Task Xoa_luot_theo_doi_cua_nguoi_khac_tra_404_cung_nhu_khong_ton_tai()
    {
        using var factory = new TestAppFactory();
        var chu = await SeedUserAsync(factory);
        var nguoiKhac = await SeedUserAsync(factory);
        var client = ClientWith(factory, factory.CreateTokenFor(chu));

        var route = await SeedRouteAsync(factory, "08");
        var luotCuaNguoiKhac = await SeedSubscriptionAsync(factory, nguoiKhac.Id, route.Id, stopId: null);

        // Cùng một câu 404 cho "không tồn tại" và "của người khác" — không để lộ lượt theo dõi nào.
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/subscriptions/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/subscriptions/{luotCuaNguoiKhac.Id}")).StatusCode);

        // Lượt của người khác còn nguyên sau lượt xoá hỏng.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.NotNull(await db.Subscriptions.SingleOrDefaultAsync(s => s.Id == luotCuaNguoiKhac.Id));
    }

    // -------------------------------------------------------------- Nhật ký kiểm toán

    [Fact]
    public async Task Dang_ky_va_huy_deu_duoc_ghi_nhat_ky_dung_doi_tuong()
    {
        using var factory = new TestAppFactory();
        var chu = await SeedUserAsync(factory);
        var client = ClientWith(factory, factory.CreateTokenFor(chu));

        var route = await SeedRouteAsync(factory, "08");

        var tao = JsonDocument.Parse(await (await client.PostAsJsonAsync("/api/subscriptions", new { routeId = route.Id })).Content.ReadAsStringAsync());
        var id = tao.RootElement.GetProperty("id").GetGuid();

        await client.DeleteAsync($"/api/subscriptions/{id}");

        var nhatKy = await DocNhatKyAsync(factory);
        var dongTao = Assert.Single(nhatKy.Where(l => l.Action == AuditAction.Create));
        var dongXoa = Assert.Single(nhatKy.Where(l => l.Action == AuditAction.Delete));

        // POST: Target là id dòng mới đọc từ body trả về; DELETE: Target từ {id} trên đường dẫn.
        Assert.Equal($"Subscriptions:{id}", dongTao.Target);
        Assert.Equal($"Subscriptions:{id}", dongXoa.Target);
        Assert.Equal(chu.Id, dongTao.UserId);
    }

    // -------------------------------------------------------------- Service — chốt tầng CSDL

    [Fact]
    public async Task Loi_chi_muc_duy_nhat_duoc_dich_thanh_409_khong_noi_thanh_500()
    {
        // Nhánh thua cuộc đua thật (docs/37 §3.3): hai lượt đăng ký song song cùng lọt qua phép kiểm
        // trước, lượt thua nhận DbUpdateException từ chỉ mục duy nhất ở SaveChanges. Mô phỏng bằng
        // DbContext con ném lỗi — trên InMemory không có chỉ mục để mà ném thật, còn chờ PostgreSQL
        // thì test phải đua nhau theo thời điểm (flaky).
        var tenCsdl = $"subscription-db-nem-{Guid.NewGuid():N}";
        using var db = new DbNemTruyVan(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(tenCsdl).Options);
        var route = new RouteEntity
        {
            Code = "08",
            Name = "Tuyến 08",
            Origin = "Bến Thành",
            Destination = "Chợ Lớn",
        };
        db.Routes.Add(route);
        await db.SaveChangesAsync();

        var service = new SubscriptionService(db);

        var ketQua = await service.SubscribeAsync(
            Guid.NewGuid(),
            new CreateSubscriptionRequest { RouteId = route.Id });

        Assert.False(ketQua.Success);
        Assert.Equal(ServiceErrorKind.Conflict, ketQua.ErrorKind);
    }

    [Fact]
    public void Hai_chi_muc_duy_nhat_bo_phan_phai_co_mat_tren_mo_hinh()
    {
        // Chốt chống hoàn... không — chốt chống ĐĂNG KÝ TRÙNG ở tầng CSDL (docs/37 §3.3) chỉ tồn tại
        // thật ở PostgreSQL; InMemory không cưỡng chế nên test xanh không nói gì. Ghim trên MÔ HÌNH
        // để ai vô tình xoá HasFilter thì test đỏ ngay (cùng lối RefundClaimConcurrencyTests).
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=khong-ket-noi;Database=chi-soi-metadata;Username=khong;Password=khong")
            .Options);

        var entity = db.Model.FindEntityType(typeof(Subscription));
        Assert.NotNull(entity);

        var chiMuc = entity!.GetIndexes().ToArray();
        var theoCaTuyen = Assert.Single(chiMuc.Where(i => i.GetFilter() is not null && i.GetFilter()!.Contains("\"StopId\" IS NULL")));
        var theoMotTram = Assert.Single(chiMuc.Where(i => i.GetFilter() is not null && i.GetFilter()!.Contains("\"StopId\" IS NOT NULL")));

        Assert.True(theoCaTuyen.IsUnique);
        Assert.True(theoMotTram.IsUnique);
    }

    // -------------------------------------------------------------- Hạ tầng test

    private static HttpClient ClientWith(TestAppFactory factory, string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    private static async Task<HttpClient> ClientAsPassengerAsync(TestAppFactory factory)
    {
        var passenger = await SeedUserAsync(factory);
        return ClientWith(factory, factory.CreateTokenFor(passenger));
    }

    /// <summary>Mốc "bây giờ" cố định để seed createdAt rõ ràng.</summary>
    private static DateTime Ngay(int gio) => new(2026, 10, 16, gio, 0, 0, DateTimeKind.Utc);

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

    private static async Task SeedRouteStopAsync(
        TestAppFactory factory,
        Guid routeId,
        Guid stopId,
        int stopOrder)
    {
        await factory.SeedAsync(db => db.RouteStops.Add(new RouteStop
        {
            RouteId = routeId,
            StopId = stopId,
            StopOrder = stopOrder,
            DistanceKm = 0,
        }));
    }

    private static async Task<Subscription> SeedSubscriptionAsync(
        TestAppFactory factory,
        Guid userId,
        Guid routeId,
        Guid? stopId = null,
        DateTime? createdAt = null)
    {
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RouteId = routeId,
            StopId = stopId,
            CreatedAt = createdAt ?? DateTime.UtcNow,
        };

        await factory.SeedAsync(db => db.Subscriptions.Add(subscription));

        return subscription;
    }

    private static async Task<List<AuditLog>> DocNhatKyAsync(TestAppFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuditLogs.AsNoTracking().ToListAsync();
    }

    /// <summary>
    /// DbContext con mô phỏng lỗi chỉ mục duy nhất của PostgreSQL ở đúng bước SaveChanges — để test
    /// nhánh dịch DbUpdateException → 409 mà không phải đua theo thời điểm. Chỉ ném khi có dòng
    /// Subscription đang thêm: lượt SaveChanges để seed Route phải đi qua bình thường.
    /// </summary>
    private sealed class DbNemTruyVan(DbContextOptions<AppDbContext> options) : AppDbContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (ChangeTracker.Entries<Subscription>().Any(e => e.State == EntityState.Added))
            {
                return Task.FromException<int>(
                    new DbUpdateException("mô phỏng vi phạm chỉ mục duy nhất IX_Subscriptions", innerException: null));
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
