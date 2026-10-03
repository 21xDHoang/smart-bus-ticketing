using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Ghim chỗ nối job quét vé tháng hết hạn vào app — phần mà
/// <see cref="MonthlyPassExpiryServiceTests"/> không nhìn tới.
///
/// Viết ra vì một hosted service đăng ký thiếu KHÔNG có triệu chứng nào ở tầng code: app vẫn build,
/// mọi endpoint vẫn chạy, CI vẫn xanh — chỉ mỗi cái job là im lặng không bao giờ chạy, và cột Status
/// cứ thế sai dần. Đúng kiểu lỗi chỉ test mới bắt được. Cùng lối
/// <see cref="TripSearchCacheApiTests"/> ghim chỗ nối bộ đệm vào <c>GET /trips/search</c>.
/// </summary>
public class MonthlyPassExpiryWiringTests
{
    [Fact]
    public void Job_duoc_dang_ky_dung_mot_lan_lam_hosted_service()
    {
        using var factory = new TestAppFactory();

        var job = factory.Services
            .GetServices<IHostedService>()
            .OfType<MonthlyPassExpiryBackgroundService>();

        // Single chứ không Contains: đăng ký hai lần thì hai vòng lặp cùng quét một bảng, và không có
        // gì khác trong hệ thống tố cáo chuyện đó.
        Assert.Single(job);
    }

    [Fact]
    public void Ruot_job_resolve_duoc_tu_mot_scope_nhu_moi_luot_quet()
    {
        using var factory = new TestAppFactory();
        using var scope = factory.Services.CreateScope();

        // Resolve được nghĩa là AddScoped đã có VÀ AppDbContext dựng được trong scope — đúng thứ
        // SweepOnceAsync làm mỗi lượt quét. Thiếu dòng AddScoped thì job ném
        // InvalidOperationException ngay lượt đầu, mà lỗi ở đó bị nuốt (cố ý, xem quyết định (2) ở
        // đầu MonthlyPassExpiryBackgroundService) nên nó chỉ hiện trong log chứ không làm đỏ thứ gì.
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IMonthlyPassExpiryService>());
    }
}
