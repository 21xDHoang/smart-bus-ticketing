using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test service tính phí huỷ — <see cref="CancellationFeeService"/>
/// (US 5, task *"Service tính phí huỷ theo quy định (trước 24h / 12h / 2h)"* — Nguyễn Duy Kiên,
/// dòng 6 Sprint 4).
///
/// Quy định đang ghim ở đây khớp docs/32 §2.1 và mock <c>mockCancellationFee</c> của frontend:
/// ≥24h miễn phí, ≥12h phí 30%, ≥2h phí 50%, dưới 2h KHÔNG được huỷ. Mốc 2h là điều kiện, không
/// phải mức phí thứ ba — vì vậy <c>CancellationFeeQuote.Fee</c> bằng 0 khi bị từ chối KHÔNG có nghĩa
/// là "miễn phí" và mọi ca từ chối đều kiểm cả <c>CanCancel</c> lẫn <c>RefusalReason</c>.
///
/// Gọi thẳng service, KHÔNG dựng host: đây là hàm thuần, không có dependency nào để tiêm. Mốc
/// "bây giờ" được ghim sẵn để thử đúng các biên 24h/12h/2h mà không phải chờ đồng hồ thật — cùng
/// lối <see cref="MonthlyPassExpiryServiceTests"/>.
/// </summary>
public class CancellationFeeServiceTests
{
    /// <summary>Mốc "bây giờ" cố định — mọi khoảng cách trong file tính từ mốc này.</summary>
    private static readonly DateTime BayGio = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

    private const int MotGio = 3600;

    private readonly ICancellationFeeService _service = new CancellationFeeService();

    /// <summary>Báo giá cho một vé khởi hành sau <paramref name="conLaiGiay"/> giây.</summary>
    private CancellationFeeQuote BaoGia(decimal giaVe, int conLaiGiay)
        => _service.GetCancellationFee(giaVe, BayGio.AddSeconds(conLaiGiay), BayGio);

