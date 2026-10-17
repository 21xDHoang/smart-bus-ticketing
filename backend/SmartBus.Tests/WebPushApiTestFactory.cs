using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SmartBus.Api.Data;
using SmartBus.Api.Services;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Host test cho các test bề mặt Web Push (Sprint 4 dòng 42) — bản thu gọn của
/// <see cref="TestAppFactory"/> (cùng lối <see cref="TicketValidationTestFactory"/>) CỘNG thêm:
///
///   · <c>WebPush:*</c> qua UseSetting — cặp khoá GIẢ rõ ràng của test (luật 2: khoá giả in ngay
///     trong file test thì không có bí mật nào bị lộ);
///   · <see cref="IWebPushSender"/> bị thay bằng <see cref="GhiNhanWebPushSender"/> — test bề mặt
///     không được gọi mạng thật tới push service; bản ghi nhận để lại danh sách tin đã "gửi" cho
///     test đọc lại.
///
/// Vì sao KHÔNG dùng <c>new TestAppFactory().WithWebHostBuilder(...)</c>: phần ConfigureTestServices
/// của TestAppFactory sẽ KHÔNG được áp dụng, DbContext trỏ PostgreSQL thật và mọi lần seed đi thẳng
/// vào CSDL chung (cạm bẫy đã ghi ở TicketValidationTestFactory).
/// </summary>
internal sealed class WebPushApiTestFactory : WebApplicationFactory<Program>
{
    /// <summary>Khoá công khai VAPID của test — giá trị giả, dùng được cho mọi phép kiểm tra hình dạng.</summary>
    public const string PublicKey = "BFKkHFhOr1Z6OZH2yVJioAulrtRcOcPKdIvP_R8muDo0vr8ChsNLegy-jh6JeVtW2V-3vdlWpGnwz0d3vihu6n4";

    /// <summary>Khoá riêng VAPID của test (PKCS#8) — giá trị giả, chỉ để ký trong test.</summary>
    public const string PrivateKey = "MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQg-MppzMm5rod28JK9AVahSuzBcnxnNb5UOb3TLGLBcaShRANCAARSpBxYTq9WejmR9slSYqALpa7UXDnDynSLz_0fJrg6NL6_AobDS3oMvo4eiXlbVtlft73ZVqRp8M9Hd74obup-";

    public const string Subject = "mailto:test@smartbus.example";

    private static readonly Type[] JobNen =
    [
        typeof(TripGenerationBackgroundService),
        typeof(MonthlyPassExpiryBackgroundService),
        typeof(SeatHoldExpiryBackgroundService),
        typeof(SeatHoldAbuseBackgroundService),
        typeof(RefundRetryBackgroundService),
        typeof(RefundReconciliationBackgroundService),
        typeof(ExportJobBackgroundService),
    ];

    private readonly string _databaseName = $"smartbus-test-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Cùng lối TestAppFactory: JWT phải có mặt ngay lúc Program.cs dựng service, nên dùng
        // UseSetting chứ không phải ConfigureAppConfiguration. Cặp khoá VAPID giả đặt ở đây để
        // GET /api/web-push/vapid-public-key có giá trị xác định để so.
        builder.UseSetting("Jwt:Issuer", TestAppFactory.Issuer);
        builder.UseSetting("Jwt:Audience", TestAppFactory.Audience);
        builder.UseSetting("Jwt:Key", TestAppFactory.SigningKey);
        builder.UseSetting("Jwt:AccessTokenMinutes", "30");
        builder.UseSetting("Jwt:RefreshTokenDays", "7");
        builder.UseSetting("WebPush:PublicKey", PublicKey);
        builder.UseSetting("WebPush:PrivateKey", PrivateKey);
        builder.UseSetting("WebPush:Subject", Subject);

