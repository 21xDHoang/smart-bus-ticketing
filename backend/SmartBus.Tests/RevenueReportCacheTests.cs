using SmartBus.Api.Dtos.Reports;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test lớp đệm kết quả báo cáo doanh thu — <see cref="RevenueReportCache"/>
/// (task *"Cache kết quả báo cáo theo kỳ để tăng tốc"* — Vàng Thị Dăm).
///
/// Không đi qua HTTP: dựng thẳng lớp đệm nên **đặt được TTL** — <see cref="TestAppFactory"/> là
/// <c>internal sealed</c> và không có chỗ cấu hình tham số cho service, nên muốn thử nhánh hết
/// hạn thì phải dựng trực tiếp. Phần "đệm có thật sự đứng trước endpoint không" nằm ở
/// <see cref="RevenueReportCacheApiTests"/> (đi qua HTTP).
///
/// Cố ý KHÔNG có test chờ đồng hồ (kiểu <c>Task.Delay</c> quá TTL): nhánh hết hạn thử bằng
/// <see cref="TimeSpan.Zero"/> — vừa tất định vừa không chập chờn khi máy chậm.
/// </summary>
public class RevenueReportCacheTests
{
    [Fact]
    public void Luu_roi_thi_luot_sau_trung_dem()
    {
        var cache = new RevenueReportCache();
        var baoCao = BaoCao(10_000m);
        const string key = "day|2026-10-14|2026-10-14|-|-";

        // Lượt đầu chưa có gì — phải hỏi CSDL.
        Assert.Null(cache.Lookup(key));

        cache.Store(key, baoCao);

        // Lượt sau trả đúng đối tượng đã đệm, không chạm CSDL.
        Assert.Same(baoCao, cache.Lookup(key));
        Assert.Equal(1, cache.Hits);
        Assert.Equal(1, cache.Misses);
    }

    [Fact]
    public void Khoa_khac_nhau_khong_dung_chung_ket_qua()
    {
        var cache = new RevenueReportCache();
        var cuaA = BaoCao(10_000m);
        var cuaB = BaoCao(99_000m);

        cache.Store("A", cuaA);
        cache.Store("B", cuaB);

        // Hai kỳ/tuyến khác nhau là hai truy vấn khác nhau — không được trả lẫn kết quả.
        Assert.Same(cuaA, cache.Lookup("A"));
        Assert.Same(cuaB, cache.Lookup("B"));
    }

    [Fact]
    public void Het_han_thi_tra_miss_va_khong_giu_khoa()
    {
        // TTL bằng 0 = hết hạn ngay: mọi lượt hỏi sau Store đều rơi vào nhánh hết hạn.
        var cache = new RevenueReportCache(TimeSpan.Zero);
        const string key = "khoa-het-han";

        cache.Store(key, BaoCao(10_000m));

        Assert.Null(cache.Lookup(key));
        Assert.Equal(0, cache.Hits);
        Assert.Equal(1, cache.Misses);

        // Khoá hết hạn bị xoá khỏi từ điển — không chiếm chỗ tới khi đầy trần.
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Store_lai_cung_khoa_thi_lam_moi_ket_qua()
    {
        var cache = new RevenueReportCache();
        const string key = "day|2026-10-14|2026-10-14|-|-";

        cache.Store(key, BaoCao(10_000m));
        cache.Store(key, BaoCao(25_000m));

        Assert.Equal(25_000m, cache.Lookup(key)!.TotalRevenue);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void Cham_tran_thi_khong_nhan_them_khoa_moi()
    {
        var cache = new RevenueReportCache();
        var baoCao = BaoCao(10_000m);

        for (var i = 0; i < IRevenueReportCache.MaxEntries; i++)
        {
            cache.Store($"khoa-{i}", baoCao);
        }

        Assert.Equal(IRevenueReportCache.MaxEntries, cache.Count);

        // Đệm đầy và không có khoá nào hết hạn → khoá mới bị bỏ, không đuổi khoá cũ.
        cache.Store("khoa-moi", baoCao);

        Assert.Equal(IRevenueReportCache.MaxEntries, cache.Count);
        Assert.Null(cache.Lookup("khoa-moi"));
    }

    [Fact]
    public void Cham_tran_thi_don_khoa_het_han_de_nhan_khoa_moi()
    {
        // TTL bằng 0: mọi khoá đã lấp trần đều hết hạn, nên lượt Store khoá mới phải dọn ra chỗ.
        var cache = new RevenueReportCache(TimeSpan.Zero);
        var baoCao = BaoCao(10_000m);

        for (var i = 0; i < IRevenueReportCache.MaxEntries; i++)
        {
            cache.Store($"khoa-{i}", baoCao);
        }

        cache.Store("khoa-moi", baoCao);

        // Không kiểm Lookup ở đây: TTL bằng 0 nên khoá vừa thêm cũng hết hạn ngay. Count = 1
        // chứng minh SweepExpired đã dọn 200 khoá cũ rồi mới nhận khoá mới.
        Assert.Equal(1, cache.Count);
    }

    private static RevenueReportResponse BaoCao(decimal totalRevenue) => new()
    {
        Granularity = "day",
        From = new DateOnly(2026, 10, 14),
        To = new DateOnly(2026, 10, 14),
        TotalRevenue = totalRevenue,
        TotalPayments = 1,
        Buckets =
        [
            new RevenueBucketResponse
            {
                PeriodStart = "2026-10-14",
                Revenue = totalRevenue,
                Payments = 1,
            },
        ],
    };
}
