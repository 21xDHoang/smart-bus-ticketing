using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Ghim chỗ nối job thử lại hoàn tiền vào app — phần mà <see cref="RefundRetryServiceTests"/> và
/// <see cref="RefundAttemptServiceTests"/> không nhìn tới (chúng dựng service tay, còn đây là
/// <c>Program.cs</c> thật: nơi đăng ký).
///
/// Viết ra vì một hosted service đăng ký thiếu KHÔNG có triệu chứng nào ở tầng code: app vẫn build,
/// mọi endpoint vẫn chạy, CI vẫn xanh — chỉ mỗi cái job là im lặng không bao giờ chạy, và mọi khoản
/// hoàn đã trượt khỏi tay cổng cứ thế nằm Failed vĩnh viễn chờ quản trị bấm tay (mà màn quản trị
/// dòng 35 cũng mới chỉ gọi được vào chính service này). Cùng lối
/// <see cref="SeatHoldExpiryWiringTests"/> và <see cref="MonthlyPassExpiryWiringTests"/>.
/// </summary>
public class RefundRetryWiringTests
{
    [Fact]
    public void Job_duoc_dang_ky_dung_mot_lan_lam_hosted_service()
    {
        using var factory = new TestAppFactory();

        var job = factory.Services
            .GetServices<IHostedService>()
            .OfType<RefundRetryBackgroundService>();

        // Single chứ không Contains: đăng ký hai lần thì hai vòng lặp cùng quét một bảng — lượt quét
        // thứ hai nhặt đúng các khoản còn Failed tới hạn, nhưng lượt gọi cổng vẫn chỉ ra ngoài một
        // lần cho mỗi khoản (giao thức giành dòng ở docs/35 §4.2 lo phần đó). Khác biệt không nằm ở
        // tiền: nằm ở chỗ đôi khi mỗi khoản bị hai lượt giành nhau và lượt thua ghi log "bỏ qua"
        // như có sự cố, làm log vận hành đọc sai hoàn toàn.
        Assert.Single(job);
    }

    [Fact]
    public void Ruot_job_resolve_duoc_tu_mot_scope_nhu_moi_luot_quet()
    {
        using var factory = new TestAppFactory();
        using var scope = factory.Services.CreateScope();

        // Resolve được nghĩa là AddScoped đã có VÀ AppDbContext dựng được trong scope — đúng thứ
        // SweepOnceAsync làm mỗi lượt quét (nó tự mở scope con cho TỪNG khoản qua IServiceScopeFactory).
        // Thiếu dòng AddScoped thì job ném InvalidOperationException ngay lượt đầu, mà lỗi ở đó bị
        // nuốt (cố ý, xem RefundRetryBackgroundService) nên nó chỉ hiện trong log chứ không làm đỏ
        // thứ gì.
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IRefundRetryService>());
    }

    [Fact]
    public void Ruot_mot_luot_goi_cong_resolve_duoc_tu_scope_con_cua_tung_khoan()
    {
        using var factory = new TestAppFactory();
        using var scope = factory.Services.CreateScope();

        // Chuỗi của MỘT lượt thử lại: RefundRetryService (scope lượt quét) → mở scope con cho khoản →
        // IRefundAttemptService → IRefundGatewayCaller. Đứt bất kỳ mắt nào thì mọi lượt quét có việc
        // đều ném ngay và bị nuốt như trên — job "chạy" mà không bao giờ gọi được cổng.
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IRefundAttemptService>());

        // Cố ý KHÔNG ghim lớp cụ thể (PlaceholderRefundGatewayCaller): dòng 29 (Hiếu) sẽ thay bằng
        // cổng thật — test này giữ chỗ nối còn sống, không giữ tên lớp cắm tạm.
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IRefundGatewayCaller>());
    }
}
