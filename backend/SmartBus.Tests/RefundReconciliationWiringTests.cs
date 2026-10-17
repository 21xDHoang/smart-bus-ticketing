using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Ghim chỗ nối bước đối soát hoàn tiền vào app — phần mà
/// <see cref="RefundReconciliationServiceTests"/> và
/// <see cref="RefundReconciliationNotifierTests"/> không nhìn tới (chúng dựng service tay, còn đây là
/// <c>Program.cs</c> thật: nơi đăng ký).
///
/// Viết ra vì một hosted service đăng ký thiếu KHÔNG có triệu chứng nào ở tầng code: app vẫn build,
/// mọi endpoint vẫn chạy, CI vẫn xanh — chỉ mỗi việc đối soát là im lặng không bao giờ chạy, và các
/// khoản hoàn có thể đã rời cổng mà chưa lật trạng thái cứ thế nằm chờ khách khiếu nại. Cùng lối
/// <see cref="RefundRetryWiringTests"/> và <see cref="SeatHoldExpiryWiringTests"/>.
/// </summary>
public class RefundReconciliationWiringTests
{
    [Fact]
    public void Job_doi_soat_duoc_dang_ky_dung_mot_lan_lam_hosted_service()
    {
        using var factory = new TestAppFactory();

        var job = factory.Services
            .GetServices<IHostedService>()
            .OfType<RefundReconciliationBackgroundService>();

        // Single chứ không Contains: hai bản chạy thì hai lượt đối soát cùng đêm, và dù notifier có
        // chốt chống trùng theo ngày, lượt thứ hai vẫn là một lượt quét toàn bảng không cần thiết —
        // cùng lý do đã ghi ở RefundRetryWiringTests.
        Assert.Single(job);
    }

    [Fact]
    public void Ruot_job_resolve_duoc_tu_mot_scope_nhu_moi_luot_chay()
    {
        using var factory = new TestAppFactory();
        using var scope = factory.Services.CreateScope();

        // RunOnceAsync mở đúng một scope rồi resolve hai thứ này. Thiếu một dòng AddScoped thì lượt
        // chạy ném InvalidOperationException — mà lỗi ở đó bị nuốt (cố ý, xem job), nên nó chỉ hiện
        // trong log chứ không làm đỏ thứ gì.
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IRefundReconciliationService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IRefundReconciliationNotifier>());
    }

    [Fact]
    public void Nguon_sao_ke_mac_dinh_la_khong_kha_dung_chu_khong_phai_sao_ke_rong()
    {
        using var factory = new TestAppFactory();
        using var scope = factory.Services.CreateScope();

        // Bản chạy hiện tại chưa có cổng nào trả sao kê HOÀN TIỀN (xem doc-comment
        // IRefundStatementSource). Đăng ký nhầm một lớp trả sao kê rỗng sẽ biến MỌI khoản hoàn thành
        // công hằng ngày thành "không có trên sao kê" — cảnh báo giả hàng loạt, đúng thứ seam này
        // tồn tại để tránh.
        Assert.IsType<UnavailableRefundStatementSource>(
            scope.ServiceProvider.GetRequiredService<IRefundStatementSource>());
    }
}