        builder.ConfigureTestServices(services =>
        {
            // Gỡ hẳn đăng ký DbContext trỏ PostgreSQL. Phải xoá cả
            // IDbContextOptionsConfiguration vì EF Core 9+ giữ action cấu hình ở đó — để sót thì
            // Npgsql và InMemory cùng được đăng ký và EF sẽ báo lỗi.
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(_databaseName));

            services.AddControllers().AddApplicationPart(typeof(TestAppFactory).Assembly);

            // Thay chỗ gửi tin thật (mạng tới push service) bằng bản ghi nhận — test bề mặt chỉ
            // quan tâm "đã gọi gửi đúng thiết bị, đúng nội dung", không quan tâm mạng. Đăng ký cả
            // lớp cụ thể để test lấy thẳng danh sách tin đã "gửi" qua factory.
            services.RemoveAll<IWebPushSender>();
            services.AddSingleton<GhiNhanWebPushSender>();
            services.AddSingleton<IWebPushSender>(provider => provider.GetRequiredService<GhiNhanWebPushSender>());

            GoJobNen(services);
        });
    }

    /// <summary>Gỡ đúng bảy job nền theo kiểu đăng ký, không gỡ cả <see cref="IHostedService"/>.</summary>
    private static void GoJobNen(IServiceCollection services)
    {
        var canGo = services
            .Where(d => d.ServiceType == typeof(IHostedService)
                && d.ImplementationType is not null
                && JobNen.Contains(d.ImplementationType))
            .ToList();

        foreach (var descriptor in canGo)
        {
            services.Remove(descriptor);
        }
    }

    /// <summary>Bản ghi nhận đã "gửi" — gom vào service trong host test.</summary>
    public GhiNhanWebPushSender DaGui => Services.GetRequiredService<GhiNhanWebPushSender>();

    /// <summary>Sinh access token bằng chính <see cref="ITokenService"/> của app — chép từ TestAppFactory.</summary>
    public string CreateTokenFor(UserEntity user)
    {
        using var scope = Services.CreateScope();

        return scope.ServiceProvider.GetRequiredService<ITokenService>().CreateAccessToken(user).Token;
    }

    /// <summary>Ghi dữ liệu vào CSDL test. Luôn lưu lại vì middleware đọc ở scope khác — chép từ TestAppFactory.</summary>
    public async Task SeedAsync(Action<AppDbContext> seed)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        seed(db);

        await db.SaveChangesAsync();
    }
}

/// <summary>
/// <see cref="IWebPushSender"/> giả cho host test — không gọi mạng, chỉ ghi lại các lượt gửi vào
/// danh sách để test đọc. Đăng ký theo cách "sau cùng thắng" của DI: TestAppFactory đã đăng ký
/// bản thật, factory test gỡ rồi thêm bản này.
/// </summary>
public sealed class GhiNhanWebPushSender : IWebPushSender
{
    private readonly object _lock = new();
    private readonly List<(Guid UserId, WebPushMessage Message)> _guiChoNguoi = [];
    private readonly List<(Guid UserId, Guid DeviceId, WebPushMessage Message)> _guiChoThietBi = [];

    /// <summary>Mọi lượt gửi theo người dùng, theo thứ tự gọi.</summary>
    public IReadOnlyList<(Guid UserId, WebPushMessage Message)> DaGuiChoNguoi
    {
        get
        {
            lock (_lock)
            {
                return [.. _guiChoNguoi];
            }
        }
    }

    /// <summary>Mọi lượt gửi theo thiết bị, theo thứ tự gọi.</summary>
    public IReadOnlyList<(Guid UserId, Guid DeviceId, WebPushMessage Message)> DaGuiChoThietBi
    {
        get
        {
            lock (_lock)
            {
                return [.. _guiChoThietBi];
            }
        }
    }

    public Task SendToUserAsync(Guid userId, WebPushMessage message, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _guiChoNguoi.Add((userId, message));
        }

        return Task.CompletedTask;
    }

    public Task<bool> SendToDeviceAsync(Guid userId, Guid deviceId, WebPushMessage message, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _guiChoThietBi.Add((userId, deviceId, message));
        }

        return Task.FromResult(true);
    }
}
