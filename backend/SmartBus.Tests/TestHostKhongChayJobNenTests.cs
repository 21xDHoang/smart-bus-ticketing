using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Ghim "host test KHÔNG chạy job nền thật" — tấm chắn cho lỗi CI ngày 03/10/2026 (PR #81).
///
/// Hôm đó 3 test của <see cref="TripSearchCacheApiTests"/> đỏ dù nhánh chỉ sửa frontend: job sinh
/// chuyến THẬT chạy ngay trong host test (Program.cs đăng ký AddHostedService vô điều kiện, còn
/// <see cref="TestAppFactory"/> thời điểm ấy không gỡ), lượt quét đầu ĐUA với lúc test seed dữ
/// liệu — thắng đua thì nó nhân bản chuyến mẫu sang 7 ngày tới và làm vỡ các assert đếm-đúng. Log
/// CI khớp từng con số: "Đã sinh 14 chuyến" ×2, "Đã sinh 7 chuyến" ứng đúng 16/16/8.
///
/// <see cref="TestAppFactory"/> nay thay cả 3 job bằng bản noop (xem ThayJobNenBangBanNoop ở đó). File
/// này khoá lại điều đó để ai lỡ gỡ bản noop sẽ thấy đỏ NGAY TẠI ĐÂY, thay vì nhận một CI đỏ ngẫu
/// nhiên ở nơi khác rồi phải truy lại từ log.
///
/// Cố ý KHÔNG kiểm "đăng ký còn nguyên không" — đó là việc của <see cref="TripGenerationWiringTests"/>,
/// <see cref="MonthlyPassExpiryWiringTests"/> và <see cref="SeatHoldExpiryWiringTests"/> (chúng vẫn
/// xanh vì bản noop là subclass của job thật, đăng ký giữ nguyên hình dạng). File này chỉ kiểm phần
/// mà chúng không thấy: lớp THẬT không còn là thứ được host khởi động.
/// </summary>
public class TestHostKhongChayJobNenTests
{
    [Fact]
    public void Job_sinh_chuyen_trong_host_test_la_ban_noop()
    {
        using var factory = new TestAppFactory();

        var job = factory.Services
            .GetServices<IHostedService>()
            .OfType<TripGenerationBackgroundService>()
            .Single();

        Assert.NotEqual(typeof(TripGenerationBackgroundService), job.GetType());
    }

    [Fact]
    public void Job_ve_thang_trong_host_test_la_ban_noop()
    {
        using var factory = new TestAppFactory();

        var job = factory.Services
            .GetServices<IHostedService>()
            .OfType<MonthlyPassExpiryBackgroundService>()
            .Single();

        Assert.NotEqual(typeof(MonthlyPassExpiryBackgroundService), job.GetType());
    }

    [Fact]
    public void Job_quet_hold_het_han_trong_host_test_la_ban_noop()
    {
        using var factory = new TestAppFactory();

        var job = factory.Services
            .GetServices<IHostedService>()
            .OfType<SeatHoldExpiryBackgroundService>()
            .Single();

        Assert.NotEqual(typeof(SeatHoldExpiryBackgroundService), job.GetType());
    }
}
