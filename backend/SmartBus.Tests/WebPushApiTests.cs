using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test tích hợp cho bề mặt Web Push — GET /api/web-push/vapid-public-key và
/// GET/POST/DELETE /api/web-push/devices, POST /api/web-push/devices/{id}/test (US 10 "Thông báo
/// trạm", Sprint 4 dòng 42 — Trần Trung Hiếu). Hợp đồng đầy đủ ở mục "Web Push — /web-push" của
/// docs/api-contract.md; luồng và lý do store trong bộ nhớ ở docs/54.
///
/// Kênh gửi thật bị thay bằng <see cref="GhiNhanWebPushSender"/> trong host test (đã giải thích ở
/// WebPushApiTestFactory) — test bề mặt không gọi mạng.
/// </summary>
public class WebPushApiTests
{
    private const string EndpointMau = "https://fcm.googleapis.com/fcm/send/thi-eb-i";
    private const string P256DhMau = "BKOtatI8ZS2MqSjG3PlfrDb3rYwHAI2qHRO1LuT_fkrr8fFARpPU8B8Rpv44Uvc4BuHY7Ow5oiPCZPYXcQOXDp4";
    private const string AuthMau = "AAECAwQFBgcICQoLDA0ODw";

    // -------------------------------------------------------------- Quyền và xác thực

