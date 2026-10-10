using System.Text;
using System.Text.RegularExpressions;
using SmartBus.Api.Services;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

namespace SmartBus.Tests;

/// <summary>
/// Test cho <see cref="TicketPdfService"/> và dữ liệu vào vé <see cref="TicketPdfModel"/> —
/// sinh file PDF vé có mã QR (US 4, Sprint 3, task *"Sinh file PDF vé có mã QR + gửi kèm qua email"*
/// — Phùng Duy Hoàng).
///
/// Chốt chính là phép GHI RỒI ĐỌC LẠI: đọc ngược toạ độ ô đen trong SVG do service dệt ra, dựng
/// lại ảnh điểm, rồi cho ZXing GIẢI MÃ — chứng minh "máy quét đọc vé của mình ra đúng mã",
/// không chỉ "file PDF mở được". Kèm đó là các ghim: vùng lặng 4 module, kích thước ma trận hợp lệ,
/// và đầu–đuôi file PDF.
///
/// Không cần CSDL/HTTP: service thuần, font Roboto nhúng trong assembly. Cũng vì thuần nên cùng
/// đầu vào cho cùng đầu ra — có test ghim luôn tính tất định.
/// </summary>
public class TicketPdfServiceTests
{
    /// <summary>Mã QR đúng khuôn thật (106 ký tự: "SBT1:" + Guid "D" + "." + blob Base64Url 64 ký tự).</summary>
    private static readonly string SampleCode =
        $"SBT1:{Guid.Parse("3f8c1d2e-4b5a-4c6d-8e7f-9012345678ab"):D}."
        + new string('A', 64);

    // ---- Ghi rồi đọc lại (máy quét) ----------------------------------------

    [Fact]
    public void BuildQrSvg_giai_ma_lai_ra_dung_ma_dau_vao()
    {
        Assert.Equal(106, SampleCode.Length);   // khuôn thật — lệch là test lệch đời thật

        var result = DecodeQr(TicketPdfService.BuildQrSvg(SampleCode));

        Assert.NotNull(result);
        Assert.Equal(BarcodeFormat.QR_CODE, result!.BarcodeFormat);
        Assert.Equal(SampleCode, result.Text);
    }

    [Fact]
    public void BuildQrSvg_giu_dung_vung_lang_4_module_va_kich_thuoc_hop_le()
    {
        var (width, height, dark) = ParseSvg(TicketPdfService.BuildQrSvg(SampleCode));

        Assert.Equal(width, height);
        Assert.True(width >= 29, $"Ma trận QR nhỏ bất thường: {width}×{height}");
        // 4 × phiên bản + 17 module, cộng 2 × 4 vùng lặng = 4 × phiên bản + 25.
        Assert.Equal(1, width % 4);

        // Không ô đen nào được lọt vào dải 4 module sát biên — máy quét cần khoảng trắng đó để
        // tách mã khỏi nền. Đây là ghim cho EncodeHintType.MARGIN.
        Assert.All(dark, m => Assert.True(
            m.X >= 4 && m.X < width - 4 && m.Y >= 4 && m.Y < height - 4,
            $"Ô đen lọt vào vùng lặng tại ({m.X},{m.Y})"));

        // Góc trên-trái ô định vị (finder pattern) luôn đen — ghim ma trận thẳng hàng đúng 4 module
        // lặng, không lệch một ô.
        Assert.Contains((4, 4), dark);
    }

    [Fact]
    public void BuildQrSvg_cung_dau_vao_ra_cung_chuoi()
    {
        Assert.Equal(
            TicketPdfService.BuildQrSvg(SampleCode),
            TicketPdfService.BuildQrSvg(SampleCode));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildQrSvg_thieu_ma_thi_nem(string? code)
    {
        // ThrowsAny: chuỗi null đi qua ThrowIfNullOrWhiteSpace của BCL ra ArgumentNullException —
        // lớp CON của ArgumentException. Assert.Throws đòi khớp chính xác kiểu nên sẽ trượt oan;
        // điều cần ghim là "ném lỗi tham số", không phải đúng lớp con nào.
        Assert.ThrowsAny<ArgumentException>(() => TicketPdfService.BuildQrSvg(code!));
    }

    // ---- File PDF -----------------------------------------------------------

    [Fact]
    public void GeneratePdf_ra_file_PDF_that_dau_PDF_duoi_EOF()
    {
        var bytes = new TicketPdfService().GeneratePdf(SampleModel());

        Assert.True(bytes.Length > 2000, $"PDF nghi ngờ rỗng: {bytes.Length} byte");
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));

