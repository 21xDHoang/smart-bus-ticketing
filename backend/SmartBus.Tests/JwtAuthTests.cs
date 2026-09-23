using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test tích hợp cho middleware xác thực JWT (task story 22 — Vàng Thị Dăm).
/// Mỗi test dựng một app riêng với CSDL InMemory riêng để không lẫn dữ liệu sang nhau.
/// </summary>
public class JwtAuthTests
{
    private const string ProtectedUrl = "/api/_test/protected";

    [Fact]
    public async Task Khong_gui_token_thi_tra_401()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync(ProtectedUrl);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Chưa đăng nhập", await MessageAsync(response));
    }

    [Fact]
    public async Task Token_rac_thi_tra_401()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "day-khong-phai-jwt");

        var response = await client.GetAsync(ProtectedUrl);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("không hợp lệ", await MessageAsync(response));
    }

    [Fact]
    public async Task Token_ky_bang_khoa_khac_thi_tra_401()
    {
        using var factory = new TestAppFactory();
        var role = NewRole("Passenger");
        var user = NewUser(role);
        await factory.SeedAsync(db => db.Users.Add(user));

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateHandcraftedToken(user, role.Code, DateTime.UtcNow.AddMinutes(30), signingKey: "khoa-gia-mao-cua-ke-tan-cong-dai-hon-32-byte"));

        var response = await client.GetAsync(ProtectedUrl);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("không hợp lệ", await MessageAsync(response));
    }

    [Fact]
    public async Task Token_het_han_thi_tra_401()
    {
        using var factory = new TestAppFactory();
        var role = NewRole("Passenger");
        var user = NewUser(role);
        await factory.SeedAsync(db => db.Users.Add(user));

        // Hết hạn 10 phút — xa hơn ClockSkew 30 giây nên chắc chắn bị từ chối.
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateHandcraftedToken(user, role.Code, DateTime.UtcNow.AddMinutes(-10)));

        var response = await client.GetAsync(ProtectedUrl);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("hết hạn", await MessageAsync(response));
    }

    [Fact]
    public async Task Token_hop_le_thi_gan_duoc_user_va_vai_tro_vao_request()
    {
        using var factory = new TestAppFactory();
        var role = NewRole("Passenger");
        var user = NewUser(role);
        await factory.SeedAsync(db => db.Users.Add(user));

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", factory.CreateTokenFor(user));

        var response = await client.GetAsync(ProtectedUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;

        Assert.True(root.GetProperty("isAuthenticated").GetBoolean());
        Assert.Equal(user.Id.ToString(), root.GetProperty("userId").GetString());
        Assert.Equal(user.FullName, root.GetProperty("fullName").GetString());
        Assert.Equal(new[] { "Passenger" }, RolesOf(root));

        // Có mặt trong HttpContext.Items nghĩa là middleware đã nạp user từ CSDL và gắn vào request.
        Assert.Equal(user.PhoneNumber, root.GetProperty("phoneNumber").GetString());
    }

    [Fact]
    public async Task Tai_khoan_bi_khoa_thi_tra_401_du_token_con_han()
    {
        using var factory = new TestAppFactory();
        var role = NewRole("Passenger");
        var user = NewUser(role);
        await factory.SeedAsync(db => db.Users.Add(user));

        // Token phát ra lúc tài khoản còn hoạt động.
        var token = factory.CreateTokenFor(user);

        await factory.SeedAsync(db =>
        {
            var stored = db.Users.Find(user.Id);
            stored!.IsActive = false;
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(ProtectedUrl);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("bị khóa", await MessageAsync(response));
    }

    [Fact]
    public async Task Tai_khoan_da_bi_xoa_thi_tra_401()
    {
        using var factory = new TestAppFactory();
        var role = NewRole("Passenger");
        var user = NewUser(role);
        await factory.SeedAsync(db => db.Users.Add(user));

        var token = factory.CreateTokenFor(user);

        await factory.SeedAsync(db =>
        {
            var stored = db.Users.Find(user.Id);
            db.Users.Remove(stored!);
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(ProtectedUrl);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("không còn tồn tại", await MessageAsync(response));
    }

    [Fact]
    public async Task Vai_tro_doi_trong_csdl_thi_co_hieu_luc_ngay_khong_can_token_moi()
    {
        using var factory = new TestAppFactory();
        var passenger = NewRole("Passenger");
        var admin = NewRole("Admin");
        var user = NewUser(passenger);
        await factory.SeedAsync(db =>
        {
            db.Roles.AddRange(admin, passenger);
            db.Users.Add(user);
        });

        // Token phát ra khi còn là Passenger.
        var token = factory.CreateTokenFor(user);

        await factory.SeedAsync(db =>
        {
            var stored = db.Users.Find(user.Id);
            stored!.RoleId = admin.Id;
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(ProtectedUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "Admin" }, RolesOf(body.RootElement));
    }

    [Fact]
    public async Task Vai_tro_gan_them_qua_bang_UserRoles_cung_duoc_nhan()
    {
        using var factory = new TestAppFactory();
        var passenger = NewRole("Passenger");
        var manager = NewRole("Manager");
        var user = NewUser(passenger);
        await factory.SeedAsync(db =>
        {
            db.Roles.AddRange(manager, passenger);
            db.Users.Add(user);
        });

        var token = factory.CreateTokenFor(user);

        // Gán thêm vai trò qua bảng nối — đây là cách API "gán/thu hồi vai trò" sẽ ghi.
        await factory.SeedAsync(db => db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = manager.Id }));

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(ProtectedUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "Manager", "Passenger" }, RolesOf(body.RootElement));
    }

    private static string[] RolesOf(JsonElement root)
        => root.GetProperty("roles").EnumerateArray().Select(item => item.GetString()!).ToArray();

    private static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("message").GetString() ?? string.Empty;
    }

    private static Role NewRole(string code)
        => new() { Id = Guid.NewGuid(), Code = code, Name = code };

    private static UserEntity NewUser(Role role, bool isActive = true) => new()
    {
        Id = Guid.NewGuid(),
        PhoneNumber = "09" + Guid.NewGuid().ToString("N")[..8],
        FullName = "Người Dùng Test",
        PasswordHash = "hash-khong-dung-toi-trong-test",
        IsActive = isActive,
        RoleId = role.Id,
        Role = role,
    };

    /// <summary>
    /// Ký token bằng tay. Chỉ dùng cho các ca mà <see cref="ITokenService.CreateAccessToken"/>
    /// không tạo được: token hết hạn và token ký sai khoá.
    /// </summary>
    private static string CreateHandcraftedToken(UserEntity user, string roleCode, DateTime expiresAt, string? signingKey = null)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey ?? TestAppFactory.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: TestAppFactory.Issuer,
            audience: TestAppFactory.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.FullName),
                new Claim(ClaimTypes.Role, roleCode),
            ],
            notBefore: expiresAt.AddMinutes(-30),
            expires: expiresAt,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

