using Microsoft.Extensions.DependencyInjection;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Nối dây thật trong Program.cs — task *"Xử lý idempotency: chống trừ tiền 2 lần khi callback
/// trùng"* — Phùng Duy Hoàng. Một dòng AddScoped nằm CUỐI danh sách đăng ký (sau khối adapter
/// cổng, trước phần hạ tầng JWT/RBAC/CORS); test chạy trên APP THẬT qua <see cref="TestAppFactory"/>
/// để bắt được việc dòng đó bị xoá/dời nhầm.
///
/// Settlement service chỉ cần AppDbContext nên không phải bơm khoá cổng giả như
/// <see cref="PaymentGatewayWiringTests"/>.
/// </summary>
public class PaymentSettlementWiringTests
{
    [Fact]
    public void App_that_resolve_duoc_PaymentSettlementService()
    {
        using var factory = new TestAppFactory();
        using var scope = factory.Services.CreateScope();

        var service = scope.ServiceProvider.GetRequiredService<IPaymentSettlementService>();

        Assert.IsType<PaymentSettlementService>(service);
    }
}