        // Đuôi file có thể kèm xuống dòng sau %%EOF — quét 32 byte cuối.
        var tail = Encoding.ASCII.GetString(bytes, bytes.Length - 32, 32);
        Assert.Contains("%%EOF", tail);
    }

    [Fact]
    public void GeneratePdf_van_ra_ve_khi_thieu_du_lieu_phu()
    {
        // Vé không có biển số / điểm lên / điểm xuống là chuyện thường (dữ liệu chưa đủ) — các dòng
        // đó phải biến mất chứ không được làm vỡ cả tấm vé.
        var model = SampleModel() with
        {
            LicensePlate = null,
            BoardingStopName = null,
            AlightingStopName = null,
        };

        var bytes = new TicketPdfService().GeneratePdf(model);

        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GeneratePdf_thieu_ma_QR_thi_nem(string? code)
    {
        // Vé không mã là vé vô dụng khi lên xe — hỏng phải nổi lên, KHÔNG in tấm vé thiếu mã.
        var model = SampleModel() with { Code = code! };

        // ThrowsAny — cùng lý do ở BuildQrSvg_thieu_ma_thi_nem: null ra ArgumentNullException.
        Assert.ThrowsAny<ArgumentException>(() => new TicketPdfService().GeneratePdf(model));
    }

    [Fact]
    public void GeneratePdf_model_null_thi_nem()
    {
        Assert.Throws<ArgumentNullException>(() => new TicketPdfService().GeneratePdf(null!));
    }

    // ---- Dữ liệu vé (TicketPdfModel) ---------------------------------------

    [Fact]
    public void DepartureText_doi_UTC_sang_gio_Viet_Nam()
    {
        // 01:30 UTC 15/10/2026 = 08:30 cùng ngày theo đồng hồ Việt Nam (UTC+7).
        Assert.Equal("08:30 15/10/2026", SampleModel().DepartureText);
    }

    [Fact]
    public void DepartureText_KIND_Unspecified_coi_nhu_UTC()
    {
        // Cố ý: KHÔNG đi qua ToUniversalTime() — hôm đó máy chạy múi giờ nào cũng phải ra cùng giờ,
        // nếu không cùng một vé in trên máy dev Việt Nam và CI ubuntu sẽ lệch nhau.
        var model = SampleModel() with
        {
            DepartureTime = new DateTime(2026, 10, 15, 1, 30, 0, DateTimeKind.Unspecified),
        };

        Assert.Equal("08:30 15/10/2026", model.DepartureText);
    }

    [Fact]
    public void DepartureText_qua_nua_dem_thi_sang_ngay_hom_sau()
    {
        var model = SampleModel() with
        {
            DepartureTime = new DateTime(2026, 10, 15, 20, 0, 0, DateTimeKind.Utc),
        };

        Assert.Equal("03:00 16/10/2026", model.DepartureText);
    }

    [Fact]
    public void PriceText_nhom_nghin_va_ky_hieu_dong_kieu_Viet_Nam()
    {
        Assert.Equal("350.000 ₫", SampleModel().PriceText);
        Assert.Equal("0 ₫", (SampleModel() with { Price = 0m }).PriceText);
        Assert.Equal("9.999.999 ₫", (SampleModel() with { Price = 9999999m }).PriceText);
    }

    // ---- Đọc ngược SVG ------------------------------------------------------

    /// <summary>Bóc width/height từ viewBox và danh sách ô đen từ thẻ path của SVG service dệt ra.</summary>
    private static (int Width, int Height, List<(int X, int Y)> Dark) ParseSvg(string svg)
    {
        var viewBox = Regex.Match(svg, @"viewBox=""0 0 (\d+) (\d+)""");
        Assert.True(viewBox.Success, "SVG thiếu viewBox");

        var path = Regex.Match(svg, @"\sd=""([^""]*)""");
        Assert.True(path.Success, "SVG thiếu thẻ path chứa các ô đen");

        var dark = Regex.Matches(path.Groups[1].Value, @"M(\d+) (\d+)h1v1h-1z")
            .Select(m => (X: int.Parse(m.Groups[1].Value), Y: int.Parse(m.Groups[2].Value)))
            .ToList();

        return (int.Parse(viewBox.Groups[1].Value), int.Parse(viewBox.Groups[2].Value), dark);
    }

    /// <summary>
    /// Dựng lại ảnh điểm từ SVG rồi cho ZXing giải mã — vòng đời đầy đủ "in rồi quét".
    /// Phóng mỗi module lên <paramref name="scale"/> điểm ảnh cho bộ đọc dễ thở (ảnh 1 điểm
    /// ảnh/module là điều kiện biên không đáng đánh đổi độ ổn định của test).
    /// </summary>
    private static Result? DecodeQr(string svg, int scale = 8)
    {
        var (width, height, dark) = ParseSvg(svg);
        var imageWidth = width * scale;
        var imageHeight = height * scale;

        // RGB24: nền trắng, ô đen tô đen.
        var pixels = new byte[imageWidth * imageHeight * 3];
        Array.Fill(pixels, (byte)255);

        foreach (var (x, y) in dark)
        {
            for (var dy = 0; dy < scale; dy++)
            {
                for (var dx = 0; dx < scale; dx++)
                {
                    var offset = ((y * scale + dy) * imageWidth + x * scale + dx) * 3;
                    pixels[offset] = 0;
                    pixels[offset + 1] = 0;
                    pixels[offset + 2] = 0;
                }
            }
        }

        var source = new RGBLuminanceSource(
            pixels, imageWidth, imageHeight, RGBLuminanceSource.BitmapFormat.RGB24);

        return new QRCodeReader().decode(new BinaryBitmap(new HybridBinarizer(source)));
    }

    // ---- Dựng dữ liệu -------------------------------------------------------

    private static TicketPdfModel SampleModel() => new()
    {
        TicketId = Guid.Parse("3f8c1d2e-4b5a-4c6d-8e7f-9012345678ab"),
        Code = SampleCode,
        PassengerName = "Nguyễn Văn Hạnh",
        RouteName = "Hà Nội — Đà Nẵng",
        DepartureTime = new DateTime(2026, 10, 15, 1, 30, 0, DateTimeKind.Utc),
        SeatNumber = "A12",
        Price = 350000m,
        LicensePlate = "29B-12345",
        BoardingStopName = "Bến xe Mỹ Đình",
        AlightingStopName = "Bến xe Đà Nẵng",
    };
}
