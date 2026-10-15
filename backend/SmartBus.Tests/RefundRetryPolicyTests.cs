using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Ghim nấc thang thử lại hoàn tiền (task *"Xử lý hoàn tiền thất bại + retry theo lịch"*,
/// US 8): <see cref="RefundRetryPolicy.Delays"/> và <see cref="RefundRetryPolicy.NextRetryAt"/>.
///
/// Vì sao ghim CHÍNH XÁC từng con số: mốc hẹn là dữ liệu bền nằm trong CSDL (<c>Refunds.NextRetryAt</c>,
/// index IX_Refunds_Status_NextRetryAt). Đổi thang là đổi hành vi của những dòng ĐÃ ghi, không phải
/// của riêng code — không có test nào khác bắt được việc ai đó lỡ sửa 720 thành 72.
///
/// Bài học từ <see cref="SeatHoldExpiryFlowApiTests"/>: đây là hàm THUẦN, không dựng host, không
/// đồng hồ thật — mọi mốc đi qua tham số <c>now</c> nên không có gì chập chờn.
/// </summary>
public class RefundRetryPolicyTests
{
    /// <summary>Mốc "hỏng lúc" cố định — mọi assert đều là mốc này cộng một nấc thang.</summary>
    private static readonly DateTime HongLuc = new(2026, 10, 14, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Thang_thu_lai_dung_5_nac_5_30_120_720_1440_phut()
    {
        // Cộng dồn tới nấc cuối ≈ 38,6 giờ — đủ dài để cổng kịp xử lý sự cố, đủ ngắn để khoản hoàn
        // không nằm im cả tuần trước khi quản trị phải bấm tay.
        Assert.Equal(
            new[]
            {
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(30),
                TimeSpan.FromMinutes(120),
                TimeSpan.FromMinutes(720),
                TimeSpan.FromMinutes(1440),
            },
            RefundRetryPolicy.Delays);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 30)]
    [InlineData(2, 120)]
    [InlineData(3, 720)]
    [InlineData(4, 1440)]
    public void Con_nac_thi_hen_dung_moc_ke_tiep(int soLanDaThu, int soPhutHen)
    {
        var mocHen = RefundRetryPolicy.NextRetryAt(HongLuc, soLanDaThu);

        Assert.Equal(HongLuc.AddMinutes(soPhutHen), mocHen);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(99)]
    public void Het_thang_thi_khong_hen_nua(int soLanDaThu)
    {
        // null = hết lịch tự động, KHÔNG phải "hỏng không rõ" — quản trị vẫn bấm thử tay được
        // (ManualRetry không đọc NextRetryAt, xem RefundAttemptServiceTests).
        Assert.Null(RefundRetryPolicy.NextRetryAt(HongLuc, soLanDaThu));
    }

    [Fact]
    public void Moc_hen_tinh_tu_lan_hong_gan_nhat_chu_khong_phai_tu_luc_tao()
    {
        // Hỏng lúc nào hẹn từ lúc đó: lượt thử lại thứ hai hẹn 30 phút sau LẦN HỎNG THỨ HAI,
        // không phải 30 phút sau lần tạo khoản. Đọc nhầm thành "từ lúc tạo" là mốc trôi dần về quá khứ.
        var hongLanHai = HongLuc.AddHours(3);

        Assert.Equal(hongLanHai.AddMinutes(30), RefundRetryPolicy.NextRetryAt(hongLanHai, 1));
    }
}
