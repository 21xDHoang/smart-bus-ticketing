using System.Text;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test cho <see cref="ReportPdfService"/> và dữ liệu vào <see cref="ReportPdfModel"/> — service
/// xuất PDF báo cáo có tiêu đề, logo, ngày xuất (US 21 "Xuất dữ liệu", Sprint 4, task dòng 77 —
/// Trần Trung Hiếu).
///
/// Service thuần (không CSDL, không mạng, không đọc giờ hệ thống) nên test không cần hạ tầng gì.
/// Hai ghim chính: (1) cùng model luôn ra cùng file — chứng minh không có DateTime.Now lẩn trong
/// code vẽ; (2) lỗi dữ liệu (thiếu tiêu đề, logo không phải SVG, bảng lệch cột) ném ngay khi gọi
/// thay vì in ra chứng từ sai.
/// </summary>
public class ReportPdfServiceTests
{
    /// <summary>Logo giả của test — SVG tối giản, không phụ thuộc file ngoài assembly.</summary>
    private const string SampleLogoSvg =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"48\" height=\"46\" viewBox=\"0 0 48 46\">" +
        "<rect width=\"48\" height=\"46\" fill=\"#863bff\"/></svg>";

    // ---- File PDF -----------------------------------------------------------

    [Fact]
    public void GeneratePdf_ra_file_PDF_that_dau_PDF_duoi_EOF()
    {
        var bytes = new ReportPdfService().GeneratePdf(SampleModel());

        Assert.True(bytes.Length > 2000, $"PDF nghi ngờ rỗng: {bytes.Length} byte");
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));

        // Đuôi file có thể kèm xuống dòng sau %%EOF — quét 32 byte cuối.
        var tail = Encoding.ASCII.GetString(bytes, bytes.Length - 32, 32);
        Assert.Contains("%%EOF", tail);
    }

    [Fact]
    public void GeneratePdf_cung_model_ra_cung_file()
    {
        // Chứng từ trình ban giám đốc phải tái hiện được: cùng dữ liệu xuất hai lần ra hai file
        // giống hệt nhau — ngày xuất do model truyền vào, không có DateTime.Now trong code vẽ.
        var service = new ReportPdfService();

        Assert.Equal(service.GeneratePdf(SampleModel()), service.GeneratePdf(SampleModel()));
    }

    [Fact]
    public void GeneratePdf_model_toi_thieu_van_ra_PDF()
    {
        // Báo cáo không logo, không bảng, không ghi chú vẫn là báo cáo hợp lệ — các khối phụ phải
        // biến mất chứ không được làm vỡ trang.
        var model = new ReportPdfModel
        {
            Title = "Báo cáo doanh thu",
            ExportedAt = new DateTime(2026, 10, 14, 2, 30, 0, DateTimeKind.Utc),
        };

        var bytes = new ReportPdfService().GeneratePdf(model);

        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
    }

    [Fact]
    public void GeneratePdf_logo_trang_xem_nhu_khong_co_logo()
    {
        // LogoSvg null / chuỗi trắng là "cố ý không in logo" — không ném, không in khối trống.
        var model = SampleModel() with { LogoSvg = "   " };

        var bytes = new ReportPdfService().GeneratePdf(model);

        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
    }

    [Fact]
    public void GeneratePdf_bang_nhieu_dong_tra_nhieu_trang()
    {
        // 300 dòng vượt xa một trang A4 — QuestPDF tự phân trang, hàng tiêu đề lặp lại mỗi trang.
        var model = SampleModel() with
        {
            Table = new ReportTable
            {
                Headers = ["Tuyến", "Doanh thu"],
                Rows = Enumerable.Range(1, 300)
                    .Select(i => (IReadOnlyList<string>)[$"Tuyến {i}", $"{i * 1_000_000:N0} ₫"])
                    .ToList(),
            },
        };

        var bytes = new ReportPdfService().GeneratePdf(model);

        Assert.True(bytes.Length > 2000, $"PDF nghi ngờ rỗng: {bytes.Length} byte");
        // File nhiều trang phải lớn hơn hẳn file một trang cùng model — phân trang có thật chứ
        // không phải nhét 300 dòng vào một trang.
        Assert.True(bytes.Length > new ReportPdfService().GeneratePdf(SampleModel()).Length);
    }

    // ---- Lỗi dữ liệu ---------------------------------------------------------

    [Fact]
    public void GeneratePdf_model_null_thi_nem()
    {
        Assert.Throws<ArgumentNullException>(() => new ReportPdfService().GeneratePdf(null!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GeneratePdf_thieu_tieu_de_thi_nem(string? title)
    {
        var model = SampleModel() with { Title = title! };

        // ThrowsAny — cùng lý do TicketPdfServiceTests: null đi qua ThrowIfNullOrWhiteSpace ra
        // ArgumentNullException (lớp CON của ArgumentException), chỉ cần ghim "lỗi tham số".
        Assert.ThrowsAny<ArgumentException>(() => new ReportPdfService().GeneratePdf(model));
    }

    [Fact]
    public void GeneratePdf_logo_khong_phai_SVG_thi_nem()
    {
        // Nhầm đường dẫn file hoặc chuỗi Base64 vào ô logo là lỗi phổ biến — chặn ngay khi gọi
        // chứ không đợi QuestPDF ném giữa lúc render.
        var model = SampleModel() with { LogoSvg = "C:\\anh\\logo.png" };

        Assert.Throws<ArgumentException>(() => new ReportPdfService().GeneratePdf(model));
    }

    [Fact]
    public void GeneratePdf_bang_co_dong_mot_khong_co_dau_cot_thi_nem()
    {
        var model = SampleModel() with
        {
            Table = new ReportTable
            {
                Headers = [],
                Rows = [["Hà Nội — Đà Nẵng"]],
            },
        };

        Assert.Throws<ArgumentException>(() => new ReportPdfService().GeneratePdf(model));
    }

    [Fact]
    public void GeneratePdf_dong_lech_so_cot_thi_nem()
    {
        var model = SampleModel() with
        {
            Table = new ReportTable
            {
                Headers = ["Tuyến", "Doanh thu"],
                Rows = [["Thiếu cột thứ hai"]],
            },
        };

        Assert.Throws<ArgumentException>(() => new ReportPdfService().GeneratePdf(model));
    }

    // ---- Ngày xuất (ReportPdfModel) -----------------------------------------

    [Fact]
    public void ExportedAtText_doi_UTC_sang_gio_Viet_Nam()
    {
        // 02:30 UTC 14/10/2026 = 09:30 cùng ngày theo đồng hồ Việt Nam (UTC+7).
        Assert.Equal("09:30 14/10/2026", SampleModel().ExportedAtText);
    }

    [Fact]
    public void ExportedAtText_KIND_Unspecified_coi_nhu_UTC()
    {
        // Cố ý: KHÔNG đi qua ToUniversalTime() — hôm đó máy chạy múi giờ nào cũng phải ra cùng giờ,
        // nếu không cùng một báo cáo xuất trên máy dev Việt Nam và CI ubuntu sẽ lệch nhau.
        var model = SampleModel() with
        {
            ExportedAt = new DateTime(2026, 10, 14, 2, 30, 0, DateTimeKind.Unspecified),
        };

        Assert.Equal("09:30 14/10/2026", model.ExportedAtText);
    }

    [Fact]
    public void ExportedAtText_qua_nua_dem_thi_sang_ngay_hom_sau()
    {
        var model = SampleModel() with
        {
            ExportedAt = new DateTime(2026, 10, 14, 20, 0, 0, DateTimeKind.Utc),
        };

        Assert.Equal("03:00 15/10/2026", model.ExportedAtText);
    }

    // ---- Dựng dữ liệu -------------------------------------------------------

    private static ReportPdfModel SampleModel() => new()
    {
        Title = "Báo cáo doanh thu theo tuyến",
        LogoSvg = SampleLogoSvg,
        ExportedAt = new DateTime(2026, 10, 14, 2, 30, 0, DateTimeKind.Utc),
        Subtitle = "Từ 01/10/2026 đến 14/10/2026",
        GeneratedBy = "Nguyễn Văn An",
        FooterNote = "Số liệu tổng hợp từ vé đã thanh toán",
        Table = new ReportTable
        {
            Headers = ["Tuyến", "Số vé", "Doanh thu"],
            Rows =
            [
                ["Hà Nội — Đà Nẵng", "120", "35.000.000 ₫"],
                ["Hà Nội — Hải Phòng", "85", "12.750.000 ₫"],
            ],
        },
    };
}
