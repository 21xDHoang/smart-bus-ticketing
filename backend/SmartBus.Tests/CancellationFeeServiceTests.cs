using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test service tính phí huỷ — <see cref="CancellationFeeService"/>
/// (US 5, task *"Service tính phí huỷ theo quy định (trước 24h / 12h / 2h)"* — Nguyễn Duy Kiên,
/// dòng 6 Sprint 4).
///
/// Quy định đang ghim ở đây khớp docs/32 §2.1 và mock <c>mockCancellationFee</c> của frontend:
/// vé tạo chưa quá 10 phút được TỰ ĐỘNG huỷ miễn phí (thắng mốc 2h, không thắng "đã khởi hành");
/// quá 10 phút thì ≥24h miễn phí, ≥12h phí 30%, ≥2h phí 50%, dưới 2h KHÔNG được huỷ. Mốc 2h là
/// điều kiện, không phải mức phí thứ ba — vì vậy <c>CancellationFeeQuote.Fee</c> bằng 0 khi bị từ
/// chối KHÔNG có nghĩa là "miễn phí" và mọi ca từ chối đều kiểm cả <c>CanCancel</c> lẫn
/// <c>RefusalReason</c>.
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

    /// <summary>
    /// Báo giá cho một vé khởi hành sau <paramref name="conLaiGiay"/> giây, đặt từ 2 ngày trước —
    /// nằm NGOÀI cửa sổ tự huỷ 10 phút, nên các ca dưới đây kiểm đúng bảng phí 24h/12h/2h.
    /// </summary>
    private CancellationFeeQuote BaoGia(decimal giaVe, int conLaiGiay)
        => _service.GetCancellationFee(
            giaVe, BayGio.AddSeconds(conLaiGiay), BayGio, BayGio.AddDays(-2));

    /// <summary>Báo giá cho vé vừa đặt <paramref name="daDatGiay"/> giây trước — kiểm cửa sổ 10 phút.</summary>
    private CancellationFeeQuote BaoGiaVuaDat(decimal giaVe, int conLaiGiay, int daDatGiay)
        => _service.GetCancellationFee(
            giaVe, BayGio.AddSeconds(conLaiGiay), BayGio, BayGio.AddSeconds(-daDatGiay));

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
        Assert.False(baoGia.AutoApprove);
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
        Assert.False(baoGia.AutoApprove);
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
        Assert.False(baoGia.AutoApprove);
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
        Assert.False(baoGia.AutoApprove);
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
        Assert.False(baoGia.AutoApprove);
        Assert.Equal(0m, baoGia.Fee);
        Assert.Equal("Chuyến xe đã khởi hành, không thể huỷ vé.", baoGia.RefusalReason);
    }

    // ---------------------------------------------------------------------------------------
    // Cửa sổ 10 phút tự huỷ — chốt 17/10/2026, docs/32 §2.1
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5 * 60)]
    [InlineData(10 * 60)]            // đúng 10 phút VẪN trong cửa sổ (mốc tính cả biên)
    public void Trong_10_phut_tu_luc_tao_ve_duoc_tu_dong_huy_mien_phi(int daDatGiay)
    {
        var baoGia = BaoGiaVuaDat(250_000m, 12 * MotGio, daDatGiay);

        Assert.True(baoGia.CanCancel);
        Assert.True(baoGia.AutoApprove);
        Assert.Equal(0m, baoGia.Fee);
        Assert.Null(baoGia.RefusalReason);
    }

    [Fact]
    public void Qua_10_phut_thi_quay_ve_quy_dinh_phi_thuong()
    {
        // 10 phút + 1 giây: hết ân hạn, còn 12h tới giờ chạy nên phí 30% và vẫn phải chờ duyệt.
        var baoGia = BaoGiaVuaDat(250_000m, 12 * MotGio, 10 * 60 + 1);

        Assert.True(baoGia.CanCancel);
        Assert.False(baoGia.AutoApprove);
        Assert.Equal(75_000m, baoGia.Fee);
    }

    [Fact]
    public void Cua_so_10_phut_thang_moc_2_gio()
    {
        // Đặt vé 3 phút trước, chuyến chạy sau 1 giờ: quy định thường từ chối, nhưng ân hạn thắng.
        var baoGia = BaoGiaVuaDat(250_000m, MotGio, 3 * 60);

        Assert.True(baoGia.CanCancel);
        Assert.True(baoGia.AutoApprove);
        Assert.Equal(0m, baoGia.Fee);
        Assert.Null(baoGia.RefusalReason);
    }

    [Fact]
    public void Cua_so_10_phut_khong_thang_chuyen_da_khoi_hanh()
    {
        // Xe đã chạy 1 phút, vé mới tạo 2 phút trước: vẫn từ chối, không tự duyệt.
        var baoGia = BaoGiaVuaDat(250_000m, -60, 2 * 60);

        Assert.False(baoGia.CanCancel);
        Assert.False(baoGia.AutoApprove);
        Assert.Equal("Chuyến xe đã khởi hành, không thể huỷ vé.", baoGia.RefusalReason);
    }

    [Fact]
    public void Moc_tao_ve_o_tuong_lai_van_vao_cua_so()
    {
        // Lệch đồng hồ giữa các máy — coi như vé vừa tạo, không chặn khách vì dữ liệu đồng hồ.
        var baoGia = _service.GetCancellationFee(
            250_000m, BayGio.AddHours(12), BayGio, BayGio.AddMinutes(5));

        Assert.True(baoGia.CanCancel);
        Assert.True(baoGia.AutoApprove);
        Assert.Equal(0m, baoGia.Fee);
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
            () => _service.GetCancellationFee(
                -250_000m, BayGio.AddHours(24), BayGio, BayGio.AddDays(-2)));

        Assert.Equal("ticketPrice", loi.ParamName);
    }
}
