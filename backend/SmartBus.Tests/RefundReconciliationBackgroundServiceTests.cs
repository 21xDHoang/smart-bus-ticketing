using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test phần THUẦN của job đối soát hoàn tiền — <see cref="RefundReconciliationBackgroundService"/>.
///
/// Chỉ phép tính độ trễ được test ở đây: nó là hàm public, không chạm đồng hồ thật, nên ghim được
/// đúng ca biên (đúng mốc chạy, trước mốc, sau mốc). Vòng lặp và một lượt chạy đầy đủ cần dựng host
/// + chờ 24 giờ nên không test trực tiếp — hai mảnh ruột của nó đã có test riêng
/// (<see cref="RefundReconciliationServiceTests"/>, <see cref="RefundReconciliationNotifierTests"/>),
/// còn chỗ nối DI nằm ở <see cref="RefundReconciliationWiringTests"/>.
/// </summary>
public class RefundReconciliationBackgroundServiceTests
{
    [Fact]
    public void Moc_chay_la_00_05_utc()
    {
        // 00:05 UTC = 07:05 giờ Việt Nam — đầu ngày làm việc, sau khi giao dịch hôm trước đã chốt.
        Assert.Equal(new TimeOnly(0, 5), RefundReconciliationBackgroundService.RunAtUtc);
    }

    [Fact]
    public void Truoc_moc_chay_thi_cho_toi_moc_trong_ngay()
    {
        var utcNow = new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal(TimeSpan.FromMinutes(5), RefundReconciliationBackgroundService.DelayUntilNextRun(utcNow));
    }

    [Fact]
    public void Sau_moc_chay_thi_cho_toi_moc_ngay_mai()
    {
        var utcNow = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

        // Không neo vào lúc app khởi động (như PeriodicTimer): deploy lại lúc 15:00 sẽ đẩy lượt đối
        // soát sang 15:00 hôm sau, trong khi mốc phải luôn là 00:05 UTC.
        Assert.Equal(
            TimeSpan.FromHours(12) + TimeSpan.FromMinutes(5),
            RefundReconciliationBackgroundService.DelayUntilNextRun(utcNow));
    }

    [Fact]
    public void Dung_moc_chay_thi_cho_tron_mot_ngay_chu_khong_tra_0()
    {
        var utcNow = new DateTime(2026, 10, 17, 0, 5, 0, DateTimeKind.Utc);

        // Trả 0 thì vòng lặp chạy hai lượt trong cùng một giây — hai lượt đối soát, hai lượt quét
        // toàn bảng, và một cuộc đua với chính notifier.
        Assert.Equal(TimeSpan.FromDays(1), RefundReconciliationBackgroundService.DelayUntilNextRun(utcNow));
    }
}
