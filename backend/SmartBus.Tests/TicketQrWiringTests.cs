using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Chạy trên app THẬT (TestAppFactory) — ghim hai dòng đăng ký DI trong Program.cs: xoá dòng
/// Configure/AddScoped là test này đỏ ngay, không phải chờ tới lúc mở endpoint mới biết.
/// Khoá giả rõ ràng của test — không phải khoá thật (luật 2).
/// </summary>
public class TicketQrWiringTests
{
    [Fact]
    public void App_that_resolve_duoc_ITicketQrService()
    {
        using var factory = NewFactory();

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITicketQrService>();

        Assert.IsType<TicketQrService>(service);
    }

    [Fact]
    public void App_that_sinh_va_kiem_duoc_ma_that()
    {
        using var factory = NewFactory();

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITicketQrService>();

        // Đi trọn một vòng đời qua cấu hình thật của app: sinh mã rồi kiểm lại chính mã đó.
        var id = Guid.NewGuid();
        var code = service.GenerateCode(id);

        Assert.True(service.TryVerify(code, out var verifiedId));
        Assert.Equal(id, verifiedId);
    }

    private static WebApplicationFactory<Program> NewFactory()
        => new TestAppFactory().WithWebHostBuilder(builder =>
            builder.UseSetting("TicketQr:SigningKey", "khoa-ky-gia-cua-test-dai-hon-32-ky-tu-abc"));
}