/// <summary>
/// App thật nhưng trỏ CSDL InMemory và dùng cấu hình JWT của test. Mỗi instance một CSDL riêng.
/// </summary>
internal sealed class TestAppFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "SmartBus";

    public const string Audience = "SmartBusClient";

    public const string SigningKey = "khoa-ky-danh-cho-test-dai-hon-32-ky-tu-abc";

    private readonly string _databaseName = $"smartbus-test-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Phải có mặt ngay lúc Program.cs dựng service (AddJwtAuthentication đọc và kiểm tra
        // Jwt:Key rồi chết ngay nếu thiếu), nên dùng UseSetting chứ không phải
        // ConfigureAppConfiguration — nguồn config thêm ở đó chỉ có hiệu lực sau khi host build.
        builder.UseSetting("Jwt:Issuer", Issuer);
        builder.UseSetting("Jwt:Audience", Audience);
        builder.UseSetting("Jwt:Key", SigningKey);
        builder.UseSetting("Jwt:AccessTokenMinutes", "30");
        builder.UseSetting("Jwt:RefreshTokenDays", "7");

        builder.ConfigureTestServices(services =>
        {
            // Gỡ hẳn đăng ký DbContext trỏ PostgreSQL. Phải xoá cả
            // IDbContextOptionsConfiguration vì EF Core 9+ giữ action cấu hình ở đó —
            // để sót thì Npgsql và InMemory cùng được đăng ký và EF sẽ báo lỗi.
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(_databaseName));

            // Nạp controller test vào app thật.
            services.AddControllers().AddApplicationPart(typeof(TestProtectedController).Assembly);
        });
    }

    /// <summary>Sinh access token bằng chính <see cref="ITokenService"/> của app, tránh test lệch khỏi code thật.</summary>
    public string CreateTokenFor(UserEntity user)
    {
        using var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ITokenService>().CreateAccessToken(user).Token;
    }

    /// <summary>Ghi dữ liệu vào CSDL test. Luôn lưu lại vì middleware đọc ở scope khác.</summary>
    public async Task SeedAsync(Action<AppDbContext> seed)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        seed(db);

        await db.SaveChangesAsync();
    }
}