    [Fact]
    public async Task Khong_gui_token_thi_ca_nam_endpoint_deu_tra_401()
    {
        using var factory = new WebPushApiTestFactory();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/web-push/vapid-public-key")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/web-push/devices")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/web-push/devices", DangKyMau())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync($"/api/web-push/devices/{Guid.NewGuid()}/test", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync($"/api/web-push/devices/{Guid.NewGuid()}")).StatusCode);
    }

    // -------------------------------------------------------------- GET — khoá VAPID và danh sách

    [Fact]
    public async Task Khoa_vapid_tra_dung_gia_tri_cau_hinh()
    {
        using var factory = new WebPushApiTestFactory();
        var client = await ClientAsPassengerAsync(factory);

        var response = await client.GetAsync("/api/web-push/vapid-public-key");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(WebPushApiTestFactory.PublicKey, body.RootElement.GetProperty("publicKey").GetString());
    }

    [Fact]
    public async Task Chua_dang_ky_thiet_bi_nao_thi_tra_mang_rong()
    {
        using var factory = new WebPushApiTestFactory();
        var client = await ClientAsPassengerAsync(factory);

        var response = await client.GetAsync("/api/web-push/devices");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, body.RootElement.ValueKind);
        Assert.Equal(0, body.RootElement.GetArrayLength());
    }

    // -------------------------------------------------------------- POST — đăng ký thiết bị

    [Fact]
    public async Task Dang_ky_hop_le_tra_201_kem_dong_thiet_bi()
    {
        using var factory = new WebPushApiTestFactory();
        var client = await ClientAsPassengerAsync(factory);

        var response = await client.PostAsJsonAsync("/api/web-push/devices", DangKyMau());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(EndpointMau, body.RootElement.GetProperty("endpoint").GetString());
        Assert.NotEqual(Guid.Empty, body.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("expirationTime").ValueKind);

        var devices = JsonDocument.Parse(await (await client.GetAsync("/api/web-push/devices")).Content.ReadAsStringAsync());
        Assert.Equal(1, devices.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Dang_ky_lai_dung_endpoint_tra_200_va_khong_them_dong_thu_hai()
    {
        using var factory = new WebPushApiTestFactory();
        var client = await ClientAsPassengerAsync(factory);

        var first = JsonDocument.Parse(await (await client.PostAsJsonAsync("/api/web-push/devices", DangKyMau())).Content.ReadAsStringAsync());
        var second = await client.PostAsJsonAsync("/api/web-push/devices", DangKyMau());

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var secondBody = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal(first.RootElement.GetProperty("id").GetGuid(), secondBody.RootElement.GetProperty("id").GetGuid());

        var devices = JsonDocument.Parse(await (await client.GetAsync("/api/web-push/devices")).Content.ReadAsStringAsync());
        Assert.Equal(1, devices.RootElement.GetArrayLength());
    }

    [Theory]
    [InlineData("http://example.com/push", "endpoint")]
    [InlineData("khong-phai-url", "endpoint")]
    [InlineData("https://", "endpoint")]
    public async Task Endpoint_khong_phai_https_that_thi_400_theo_truong(string endpoint, string truong)
    {
        using var factory = new WebPushApiTestFactory();
        var client = await ClientAsPassengerAsync(factory);

        var response = await client.PostAsJsonAsync("/api/web-push/devices", new
        {
            endpoint,
            p256Dh = P256DhMau,
            auth = AuthMau,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty(truong, out _));
    }

    [Fact]
    public async Task Khoa_p256dh_hoac_auth_sai_do_dai_thi_400_theo_truong()
    {
        using var factory = new WebPushApiTestFactory();
        var client = await ClientAsPassengerAsync(factory);

        var response = await client.PostAsJsonAsync("/api/web-push/devices", new
        {
            endpoint = EndpointMau,
            p256Dh = "cua-ngan", // 8 byte — phải 65 byte
            auth = AuthMau,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("p256Dh", out _));
        Assert.False(body.RootElement.GetProperty("errors").TryGetProperty("auth", out _));
    }

    [Fact]
    public async Task Thieu_truong_bat_buoc_thi_400_kem_loi_tung_truong()
    {
        using var factory = new WebPushApiTestFactory();
        var client = await ClientAsPassengerAsync(factory);

        var response = await client.PostAsJsonAsync("/api/web-push/devices", new { p256Dh = P256DhMau, auth = AuthMau });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("endpoint", out _));
    }

    [Fact]
    public async Task User_agent_duoc_giu_lai_de_man_cai_dat_phan_biet_thiet_bi()
    {
        using var factory = new WebPushApiTestFactory();
        var client = await ClientAsPassengerAsync(factory);
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ChromeTest", "1.0"));

        var response = await client.PostAsJsonAsync("/api/web-push/devices", DangKyMau());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.StartsWith("ChromeTest", body.RootElement.GetProperty("userAgent").GetString());
    }

    // -------------------------------------------------------------- Phạm vi "của tôi"

    [Fact]
    public async Task Thiet_bi_cua_nguoi_khac_khong_lot_vao_danh_sach_va_thao_tac_deu_404()
    {
        using var factory = new WebPushApiTestFactory();
        var chu = await SeedUserAsync(factory);
        var nguoiKhac = await SeedUserAsync(factory);

        var device = await DangKyAsync(ClientWith(factory, factory.CreateTokenFor(chu)));

        var clientKhac = ClientWith(factory, factory.CreateTokenFor(nguoiKhac));
        var danhSach = JsonDocument.Parse(await (await clientKhac.GetAsync("/api/web-push/devices")).Content.ReadAsStringAsync());
        Assert.Equal(0, danhSach.RootElement.GetArrayLength());

        Assert.Equal(HttpStatusCode.NotFound, (await clientKhac.DeleteAsync($"/api/web-push/devices/{device}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clientKhac.PostAsync($"/api/web-push/devices/{device}/test", null)).StatusCode);

        // Thiết bị của chủ vẫn nguyên vẹn sau các lượt thăm dò.
        var danhSachChu = JsonDocument.Parse(await (await ClientWith(factory, factory.CreateTokenFor(chu)).GetAsync("/api/web-push/devices")).Content.ReadAsStringAsync());
        Assert.Equal(1, danhSachChu.RootElement.GetArrayLength());
    }

    // -------------------------------------------------------------- DELETE — gỡ thiết bị

    [Fact]
    public async Task Go_thiet_bi_tra_204_va_lan_go_lai_tra_404()
    {
        using var factory = new WebPushApiTestFactory();
        var client = await ClientAsPassengerAsync(factory);
        var device = await DangKyAsync(client);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/web-push/devices/{device}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/web-push/devices/{device}")).StatusCode);

        var devices = JsonDocument.Parse(await (await client.GetAsync("/api/web-push/devices")).Content.ReadAsStringAsync());
        Assert.Equal(0, devices.RootElement.GetArrayLength());
    }

    // -------------------------------------------------------------- POST …/test — tin thử

    [Fact]
    public async Task Gui_tin_thu_tra_204_va_sender_nhan_dung_thiet_bi_dung_noi_dung()
    {
        using var factory = new WebPushApiTestFactory();
        var chu = await SeedUserAsync(factory);
        var device = await DangKyAsync(ClientWith(factory, factory.CreateTokenFor(chu)));

        var response = await ClientWith(factory, factory.CreateTokenFor(chu)).PostAsync($"/api/web-push/devices/{device}/test", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var daGui = factory.DaGui.DaGuiChoThietBi;
        Assert.Single(daGui);
        Assert.Equal(chu.Id, daGui[0].UserId);
        Assert.Equal(device, daGui[0].DeviceId);
        Assert.Equal("/notifications", daGui[0].Message.Url);
        Assert.Contains("thử", daGui[0].Message.Title);
    }

    // -------------------------------------------------------------- Nhật ký

    [Fact]
    public async Task Ba_thao_tac_doi_du_lieu_deu_duoc_ghi_nhat_ky()
    {
        using var factory = new WebPushApiTestFactory();
        var chu = await SeedUserAsync(factory);
        var client = ClientWith(factory, factory.CreateTokenFor(chu));

        var created = await client.PostAsJsonAsync("/api/web-push/devices", DangKyMau());
        var deviceId = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        await client.PostAsync($"/api/web-push/devices/{deviceId}/test", null);
        await client.DeleteAsync($"/api/web-push/devices/{deviceId}");

        var logs = await DocNhatKyAsync(factory);
        Assert.Contains(logs, l => l.Action == AuditAction.Create && l.Target == $"Devices:{deviceId}");
        Assert.Equal(2, logs.Count(l => l.Action == AuditAction.Create && l.Target == $"Devices:{deviceId}"));
        Assert.Contains(logs, l => l.Action == AuditAction.Delete && l.Target == $"Devices:{deviceId}");
    }

    // -------------------------------------------------------------- Hạ tầng test

    private static object DangKyMau() => new
    {
        endpoint = EndpointMau,
        p256Dh = P256DhMau,
        auth = AuthMau,
    };

    /// <summary>Đăng ký một thiết bị rồi trả id — người gọi tự dựng client với token của mình.</summary>
    private static async Task<Guid> DangKyAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/web-push/devices", DangKyMau());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
    }

    private static HttpClient ClientWith(WebPushApiTestFactory factory, string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    private static async Task<HttpClient> ClientAsPassengerAsync(WebPushApiTestFactory factory)
    {
        var passenger = await SeedUserAsync(factory);
        return ClientWith(factory, factory.CreateTokenFor(passenger));
    }

    private static async Task<UserEntity> SeedUserAsync(WebPushApiTestFactory factory)
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

    private static async Task<List<AuditLog>> DocNhatKyAsync(WebPushApiTestFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuditLogs.AsNoTracking().ToListAsync();
    }
}
