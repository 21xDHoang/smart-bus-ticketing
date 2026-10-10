using System.Text;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using ZXing.QrCode.Internal;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="ITicketPdfService"/> — vẽ vé A5 bằng QuestPDF, mã QR vẽ bằng tay từ ma trận
/// ZXing (US 4, Sprint 3, task *"Sinh file PDF vé có mã QR + gửi kèm qua email"* — Phùng Duy Hoàng).
///
/// Ba chốt kỹ thuật, đọc trước khi sửa:
///
/// 1. **QR vẽ bằng SVG tự dựng, không qua ảnh bitmap.** ZXing cho ra ma trận điểm; service tự dệt
///    chuỗi SVG (mỗi ô đen một lệnh <c>M x y h1v1h-1z</c>) rồi nhúng bằng <c>.Svg()</c> của
///    QuestPDF. Vì sao không dùng đường ảnh: (a) khỏi kéo theo đường encode ảnh native — thứ dễ
///    vỡ khi CI chạy ubuntu còn máy dev chạy Windows; (b) SVG là vector — in ra ở cỡ nào cũng sắc
///    cạnh, không bị nội suy làm nhoè ô QR; (c) test kiểm được bằng cách đọc ngược toạ độ trong
///    SVG rồi giải mã lại — chứng minh "máy quét đọc vé của mình ra đúng mã", không chỉ "file mở
///    được".
///
/// 2. **Font Roboto nhúng trong assembly** (EmbeddedResource ở csproj). Font Lato mặc định kèm
///    QuestPDF THIẾU dấu tiếng Việt — "Vé điện tử" sẽ ra ô vuông (tofu) hoặc rơi xuống font hệ
///    thống (máy dev có, CI ubuntu không). Nhúng thẳng file .ttf vào assembly là đường duy nhất
///    bảo đảm giống nhau ở mọi môi trường. Kèm Roboto-LICENSE.txt (Apache-2.0) — xem thư mục Assets.
///
/// 3. **Mã QR in từ <c>model.Code</c> đã lưu — KHÔNG sinh lại.** Sinh lại là mỗi lần gửi email ra
///    một mã khác, vé trong email không khớp mã đã phát hành (docs/28 §6, docs/30 §3.1).
///
/// Service thuần: không CSDL, không mạng. Bên gọi gom đủ dữ liệu vào <see cref="TicketPdfModel"/>.
/// </summary>
public class TicketPdfService : ITicketPdfService
{
    /// <summary>Tên họ font nhận diện từ file Roboto nhúng (QuestPDF tự dò tên + độ đậm trong file).</summary>
    private const string FontFamily = "Roboto";

    /// <summary>Tên tài nguyên nhúng — phải khớp <c>LogicalName</c> khai trong SmartBus.Api.csproj.</summary>
    private const string RegularFontResource = "SmartBus.Api.Assets.Roboto-Regular.ttf";
    private const string BoldFontResource = "SmartBus.Api.Assets.Roboto-Bold.ttf";

    /// <summary>
    /// Vùng lặng quanh QR, tính bằng module — chuẩn QR gọi là 4. Giữ đúng chuẩn: máy quét cần
    /// khoảng trắng này để tách mã khỏi nền; cắt bớt để "tiết kiệm chỗ" là cách tự làm hỏng tỉ lệ
    /// quét được.
    /// </summary>
    private const int QuietZoneModules = 4;

    static TicketPdfService()
    {
        // QuestPDF Community: miễn phí cho tổ chức dưới 1 triệu USD doanh thu/năm — dự án học tập
        // thuộc diện này. Đổi giấy phép phải là quyết định có ý thức, không phải sửa cho hết cảnh
        // báo khi build.
        QuestPDF.Settings.License = LicenseType.Community;

        // Chạy MỘT lần cho cả tiến trình (static ctor). Nhúng trong assembly của chính service này
        // nên bản build của SmartBus.Tests tham chiếu là có font — không phụ thuộc file copy cạnh
        // file chạy (CI ubuntu không có Roboto hệ thống).
        var assembly = typeof(TicketPdfService).Assembly;
        FontManager.RegisterFontFromEmbeddedResource(assembly, RegularFontResource);
        FontManager.RegisterFontFromEmbeddedResource(assembly, BoldFontResource);
    }

    public byte[] GeneratePdf(TicketPdfModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        // Vé không mã QR là vé vô dụng khi lên xe — hỏng phải ném ra ngay, KHÔNG in tấm vé thiếu
        // mã rồi để hành khách ra bến mới biết.
        ArgumentException.ThrowIfNullOrWhiteSpace(model.Code);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A5);   // 148 × 210 mm — cỡ vé, in giấy A4 gấp đôi vẫn vừa
                page.Margin(12, Unit.Millimetre);
                page.DefaultTextStyle(style => style.FontFamily(FontFamily).FontSize(10).FontColor(Colors.Black));

