using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IReportPdfService"/> — vẽ báo cáo khổ A4 bằng QuestPDF (US 21 "Xuất dữ
/// liệu", Sprint 4, task dòng 77 — *"Service xuất PDF có tiêu đề, logo, ngày xuất"* — Trần Trung
/// Hiếu).
///
/// Ba chốt kỹ thuật, đọc trước khi sửa:
///
/// 1. **Logo nhận dạng chuỗi SVG, không phải ảnh bitmap.** Cùng lý do mã QR trên vé
///    (<see cref="TicketPdfService"/> chốt 1): vector in cỡ nào cũng sắc, và không kéo đường
///    giải mã ảnh native — thứ dễ vỡ khi CI chạy ubuntu còn máy dev chạy Windows. SVG cũng test
///    được: thiếu thẻ <c>&lt;svg&gt;</c> là ném ngay trước khi render.
///
/// 2. **Font Roboto nhúng trong assembly** (EmbeddedResource ở csproj — đã có sẵn từ service vé,
///    file của Hoàng, KHÔNG sửa). Font Lato mặc định kèm QuestPDF thiếu dấu tiếng Việt. Vì vé và
///    báo cáo cùng tiến trình, Roboto có thể đã được <see cref="TicketPdfService"/> đăng ký khi
///    process chạy service kia trước — kiểm tra danh sách font đã đăng ký rồi mới đăng ký, không
///    để font list phình bản copy vô ích.
///
/// 3. **Ngày xuất do bên gọi truyền vào, không tự lấy <c>DateTime.Now</c>.** Cùng một model phải
///    ra cùng một file — đây là chứng từ trình ban giám đốc, phải tái hiện được. Test ghim tính
///    tất định; in ra là giờ Việt Nam (UTC+7) theo <see cref="ReportPdfModel.ExportedAtText"/>.
///
/// Service thuần: không CSDL, không mạng. Bên gọi gom đủ dữ liệu vào <see cref="ReportPdfModel"/>.
/// </summary>
public sealed class ReportPdfService : IReportPdfService
{
    /// <summary>Tên họ font nhận diện từ file Roboto nhúng (QuestPDF tự dò tên + độ đậm trong file).</summary>
    private const string FontFamily = "Roboto";

    /// <summary>Tên tài nguyên nhúng — phải khớp <c>LogicalName</c> khai trong SmartBus.Api.csproj.</summary>
    private const string RegularFontResource = "SmartBus.Api.Assets.Roboto-Regular.ttf";
    private const string BoldFontResource = "SmartBus.Api.Assets.Roboto-Bold.ttf";

    /// <summary>Khổ logo ở góc trái đầu trang (rộng × cao tối thiểu — FitArea giữ tỉ lệ trong khung).</summary>
    private const float LogoWidthMillimetres = 26;
    private const float LogoMinHeightMillimetres = 14;

    static ReportPdfService()
    {
        // QuestPDF Community: miễn phí cho tổ chức dưới 1 triệu USD doanh thu/năm — dự án học tập
        // thuộc diện này. Đổi giấy phép phải là quyết định có ý thức, không phải sửa cho hết cảnh
        // báo khi build.
        QuestPDF.Settings.License = LicenseType.Community;

        // Roboto có thể đã được TicketPdfService (static ctor của service vé, file của Hoàng) đăng
        // ký nếu process chạy service kia trước. QuestPDF KHÔNG ném khi đăng ký trùng — chỉ thêm
        // một bản copy vào font list (đã kiểm chứng: danh sách tăng thêm entry chứ không lỗi) —
        // nên chủ động bỏ qua để danh sách font không phình vô ích.
        if (FontManager.GetRegisteredFonts().Any(f => f.FamilyName == FontFamily))
        {
            return;
        }

        var assembly = typeof(ReportPdfService).Assembly;
        FontManager.RegisterFontFromEmbeddedResource(assembly, RegularFontResource);
        FontManager.RegisterFontFromEmbeddedResource(assembly, BoldFontResource);
    }

    public byte[] GeneratePdf(ReportPdfModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        // Báo cáo không tiêu đề là chứng từ không nhận diện được — hỏng phải ném ngay, không in
        // một trang giấy trắng tiêu đề.
        ArgumentException.ThrowIfNullOrWhiteSpace(model.Title);

        ValidateLogoSvg(model.LogoSvg);
        ValidateTable(model.Table);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);   // khổ giấy trình ban giám đốc, in A4 thẳng không phải gấp
                page.Margin(16, Unit.Millimetre);
                page.DefaultTextStyle(style => style.FontFamily(FontFamily).FontSize(10).FontColor(Colors.Black));

                page.Content().Column(column =>
                {
                    column.Spacing(6);

                    DrawHeader(column, model);

                    if (model.Table is { } table && table.Headers.Count > 0)
                    {
                        DrawTable(column, table);
                    }
                });

