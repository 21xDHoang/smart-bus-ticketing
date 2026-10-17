using Microsoft.Extensions.DependencyInjection;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test đăng ký DI của bề mặt Web Push (Sprint 4 dòng 42) — ghim đúng chỗ nối ở Program.cs để
/// không có dòng đăng ký nào bị lặp hay bị thay thế im lặng:
///
///   · store là SINGLETON — kho thiết bị phải dùng chung giữa mọi request (bản trong bộ nhớ là
///     kho duy nhất của dữ liệu, hai instance là hai bộ dữ liệu lệch nhau);
///   · client và hai interface đăng ký ĐÚNG MỘT lần — trùng là DI lấy bản sau, im lặng.
///
/// Host dùng <see cref="TestAppFactory"/> (không phải WebPushApiTestFactory — test chỉ resolve,
/// không gọi endpoint, nên không cần khoá VAPID cũng như không cần gỡ sender thật).
/// </summary>
public class WebPushWiringTests
{
    [Fact]
    public void Store_dang_ky_singleton_dung_mot_lan()
    {
        using var factory = new TestAppFactory();

        var registrations = factory.Services.GetServices<IWebPushSubscriptionStore>().ToList();
        Assert.Single(registrations);
        Assert.IsType<InMemoryWebPushSubscriptionStore>(registrations[0]);

        // Singleton: hai lần resolve phải cùng một instance — kho trong bộ nhớ mà tách đôi thì
        // thiết bị đăng ký ở request này không thấy ở request kia.
        using var scope = factory.Services.CreateScope();
        Assert.Same(factory.Services.GetRequiredService<IWebPushSubscriptionStore>(), scope.ServiceProvider.GetRequiredService<IWebPushSubscriptionStore>());
    }

    [Fact]
    public void Sender_va_service_thiet_bi_dang_ky_dung_mot_lan()
    {
        using var factory = new TestAppFactory();

        var senders = factory.Services.GetServices<IWebPushSender>().ToList();
        Assert.Single(senders);
        Assert.IsType<WebPushSender>(senders[0]);

        var deviceServices = factory.Services.GetServices<IWebPushDeviceService>().ToList();
        Assert.Single(deviceServices);
        Assert.IsType<WebPushDeviceService>(deviceServices[0]);
    }

    [Fact]
    public void Client_http_dang_ky_qua_factory_nen_resolve_duoc()
    {
        using var factory = new TestAppFactory();

        using var scope = factory.Services.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<WebPushClient>();

        Assert.NotNull(client);
    }
}
