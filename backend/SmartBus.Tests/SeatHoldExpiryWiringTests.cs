using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Ghim chỗ nối job quét hold hết hạn vào app — phần mà <see cref="SeatHoldExpiryServiceTests"/>
/// không nhìn tới.
///
/// Viết ra vì một hosted service đăng ký thiếu KHÔNG có triệu chứng nào ở tầng code: app vẫn build,
/// mọi endpoint vẫn chạy, CI vẫn xanh — chỉ mỗi cái job là im lặng không bao giờ chạy, và ghế của
/// khách bỏ ngang thao tác thanh toán cứ thế bị chặn mãi. Đúng kiểu lỗi chỉ test mới bắt được. Cùng
/// lối <see cref="MonthlyPassExpiryWiringTests"/> và <see cref="TripSearchCacheApiTests"/>.
/// </summary>
public class SeatHoldExpiryWiringTests
{
    [Fact]
    public void Job_duoc_dang_ky_dung_mot_lan_lam_hosted_service()
    {
        using var factory = new TestAppFactory();

        var job = factory.Services
            .GetServices<IHostedService>()
            .OfType<SeatHoldExpiryBackgroundService>();

        // Single chứ không Contains: đăng ký hai lần thì hai vòng lặp cùng quét một bảng — và ở bảng
        // chỉ-ghi-thêm này hậu quả nặng hơn hai job kia, vì bản thứ hai đâm vào unique index
        // (SeatHoldId, Action) rồi ném DbUpdateException mỗi lượt quét có việc (lỗi bị nuốt, chỉ
        // hiện trong log).
        Assert.Single(job);
    }

    [Fact]
    public void Ruot_job_resolve_duoc_tu_mot_scope_nhu_moi_luot_quet()
    {
        using var factory = new TestAppFactory();
        using var scope = factory.Services.CreateScope();

        // Resolve được nghĩa là AddScoped đã có VÀ AppDbContext dựng được trong scope — đúng thứ
        // SweepOnceAsync làm mỗi lượt quét. Thiếu dòng AddScoped thì job ném
        // InvalidOperationException ngay lượt đầu, mà lỗi ở đó bị nuốt (cố ý, xem
        // SeatHoldExpiryBackgroundService) nên nó chỉ hiện trong log chứ không làm đỏ thứ gì.
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ISeatHoldExpiryService>());
    }
}