                page.Content().Column(column =>
                {
                    column.Spacing(2);

                    column.Item().Text("VÉ ĐIỆN TỬ").FontSize(15).Bold().FontColor(Colors.Blue.Darken2);
                    column.Item().Text("Smart Bus Ticketing").FontSize(9).FontColor(Colors.Grey.Darken1);
                    column.Item().PaddingTop(6).Text(model.RouteName).FontSize(13).SemiBold();
                    column.Item().PaddingVertical(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);

                    InfoRow(column, "Hành khách", model.PassengerName);
                    InfoRow(column, "Khởi hành", model.DepartureText);
                    InfoRow(column, "Ghế", model.SeatNumber);

                    if (!string.IsNullOrWhiteSpace(model.LicensePlate))
                    {
                        InfoRow(column, "Biển số xe", model.LicensePlate);
                    }

                    if (!string.IsNullOrWhiteSpace(model.BoardingStopName))
                    {
                        InfoRow(column, "Điểm lên", model.BoardingStopName);
                    }

                    if (!string.IsNullOrWhiteSpace(model.AlightingStopName))
                    {
                        InfoRow(column, "Điểm xuống", model.AlightingStopName);
                    }

                    InfoRow(column, "Giá vé", model.PriceText);

                    // QR là phần duy nhất vẽ từ Code; mọi chữ khác chỉ để người đọc đối chiếu.
                    column.Item().PaddingTop(10).AlignCenter().Width(48, Unit.Millimetre).Svg(BuildQrSvg(model.Code));
                    column.Item().PaddingTop(4).AlignCenter()
                        .Text("Xuất trình mã QR này khi lên xe").FontSize(9).FontColor(Colors.Grey.Darken2);

                    // Id vé cỡ nhỏ ở chân — người hỗ trợ tra được vé khi hành khách gọi lên.
                    column.Item().PaddingTop(8).AlignCenter()
                        .Text($"Mã vé: {model.TicketId:D}").FontSize(7).FontColor(Colors.Grey.Darken1);
                });
            });
        }).GeneratePdf();
    }

    /// <summary>
    /// Dựng mã QR từ <paramref name="code"/> thành chuỗi SVG — public để test gọi thẳng mà đọc
    /// ngược toạ độ rồi giải mã lại (xem TicketPdfServiceTests). Hàm thuần, cùng đầu vào luôn ra
    /// cùng chuỗi.
    /// </summary>
    public static string BuildQrSvg(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var matrix = BuildQrMatrix(code);
        var width = matrix.Width;    // đã gồm vùng lặng hai bên (ZXing cộng MARGIN vào ma trận)
        var height = matrix.Height;

        // Mỗi ô đen một lệnh "M{x} {y}h1v1h-1z" (nhảy tới ô, vẽ hình vuông 1×1). Gộp tất cả vào
        // MỘT thẻ <path> — 41×41 ô là ~1300 lệnh, tách mỗi ô một thẻ <rect> thì SVG phình gấp
        // nhiều lần vô ích.
        var path = new StringBuilder();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (matrix[x, y])
                {
                    path.Append('M').Append(x).Append(' ').Append(y).Append("h1v1h-1z");
                }
            }
        }

        return new StringBuilder()
            .Append("<svg xmlns=\"http://www.w3.org/2000/svg\" ")
            .Append("width=\"").Append(width).Append("\" height=\"").Append(height).Append("\" ")
            .Append("viewBox=\"0 0 ").Append(width).Append(' ').Append(height).Append("\" ")
            // crispEdges: tắt khử răng cưa — mép ô QR phải sắc, nhoè là mất tỉ lệ quét.
            .Append("shape-rendering=\"crispEdges\">")
            .Append("<rect width=\"100%\" height=\"100%\" fill=\"#FFFFFF\"/>")
            .Append("<path fill=\"#000000\" d=\"").Append(path).Append("\"/>")
            .Append("</svg>")
            .ToString();
    }

    /// <summary>
    /// Ma trận điểm QR từ ZXing (thuần quản lý, không native — chạy giống nhau trên Windows lẫn CI
    /// ubuntu). Kích thước 0×0 nghĩa là "để ZXing tự chọn cỡ nhỏ nhất đủ chứa" — ta không muốn nó
    /// phóng to thành ảnh điểm, SVG tự lo việc phóng theo khung.
    /// </summary>
    private static BitMatrix BuildQrMatrix(string code)
    {
        var hints = new Dictionary<EncodeHintType, object>
        {
            // Vùng lặng 4 module — xem QuietZoneModules.
            { EncodeHintType.MARGIN, QuietZoneModules },

            // Mức sửa lỗi Q (~25%): vé in ra có thể bị gấp/hở một góc nhỏ mà máy quét vẫn đọc được.
            // Không lên H vì mã sẽ dày thêm làm ô nhỏ hơn — hại nhiều hơn lợi ở cỡ in vé.
            { EncodeHintType.ERROR_CORRECTION, ErrorCorrectionLevel.Q },

            // Mã SBT1 toàn ASCII; khai báo thẳng UTF-8 cho tường minh, khỏi phụ thuộc mặc định
            // của thư viện khi nâng cấp.
            { EncodeHintType.CHARACTER_SET, "UTF-8" },
        };

        return new QRCodeWriter().encode(code, BarcodeFormat.QR_CODE, 0, 0, hints);
    }

    /// <summary>Một dòng nhãn–giá trị trên vé. Nhãn xám nhạt, giá trị đậm — mắt quét theo cột giá trị.</summary>
    private static void InfoRow(ColumnDescriptor column, string label, string value)
    {
        column.Item().PaddingBottom(2).Row(row =>
        {
            row.ConstantItem(80).Text(label).FontSize(9).FontColor(Colors.Grey.Darken2);
            row.RelativeItem().Text(value).FontSize(10).SemiBold();
        });
    }
}
