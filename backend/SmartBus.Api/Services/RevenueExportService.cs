using System.Globalization;
using SmartBus.Api.Dtos.Reports;

namespace SmartBus.Api.Services;

/// <summary>
/// Xuất báo cáo doanh thu ra Excel/PDF — <c>GET /api/reports/revenue/export</c>. US 24 "Xuất dữ
/// liệu", Sprint 4 dòng 78 — Phùng Duy Hoàng.
///
/// Quyết định quan trọng nhất: <b>không tự tính doanh thu</b>. Service gọi
/// <see cref="IRevenueReportService"/> — đúng bản có đệm 60 giây mà endpoint JSON đang dùng — rồi
/// chỉ dàn các bucket ra file. Hợp đồng ghim "số liệu trong file là đúng số liệu
/// <c>GET /reports/revenue</c> trả về", nên mọi phép lọc/đếm/đổi múi giờ nằm ở báo cáo gốc; viết
/// lại ở đây là mở đường cho hai nơi lệch nhau.
/// </summary>
public sealed class RevenueExportService : IRevenueExportService
{
    private const string SheetName = "Doanh thu";

    private const string DocumentTitle = "BÁO CÁO DOANH THU";

    private const string UnknownActor = "(không xác định)";

    private const int HeaderRow = 4;

    private static readonly string[] Headers = ["Kỳ", "Doanh thu (VND)", "Số giao dịch"];

    /// <summary>Độ rộng cột đặt cứng — không dùng <c>AdjustToContent()</c> vì nó đo font và vỡ trên Linux.</summary>
    private static readonly double[] ColumnWidths = [16, 20, 14];

    /// <summary>
    /// Định dạng tiền/số cho chuỗi hiển thị (dòng mô tả, ô PDF): dấu chấm nhóm hàng nghìn đúng
    /// cách người Việt đọc báo cáo. Ô Excel là số thật, không dùng culture này.
    /// </summary>
    private static readonly CultureInfo VietnameseCulture = CultureInfo.GetCultureInfo("vi-VN");

    private readonly IRevenueReportService _revenueReportService;
    private readonly IReportPdfService _reportPdfService;
    private readonly IReportExcelService _reportExcelService;

    public RevenueExportService(
        IRevenueReportService revenueReportService,
        IReportPdfService reportPdfService,
        IReportExcelService reportExcelService)
    {
        _revenueReportService = revenueReportService;
        _reportPdfService = reportPdfService;
        _reportExcelService = reportExcelService;
    }

    public async Task<ServiceResult<ReportExportFile>> ExportAsync(
        RevenueExportRequest request,
        string? exportedBy,
        CancellationToken cancellationToken)
    {
        // Kiểm định dạng TRƯỚC khi chạm CSDL: tham số hỏng thì không tốn truy vấn nào, và người
        // gọi nhận lỗi gắn đúng ô format thay vì lỗi ngày tháng từ báo cáo gốc.
        if (!ReportExportFormats.TryParse(request.Format, out var format))
        {
            return FormatError();
        }

        var report = await _revenueReportService.GetAsync(request, cancellationToken);

        if (!report.Success)
        {
            return Propagate(report);
        }

        var data = report.Data!;
        var generatedAtUtc = DateTime.UtcNow;

        var content = format == ReportExportFormat.Pdf
            ? BuildPdf(data, generatedAtUtc, exportedBy)
            : BuildExcel(data, generatedAtUtc, exportedBy);

        return ServiceResult<ReportExportFile>.Ok(new ReportExportFile
        {
            Content = content,
            ContentType = format.ContentType(),

            // Tên file ASCII không dấu là CỐ Ý — FileContentResult mã hoá tên có dấu, người dùng
            // nhận file tên lạ. Mốc thời gian UTC để hai lần xuất trong cùng ngày không trùng tên.
            FileName = $"bao-cao-doanh-thu_{generatedAtUtc:yyyyMMdd-HHmmss}Z{format.Extension()}",
        });
    }

    private static ServiceResult<ReportExportFile> FormatError()
        => ServiceResult<ReportExportFile>.Invalid(
            ReportExportFormats.InvalidMessage,
            new Dictionary<string, string[]>
            {
                ["format"] = [ReportExportFormats.InvalidMessage],
            });

