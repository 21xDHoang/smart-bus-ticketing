using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Ghim chỗ nối job cảnh báo giữ chỗ quá nhiều lần vào app — phần mà
/// <see cref="SeatHoldAbuseServiceTests"/> không nhìn tới.
///
/// Viết ra vì một hosted service đăng ký thiếu KHÔNG có triệu chứng nào ở tầng code: app vẫn build,
/// mọi endpoint vẫn chạy, CI vẫn xanh — chỉ mỗi cái job là im lặng không bao giờ chạy, và không ai
/// biết tài khoản nào đang giữ chỗ hàng loạt. Đúng kiểu lỗi chỉ test mới bắt được. Cùng lối
/// <see cref="SeatHoldExpiryWiringTests"/>.
/// </summary>
public class SeatHoldAbuseWiringTests
{
    [Fact]
    public void Job_duoc_dang_ky_dung_mot_lan_lam_hosted_service()
    {
        using var factory = new TestAppFactory();

        var job = factory.Services
            .GetServices<IHostedService>()
            .OfType<SeatHoldAbuseBackgroundService>();

        // Single chứ không Contains: hai vòng lặp cùng quét sẽ cùng thấy một tài khoản vượt ngưỡng
        // trong cùng một khoảng thời gian. Phép chống ghi trùng trong SeatHoldAbuseService chỉ chặn
        // được lượt quét NỐI TIẾP (nó đọc lại AuditLogs), còn hai vòng lặp chạy song song thì cùng
        // đọc trước khi bên nào kịp ghi — kết quả là hai dòng cảnh báo cho một hành vi.
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
        // SeatHoldAbuseBackgroundService) nên nó chỉ hiện trong log chứ không làm đỏ thứ gì.
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ISeatHoldAbuseService>());
    }
}
