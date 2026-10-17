using Microsoft.Extensions.DependencyInjection;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Ghim chỗ nối service tính phí huỷ vào DI — phần mà <see cref="CancellationFeeServiceTests"/>
/// không nhìn tới.
///
/// Viết ra vì thiếu dòng <c>AddScoped&lt;ICancellationFeeService, CancellationFeeService&gt;</c>
/// trong <c>Program.cs</c> không có triệu chứng nào ở tầng code: app vẫn build, mọi endpoint hiện có
/// vẫn chạy, CI vẫn xanh — chỉ tới lúc Hiếu nối <c>POST /cancellation-requests</c> mới nổ
/// <c>InvalidOperationException</c> khi resolve. Cùng lối
/// <see cref="MonthlyPassExpiryWiringTests"/>.
/// </summary>
public class CancellationFeeWiringTests
{
    [Fact]
    public void Service_tinh_phi_resolve_duoc_tu_mot_scope()
    {
        using var factory = new TestAppFactory();
        using var scope = factory.Services.CreateScope();

        var service = scope.ServiceProvider.GetRequiredService<ICancellationFeeService>();

        // Đúng lớp cài đặt, không phải một bản giả nào được đăng ký đè.
        Assert.IsType<CancellationFeeService>(service);

        // Chạy thật một phát để chắc chắn đây là service của quy định 24h/12h/2h, không phải một
        // đăng ký nhầm vào interface khác cùng tên.
        var bayGio = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
        var baoGia = service.GetCancellationFee(
            250_000m, bayGio.AddHours(13), bayGio, bayGio.AddDays(-2));

        Assert.True(baoGia.CanCancel);
        Assert.False(baoGia.AutoApprove);
        Assert.Equal(75_000m, baoGia.Fee);
    }
}