    /// <summary>
    /// Chuyển lỗi của báo cáo gốc sang kiểu kết quả của bản xuất, GIỮ NGUYÊN thông báo và
    /// <c>errors</c> theo trường — người gọi không phải học một bảng lỗi thứ hai. Hiện báo cáo gốc
    /// chỉ trả Invalid/NotFound; nhánh còn lại phòng khi nó mở rộng.
    /// </summary>
    private static ServiceResult<ReportExportFile> Propagate(
        ServiceResult<RevenueReportResponse> report)
    {
        return report.ErrorKind switch
        {
            ServiceErrorKind.NotFound => ServiceResult<ReportExportFile>.NotFound(
                report.Error ?? "Không tìm thấy dữ liệu báo cáo."),

            _ => ServiceResult<ReportExportFile>.Invalid(
                report.Error ?? "Dữ liệu đầu vào không hợp lệ",
                report.Errors ?? new Dictionary<string, string[]>()),
        };
    }

    /// <summary>
    /// Dàn các bucket ra khuôn Excel chung. Không còn tự dựng workbook: phép vẽ nằm ở
    /// <see cref="IReportExcelService"/> (task dòng 61 — Kiên), ba báo cáo dùng chung một khuôn.
    /// </summary>
    private byte[] BuildExcel(
        RevenueReportResponse report,
        DateTime generatedAtUtc,
        string? exportedBy)
    {
        var rows = report.Buckets
            .Select(bucket => new ReportExcelRow
            {
                // Kỳ là chuỗi ĐÚNG giá trị periodStart của JSON. Doanh thu và số giao dịch là SỐ
                // THẬT để người nhận cộng/lọc/sort được trong Excel — gán chuỗi là mất nửa giá
                // trị của bản xuất.
                Cells =
                [
                    ReportExcelCell.Text(bucket.PeriodStart),
                    ReportExcelCell.Number(bucket.Revenue),
                    ReportExcelCell.Number(bucket.Payments),
                ],
            })
            .ToList();

        var model = new ReportExcelModel
        {
            SheetName = SheetName,
            Title = DocumentTitle,
            Subtitle = MetadataLine(report, generatedAtUtc, exportedBy),
            Headers = Headers,
            Rows = rows,
            ColumnWidths = ColumnWidths,
        };

        return _reportExcelService.GenerateExcel(model);
    }

    private byte[] BuildPdf(
        RevenueReportResponse report,
        DateTime generatedAtUtc,
        string? exportedBy)
    {
        var rows = report.Buckets
            .Select(bucket => (IReadOnlyList<string>)
            [
                bucket.PeriodStart,
                bucket.Revenue.ToString("N0", VietnameseCulture),
                bucket.Payments.ToString("N0", VietnameseCulture),
            ])
            .ToList();

        var model = new ReportPdfModel
        {
            Title = DocumentTitle,
            Subtitle = MetadataLine(report, generatedAtUtc, exportedBy),
            ExportedAt = generatedAtUtc,
            GeneratedBy = exportedBy,

            // Bảng rỗng vẫn hợp lệ: kỳ không phát sinh giao dịch thì file phải có đầu cột để
            // người đọc thấy "không có dòng nào", không phải một trang giấy trắng.
            Table = new ReportTable
            {
                Headers = Headers,
                Rows = rows,
            },

            FooterNote = "Số liệu lấy từ báo cáo doanh thu của hệ thống, đơn vị VND.",
        };

        return _reportPdfService.GeneratePdf(model);
    }

    /// <summary>
    /// Dòng tự mô tả dùng chung cho Excel (hàng 2) và PDF (dòng phụ). Giữ cùng thứ tự thông tin ở
    /// cả hai định dạng để người đối chiếu hai bản không phải dò lại từ đầu.
    /// </summary>
    private static string MetadataLine(
        RevenueReportResponse report,
        DateTime generatedAtUtc,
        string? exportedBy)
    {
        return
            $"Kỳ báo cáo: {report.From:yyyy-MM-dd} → {report.To:yyyy-MM-dd} (lịch Việt Nam) · " +
            $"Độ gộp: {GranularityLabel(report.Granularity)} · " +
            $"Tổng doanh thu: {report.TotalRevenue.ToString("N0", VietnameseCulture)} VND · " +
            $"Số giao dịch: {report.TotalPayments.ToString("N0", VietnameseCulture)} · " +
            $"Xuất lúc: {generatedAtUtc:yyyy-MM-dd HH:mm:ss} UTC · " +
            $"Người xuất: {exportedBy ?? UnknownActor}";
    }

    /// <summary>
    /// Nhãn tiếng Việt cho độ gộp. Báo cáo gốc trả chuỗi thường (<c>day</c>/<c>week</c>/<c>month</c>);
    /// nhãn lạ rơi về nguyên chuỗi để không nuốt mất thông tin.
    /// </summary>
    private static string GranularityLabel(string granularity) => granularity switch
    {
        "day" => "Ngày",
        "week" => "Tuần",
        "month" => "Tháng",
        _ => granularity,
    };
}
