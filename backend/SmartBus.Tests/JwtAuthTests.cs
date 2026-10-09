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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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

            // Job nền của app không được chạy thật trong host test — lý do đầy đủ ở ThayJobNenBangBanNoop.
            ThayJobNenBangBanNoop(services);
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

    /// <summary>
    /// Đổi bốn job nền của app sang bản chạy RỖNG trong host test
    /// (<see cref="NoopTripGenerationJob"/>, <see cref="NoopMonthlyPassExpiryJob"/>,
    /// <see cref="NoopSeatHoldExpiryJob"/>, <see cref="NoopSeatHoldAbuseJob"/>).
    ///
    /// Vì sao: CI ngày 03/10/2026 đỏ trên PR #81 — nhánh chỉ sửa frontend, phần backend y hệt main —
    /// vì 3 test đếm-đúng của <c>TripSearchCacheApiTests</c> bị job sinh chuyến THẬT chạy ngay trong
    /// host test. Lượt quét đầu của job chạy liền khi host khởi động (Task.Yield rồi quét luôn), tức
    /// là ĐUA với lúc test seed dữ liệu: khi nó đọc CSDL sau lúc test seed xong chuyến ngày quá khứ,
    /// nó nhân bản mẫu đó sang 7 ngày tới và làm vỡ các assert. Log CI là bằng chứng khớp từng con
    /// số: "Đã sinh 14 chuyến" ×2 + "Đã sinh 7 chuyến" ứng đúng 16, 16, 8 của ba test đỏ; còn bản
    /// xanh của main chỉ là lần đó may thắng đua.
    ///
    /// Vì sao KHÔNG <c>RemoveAll&lt;IHostedService&gt;()</c>: hai test wiring
    /// (<see cref="TripGenerationWiringTests"/>, <see cref="MonthlyPassExpiryWiringTests"/>) khẳng
    /// định job được đăng ký đúng một lần qua
    /// <c>GetServices&lt;IHostedService&gt;().OfType&lt;job thật&gt;()</c>. Gỡ hẳn thì chúng đỏ oan;
    /// thay bằng subclass giữ nguyên hình dạng đăng ký nên chúng vẫn bắt được nếu Program.cs lỡ xoá
    /// hoặc đăng ký trùng AddHostedService. Chép đúng số lượng descriptor bị gỡ (vòng lặp chứ không
    /// Add một lần) để phép đếm đó không bị làm mờ.
    /// </summary>
    private static void ThayJobNenBangBanNoop(IServiceCollection services)
    {
        var jobThat = services
            .Where(d => d.ServiceType == typeof(IHostedService)
                && d.ImplementationType is not null
                && BanNoopCuaJob.ContainsKey(d.ImplementationType))
            .ToList();

        foreach (var descriptor in jobThat)
        {
            services.Remove(descriptor);

            services.Add(ServiceDescriptor.Singleton(
                typeof(IHostedService),
                BanNoopCuaJob[descriptor.ImplementationType!]));
        }
    }

    /// <summary>
    /// Job thật → bản noop tương ứng. Bảng tra chứ không phải chuỗi <c>if</c>: thêm job nền thứ năm
    /// thì thêm đúng một dòng ở đây, không phải sửa một biểu thức điều kiện đã dài ra theo mỗi job —
    /// và quên job mới sẽ lộ ra ngay ở <see cref="TestHostKhongChayJobNenTests"/>.
    /// </summary>
    private static readonly Dictionary<Type, Type> BanNoopCuaJob = new()
    {
        [typeof(TripGenerationBackgroundService)] = typeof(NoopTripGenerationJob),
        [typeof(MonthlyPassExpiryBackgroundService)] = typeof(NoopMonthlyPassExpiryJob),
        [typeof(SeatHoldExpiryBackgroundService)] = typeof(NoopSeatHoldExpiryJob),
        [typeof(SeatHoldAbuseBackgroundService)] = typeof(NoopSeatHoldAbuseJob),
    };

    /// <summary>
    /// Bản "chạy rỗng" của <see cref="TripGenerationBackgroundService"/> cho host test — lý do đầy
    /// đủ ở <see cref="ThayJobNenBangBanNoop"/>. Vẫn là subclass nên
    /// <c>OfType&lt;TripGenerationBackgroundService&gt;()</c> của test wiring vẫn thấy nó; chỉ khác
    /// là <c>ExecuteAsync</c> trả về ngay, không có vòng lặp nền nào thức dậy giữa lúc test chạy.
    /// </summary>
    private sealed class NoopTripGenerationJob : TripGenerationBackgroundService
    {
        public NoopTripGenerationJob(
            IServiceScopeFactory scopeFactory,
            ILogger<TripGenerationBackgroundService> logger)
            : base(scopeFactory, logger)
        {
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
    }

    /// <summary>
    /// Bản "chạy rỗng" của <see cref="MonthlyPassExpiryBackgroundService"/> cho host test — cùng lý
    /// do với <see cref="NoopTripGenerationJob"/>; job vé tháng cũng chạy trong mọi host test từ
    /// Sprint 1 và cùng kiểu đua, chỉ là chưa vỡ test nào.
    /// </summary>
    private sealed class NoopMonthlyPassExpiryJob : MonthlyPassExpiryBackgroundService
    {
        public NoopMonthlyPassExpiryJob(
            IServiceScopeFactory scopeFactory,
            ILogger<MonthlyPassExpiryBackgroundService> logger)
            : base(scopeFactory, logger)
        {
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
    }

    /// <summary>
    /// Bản "chạy rỗng" của <see cref="SeatHoldExpiryBackgroundService"/> cho host test — cùng lý do
    /// với <see cref="NoopTripGenerationJob"/>. Job này quét mỗi PHÚT (nhanh nhất trong ba job), nên
    /// nếu để bản thật chạy trong host test thì nó không chỉ đua một lần lúc khởi động như hai job
    /// kia mà còn quét lại giữa lúc test đang chạy.
    /// </summary>
    private sealed class NoopSeatHoldExpiryJob : SeatHoldExpiryBackgroundService
    {
        public NoopSeatHoldExpiryJob(
            IServiceScopeFactory scopeFactory,
            ILogger<SeatHoldExpiryBackgroundService> logger)
            : base(scopeFactory, logger)
        {
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
    }

    /// <summary>
    /// Bản "chạy rỗng" của <see cref="SeatHoldAbuseBackgroundService"/> cho host test — cùng lý do
    /// với <see cref="NoopTripGenerationJob"/>. Job này còn nguy hiểm hơn ba job kia khi để chạy
    /// thật trong host test: lượt quét của nó GHI vào bảng AuditLogs, mà rất nhiều test đếm-đúng
    /// trên chính bảng đó (AuditLogCoverageTests, AuditLogWriteApiTests, AuditLogQueryTests…). Một
    /// tài khoản lỡ vượt ngưỡng trong dữ liệu seed của test là dư ra một dòng Warning và làm đỏ
    /// những phép đếm đó — hỏng ngẫu nhiên, đúng kiểu lỗi PR #81.
    /// </summary>
    private sealed class NoopSeatHoldAbuseJob : SeatHoldAbuseBackgroundService
    {
        public NoopSeatHoldAbuseJob(
            IServiceScopeFactory scopeFactory,
            ILogger<SeatHoldAbuseBackgroundService> logger)
            : base(scopeFactory, logger)
        {
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
    }
}