                DrawFooter(page, model);
            });
        }).GeneratePdf();
    }

    /// <summary>
    /// Đầu trang: logo (góc trái, có thì in) + tiêu đề + dòng phụ, rồi dòng tự mô tả "Ngày xuất /
    /// Người xuất". Dòng tự mô tả bắt buộc có vì file xuất ra là chứng từ RỜI KHỎI hệ thống —
    /// phải tự nói được xuất lúc nào, ai xuất (cùng tinh thần khối tự mô tả của
    /// <see cref="AuditLogExportService"/>).
    /// </summary>
    private static void DrawHeader(ColumnDescriptor column, ReportPdfModel model)
    {
        column.Item().Row(header =>
        {
            if (!string.IsNullOrWhiteSpace(model.LogoSvg))
            {
                header.ConstantItem(LogoWidthMillimetres, Unit.Millimetre)
                    .MinHeight(LogoMinHeightMillimetres, Unit.Millimetre)
                    .AlignCenter()
                    .AlignMiddle()
                    .Svg(model.LogoSvg)
                    .FitArea();
            }

            header.RelativeItem().PaddingLeft(4, Unit.Millimetre).Column(titleBlock =>
            {
                titleBlock.Item().Text(model.Title).FontSize(16).Bold().FontColor(Colors.Blue.Darken2);

                if (!string.IsNullOrWhiteSpace(model.Subtitle))
                {
                    titleBlock.Item().PaddingTop(2).Text(model.Subtitle).FontSize(9).FontColor(Colors.Grey.Darken1);
                }
            });
        });

        column.Item().PaddingTop(4).Row(meta =>
        {
            meta.RelativeItem()
                .Text($"Ngày xuất: {model.ExportedAtText} (UTC+7)")
                .FontSize(9)
                .FontColor(Colors.Grey.Darken2);

            if (!string.IsNullOrWhiteSpace(model.GeneratedBy))
            {
                meta.RelativeItem().AlignRight()
                    .Text($"Người xuất: {model.GeneratedBy}")
                    .FontSize(9)
                    .FontColor(Colors.Grey.Darken2);
            }
        });

        column.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
    }

    /// <summary>
    /// Bảng số liệu: hàng tiêu đề in đậm trên nền xám nhạt (QuestPDF lặp lại nó ở đầu mỗi trang),
    /// mỗi dòng dữ liệu một dòng, ngăn bằng viền mờ. Cột rộng bằng nhau — bảng dùng chung cho mọi
    /// báo cáo nên không đoán cột nào quan trọng hơn; bên gọi muốn nhấn cột nào thì tự xử lý dữ
    /// liệu (bỏ cột thừa, nối gọn tên cột).
    /// </summary>
    private static void DrawTable(ColumnDescriptor column, ReportTable table)
    {
        column.Item().PaddingTop(4).Table(t =>
        {
            t.ColumnsDefinition(cols =>
            {
                for (var i = 0; i < table.Headers.Count; i++)
                {
                    cols.RelativeColumn(1);
                }
            });

            t.Header(header =>
            {
                foreach (var headerText in table.Headers)
                {
                    header.Cell()
                        .Background(Colors.Grey.Lighten3)
                        .PaddingVertical(4)
                        .PaddingHorizontal(6)
                        .Text(headerText)
                        .FontSize(9)
                        .SemiBold();
                }
            });

            foreach (var row in table.Rows)
            {
                foreach (var cell in row)
                {
                    t.Cell()
                        .PaddingVertical(3)
                        .PaddingHorizontal(6)
                        .BorderBottom(0.5f)
                        .BorderColor(Colors.Grey.Lighten2)
                        .Text(cell)
                        .FontSize(9);
                }
            }
        });
    }

    /// <summary>Cuối mỗi trang: ghi chú (có thì in) bên trái, số trang "Trang X / Y" bên phải.</summary>
    private static void DrawFooter(PageDescriptor page, ReportPdfModel model)
    {
        page.Footer().PaddingTop(4).Row(footer =>
        {
            if (!string.IsNullOrWhiteSpace(model.FooterNote))
            {
                footer.RelativeItem()
                    .Text(model.FooterNote)
                    .FontSize(8)
                    .FontColor(Colors.Grey.Darken2);
            }

            footer.RelativeItem().AlignRight().Text(text =>
            {
                text.DefaultTextStyle(style => style.FontSize(8).FontColor(Colors.Grey.Darken2));
                text.Span("Trang ");
                text.CurrentPageNumber();
                text.Span(" / ");
                text.TotalPages();
            });
        });
    }

    /// <summary>
    /// Logo phải là chuỗi SVG có thẻ <c>&lt;svg&gt;</c> — kiểm kiểu nông (chỉ tìm thẻ) chứ không
    /// parse hẳn: parse SVG nằm trong QuestPDF lúc render, ở đây chỉ cần chặn sớm kiểu nhầm lẫn
    /// phổ biến nhất (truyền nhầm đường dẫn file hoặc chuỗi Base64 vào ô logo) bằng lỗi rõ ràng.
    /// </summary>
    private static void ValidateLogoSvg(string? logoSvg)
    {
        if (string.IsNullOrWhiteSpace(logoSvg))
        {
            return;   // trống = cố ý không in logo, hợp lệ
        }

        if (!logoSvg.Contains("<svg", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Logo phải là chuỗi SVG có thẻ <svg>.", nameof(ReportPdfModel.LogoSvg));
        }
    }

    /// <summary>
    /// Kiểm khuôn bảng trước khi vẽ: có dòng dữ liệu thì phải có đầu cột; mỗi dòng phải đủ đúng
    /// số ô của đầu cột. Bảng lệch cột in ra là chứng từ sai số liệu — ném ngay khi gọi.
    /// </summary>
    private static void ValidateTable(ReportTable? table)
    {
        if (table is null)
        {
            return;
        }

        if (table.Headers.Count == 0)
        {
            // Không dòng dữ liệu thì bảng rỗng — GeneratePdf bỏ qua, không vẽ khối trống.
            if (table.Rows.Count > 0)
            {
                throw new ArgumentException(
                    "Bảng có dòng dữ liệu thì phải có tiêu đề cột.", nameof(ReportPdfModel.Table));
            }

            return;
        }

        for (var i = 0; i < table.Rows.Count; i++)
        {
            if (table.Rows[i].Count != table.Headers.Count)
            {
                throw new ArgumentException(
                    $"Dòng {i + 1} của bảng có {table.Rows[i].Count} ô, lệch {table.Headers.Count} cột tiêu đề.",
                    nameof(ReportPdfModel.Table));
            }
        }
    }
}