    // ---------------------------------------------------------------------------------------
    // Ba mức phí — kể cả đúng biên
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(24 * MotGio)]        // đúng 24h vẫn miễn phí
    [InlineData(30 * MotGio)]
    [InlineData(48 * MotGio)]
    public void Tu_24_gio_tro_len_mien_phi(int conLaiGiay)
    {
        var baoGia = BaoGia(250_000m, conLaiGiay);

        Assert.True(baoGia.CanCancel);
        Assert.Equal(0m, baoGia.Fee);
        Assert.Null(baoGia.RefusalReason);
    }

    [Theory]
    [InlineData(12 * MotGio)]        // đúng 12h đã sang mức 30%
    [InlineData(12 * MotGio + 1)]
    [InlineData(13 * MotGio)]
    [InlineData(23 * MotGio)]
    [InlineData(24 * MotGio - 1)]    // 23h59m59s vẫn dưới 24h
    public void Tu_12_den_duoi_24_gio_phi_30_phan_tram(int conLaiGiay)
    {
        var baoGia = BaoGia(250_000m, conLaiGiay);

        Assert.True(baoGia.CanCancel);
        Assert.Equal(75_000m, baoGia.Fee);
        Assert.Null(baoGia.RefusalReason);
    }

    [Theory]
    [InlineData(2 * MotGio)]         // đúng 2h vẫn được huỷ ở mức 50%
    [InlineData(2 * MotGio + 1)]
    [InlineData(5 * MotGio)]
    [InlineData(12 * MotGio - 1)]    // 11h59m59s vẫn dưới 12h
    public void Tu_2_den_duoi_12_gio_phi_50_phan_tram(int conLaiGiay)
    {
        var baoGia = BaoGia(250_000m, conLaiGiay);

        Assert.True(baoGia.CanCancel);
        Assert.Equal(125_000m, baoGia.Fee);
        Assert.Null(baoGia.RefusalReason);
    }

    // ---------------------------------------------------------------------------------------
    // Dưới 2 giờ — điều kiện thời gian, không phải mức phí
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(2 * MotGio - 1)]     // 1h59m59s
    [InlineData(1 * MotGio)]
    [InlineData(1)]
    public void Duoi_2_gio_khong_duoc_huy(int conLaiGiay)
    {
        var baoGia = BaoGia(250_000m, conLaiGiay);

        Assert.False(baoGia.CanCancel);
        Assert.Equal(0m, baoGia.Fee);   // 0 vì không áp dụng, KHÔNG phải miễn phí
        Assert.Equal(
            "Chuyến khởi hành dưới 2 giờ nên không đủ điều kiện huỷ.",
            baoGia.RefusalReason);
    }

    [Theory]
    [InlineData(0)]                  // đúng giờ khởi hành: cửa sổ huỷ đã đóng
    [InlineData(-1)]
    [InlineData(-1 * MotGio)]
    [InlineData(-24 * MotGio)]
    public void Dung_gio_chay_hoac_da_qua_khong_duoc_huy(int conLaiGiay)
    {
        var baoGia = BaoGia(250_000m, conLaiGiay);

        Assert.False(baoGia.CanCancel);
        Assert.Equal(0m, baoGia.Fee);
        Assert.Equal("Chuyến xe đã khởi hành, không thể huỷ vé.", baoGia.RefusalReason);
    }

    // ---------------------------------------------------------------------------------------
    // Tiền và làm tròn
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(100_001, 13 * MotGio, 30_000)]   // 30.000,3 đồng → 30.000
    [InlineData(100_005, 13 * MotGio, 30_002)]   // 30.001,5 đồng → 30.002 (AwayFromZero)
    [InlineData(100_005, 5 * MotGio, 50_003)]    // 50.002,5 đồng → 50.003
    [InlineData(33_333, 5 * MotGio, 16_667)]     // 16.666,5 đồng → 16.667
    [InlineData(250_000, 5 * MotGio, 125_000)]
    public void Phi_lam_tron_toi_dong_nguyen(int giaVe, int conLaiGiay, int phiMongDoi)
    {
        var baoGia = BaoGia(giaVe, conLaiGiay);

        Assert.True(baoGia.CanCancel);
        Assert.Equal(phiMongDoi, (int)baoGia.Fee);
        Assert.Equal(decimal.Truncate(baoGia.Fee), baoGia.Fee);
    }

    [Fact]
    public void Gia_ve_bang_khong_van_la_mien_phi_chu_khong_phai_tu_choi()
    {
        var baoGia = BaoGia(0m, 5 * MotGio);

        // Ca này phân biệt hai nghĩa của số 0: ở đây CanCancel = true nên 0 đúng là "miễn phí".
        Assert.True(baoGia.CanCancel);
        Assert.Equal(0m, baoGia.Fee);
        Assert.Null(baoGia.RefusalReason);
    }

    [Theory]
    [InlineData(1, 5 * MotGio)]
    [InlineData(999, 5 * MotGio)]
    [InlineData(12_345, 13 * MotGio)]
    [InlineData(250_000, 2 * MotGio)]
    public void Phi_khong_bao_gio_vuot_gia_ve(int giaVe, int conLaiGiay)
    {
        var baoGia = BaoGia(giaVe, conLaiGiay);

        Assert.True(baoGia.CanCancel);
        Assert.InRange(baoGia.Fee, 0m, (decimal)giaVe);
    }

    [Fact]
    public void Phi_tang_dan_khi_gan_gio_chay()
    {
        // Mỗi mốc gần hơn không được rẻ hơn mốc trước — bảo vệ khỏi một lần sửa đổi làm đảo ngưỡng.
        var cacMocGio = new[] { 48, 30, 24, 23, 12, 11, 2 };

        decimal? phiTruoc = null;
        foreach (var gio in cacMocGio)
        {
            var baoGia = BaoGia(250_000m, gio * MotGio);

            Assert.True(baoGia.CanCancel);
            if (phiTruoc.HasValue)
            {
                Assert.True(
                    baoGia.Fee >= phiTruoc.Value,
                    $"Phí ở mốc {gio}h ({baoGia.Fee}) nhỏ hơn mốc gần hơn trước đó ({phiTruoc.Value}).");
            }

            phiTruoc = baoGia.Fee;
        }
    }

    [Fact]
    public void Gia_ve_am_nem_loi_thay_vi_tinh_ra_phi_am()
    {
        var loi = Assert.Throws<ArgumentOutOfRangeException>(
            () => _service.GetCancellationFee(-250_000m, BayGio.AddHours(24), BayGio));

        Assert.Equal("ticketPrice", loi.ParamName);
    }
}
