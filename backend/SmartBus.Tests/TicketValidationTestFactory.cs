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
/// Host test cho các lớp test soát vé (US 15) — bản thu gọn của <see cref="TestAppFactory"/> CỘNG
/// thêm <c>TicketQr:SigningKey</c> (khoá giả rõ ràng của test — luật 2): service ký số chết ngay
/// khi dựng nếu thiếu khoá (docs/30 §3.3).
///
/// Vì sao KHÔNG dùng <c>new TestAppFactory().WithWebHostBuilder(...)</c> như TicketQrWiringTests:
/// ở đó test chỉ resolve service, không đụng CSDL nên không lộ ra — WithWebHostBuilder trả về một
/// factory mà phần <c>ConfigureTestServices</c> của TestAppFactory KHÔNG được áp dụng, nghĩa là
/// DbContext vẫn trỏ PostgreSQL thật và mọi lần seed đi thẳng vào CSDL chung. Factory này chép lại
/// ba mảnh bắt buộc của TestAppFactory: JWT bằng UseSetting, gỡ DbContext Npgsql thay bằng InMemory,
/// và gỡ sáu job nền (gỡ hẳn như <c>NhaMaySqlite</c> — hai file test này không có test wiring nào
/// cần giữ hình dạng đăng ký job, nên không chép sáu lớp noop của TestAppFactory).
/// </summary>
internal sealed class TicketValidationTestFactory : WebApplicationFactory<Program>
{
    private static readonly Type[] JobNen =
    [
        typeof(TripGenerationBackgroundService),
        typeof(MonthlyPassExpiryBackgroundService),
        typeof(SeatHoldExpiryBackgroundService),
        typeof(SeatHoldAbuseBackgroundService),
        typeof(RefundRetryBackgroundService),
        typeof(RefundReconciliationBackgroundService),
    ];

    private readonly string _databaseName = $"smartbus-test-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Cùng lối TestAppFactory: JWT phải có mặt ngay lúc Program.cs dựng service, nên dùng
        // UseSetting chứ không phải ConfigureAppConfiguration.
        builder.UseSetting("Jwt:Issuer", TestAppFactory.Issuer);
        builder.UseSetting("Jwt:Audience", TestAppFactory.Audience);
        builder.UseSetting("Jwt:Key", TestAppFactory.SigningKey);
        builder.UseSetting("Jwt:AccessTokenMinutes", "30");
        builder.UseSetting("Jwt:RefreshTokenDays", "7");
        builder.UseSetting("TicketQr:SigningKey", "khoa-ky-gia-cua-test-dai-hon-32-ky-tu-abc");

        builder.ConfigureTestServices(services =>
        {
            // Gỡ hẳn đăng ký DbContext trỏ PostgreSQL. Phải xoá cả
            // IDbContextOptionsConfiguration vì EF Core 9+ giữ action cấu hình ở đó — để sót thì
            // Npgsql và InMemory cùng được đăng ký và EF sẽ báo lỗi.
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(_databaseName));

            services.AddControllers().AddApplicationPart(typeof(TestAppFactory).Assembly);

            GoJobNen(services);
        });
    }

    /// <summary>Gỡ đúng sáu job nền theo kiểu đăng ký, không gỡ cả <see cref="IHostedService"/>.</summary>
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
