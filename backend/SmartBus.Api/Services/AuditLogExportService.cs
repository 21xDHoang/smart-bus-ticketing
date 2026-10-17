using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.AuditLogs;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Dựng file Excel/PDF nhật ký kiểm toán — task story 23, Sprint 4 dòng 78, Phùng Duy Hoàng.
/// Hợp đồng đầy đủ ở mục "Nhật ký kiểm toán" của docs/api-contract.md.
///
/// Chỉ ĐỌC bảng AuditLogs. Không sửa entity, không thêm cột, không migration.
/// </summary>
public sealed class AuditLogExportService : IAuditLogExportService
{
    /// <summary>
    /// Trần số dòng mỗi lần xuất. Chọn theo BỘ NHỚ chứ không theo Excel — Excel chứa được hơn
    /// một triệu dòng, còn ClosedXML dựng cả DOM trong RAM: 10.000 dòng × 7 cột tốn khoảng
    /// 40–60 MB, nâng lên 50.000 là ~250 MB, hai request đồng thời là hết gói hosting nhỏ.
    ///
    /// Đổi con số này thì sửa luôn một dòng trong docs/api-contract.md.
    /// </summary>
    public const int MaxRows = 10_000;

    /// <summary>
    /// Trần số dòng khi xuất PDF. Thấp hơn Excel vì QuestPDF dựng cả tài liệu trong RAM trước khi
    /// ghi ra mảng byte, và bảng 7 cột đổ dọc trên A4 dày đặc chữ — 2.000 dòng đã là mấy chục
    /// trang, nhiều hơn nữa thì người đọc cũng không dùng bản giấy.
    ///
    /// Đổi con số này thì sửa luôn một dòng trong docs/api-contract.md.
    /// </summary>
    public const int MaxPdfRows = 2_000;

    /// <summary>Bỏ trống cả from lẫn to thì lấy 30 ngày gần nhất, TÍNH CẢ hôm nay.</summary>
    private const int DefaultWindowDays = 30;

    private const string SheetName = "Nhật ký kiểm toán";

    private const string DocumentTitle = "NHẬT KÝ TRUY CẬP VÀ THAO TÁC HỆ THỐNG";

    private const string UnknownActor = "(không xác định)";

    private const string TimeNumberFormat = "yyyy-mm-dd hh:mm:ss";

    /// <summary>Tiêu đề cột — hàng 4. Hàng 1–2 là khối tự mô tả, hàng 3 để trống.</summary>
    private const int HeaderRow = 4;

    private static readonly string[] Headers =
    [
        "Thời gian (UTC)",
        "Người thao tác",
        "Số điện thoại",
        "Hành động",
        "Đối tượng",
        "Địa chỉ IP",
        "Mã người dùng",
    ];

    /// <summary>Độ rộng cột đặt cứng — xem chú thích ở <see cref="BuildExcel"/>.</summary>
    private static readonly double[] ColumnWidths = [22, 24, 16, 12, 30, 16, 38];

    private readonly AppDbContext _db;
    private readonly IReportPdfService _reportPdfService;
    private readonly IReportExcelService _reportExcelService;

    public AuditLogExportService(
        AppDbContext db,
        IReportPdfService reportPdfService,
        IReportExcelService reportExcelService)
    {
        _db = db;
        _reportPdfService = reportPdfService;
        _reportExcelService = reportExcelService;
    }

    public async Task<ServiceResult<AuditLogExportFile>> ExportAsync(
        ExportAuditLogsRequest request,
        string? exportedBy,
        CancellationToken cancellationToken)
    {
        // Kiểm định dạng TRƯỚC khi đếm/truy vấn: tham số hỏng không tốn truy vấn nào và lỗi gắn
        // đúng ô input. Trần dòng phụ thuộc định dạng nên phải chốt cap sau bước này.
        if (!ReportExportFormats.TryParse(request.Format, out var format))
        {
            return FormatError();
        }

        var cap = format == ReportExportFormat.Pdf ? MaxPdfRows : MaxRows;

        var todayUtc = DateOnly.FromDateTime(DateTime.UtcNow);
        var fromDate = request.From ?? todayUtc.AddDays(-(DefaultWindowDays - 1));
        var toDate = request.To ?? todayUtc;

        if (toDate < fromDate)
        {
            const string message = "Ngày kết thúc phải bằng hoặc sau ngày bắt đầu";

            return ServiceResult<AuditLogExportFile>.Invalid(
                message,
                new Dictionary<string, string[]> { ["to"] = [message] });
        }

        var query = BuildQuery(request, fromDate, toDate);

        // Chốt 1 — đếm trước, chưa nạp dòng nào. Vượt trần thì thoát ngay, không dựng workbook.
        var total = await query.CountAsync(cancellationToken);

        if (total > cap)
        {
            return TooManyRows(total, fromDate, toDate, cap, format);
        }

        var raw = await query
            .OrderByDescending(a => a.CreatedAt)
            .ThenBy(a => a.Id)
            .Take(cap + 1)
            .Select(a => new
            {
                a.CreatedAt,
                a.Action,
                a.Target,
                a.IpAddress,
                a.UserId,
            })
            .ToListAsync(cancellationToken);

        // Chốt 2 — chặn đua. Middleware ghi nhật ký ở request khác có thể chèn thêm dòng giữa
        // CountAsync và ToListAsync. Vẫn vượt thì TỪ CHỐI chứ không cắt bớt rồi xuất: một chứng
        // từ thiếu dòng mà không ai biết còn tệ hơn không có chứng từ nào.
        if (raw.Count > cap)
        {
            return TooManyRows(total, fromDate, toDate, cap, format);
        }

        var actors = await LoadActorsAsync(raw.Select(r => r.UserId), cancellationToken);

        var rows = new List<ExportRow>(raw.Count);

        foreach (var item in raw)
        {
            Actor? actor = item.UserId is { } userId && actors.TryGetValue(userId, out var found)
                ? found
                : null;

            rows.Add(new ExportRow(
                item.CreatedAt,
                item.Action,
                item.Target,
                item.IpAddress,
                item.UserId,
                actor?.FullName,
                actor?.PhoneNumber));
        }

        var generatedAtUtc = DateTime.UtcNow;

        return ServiceResult<AuditLogExportFile>.Ok(new AuditLogExportFile
        {
            Content = format == ReportExportFormat.Pdf
                ? BuildPdf(rows, fromDate, toDate, generatedAtUtc, exportedBy)
                : BuildExcel(rows, fromDate, toDate, generatedAtUtc, exportedBy),

            ContentType = format.ContentType(),

            // Tên file ASCII không dấu là CỐ Ý — xem chú thích ở AuditLogExportController.Export.
            // Cùng một biến generatedAtUtc dùng cho cả tên file lẫn dòng mô tả, nên hai chỗ
            // không bao giờ lệch nhau.
            FileName = $"nhat-ky-kiem-toan_{generatedAtUtc:yyyyMMdd-HHmmss}Z{format.Extension()}",
        });
    }

    private static ServiceResult<AuditLogExportFile> FormatError()
        => ServiceResult<AuditLogExportFile>.Invalid(
            ReportExportFormats.InvalidMessage,
            new Dictionary<string, string[]>
            {
                ["format"] = [ReportExportFormats.InvalidMessage],
            });

    private IQueryable<AuditLog> BuildQuery(
        ExportAuditLogsRequest request,
        DateOnly fromDate,
        DateOnly toDate)
    {
        // Nửa khoảng [from 00:00Z, to+1 ngày 00:00Z) — nhờ vậy CẢ HAI đầu mút được tính trọn
        // ngày mà không phải sinh mốc 23:59:59.9999999 (mốc đó bỏ sót đuôi của một giây).
        var fromUtc = fromDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toExclusiveUtc = toDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var query = _db.AuditLogs
            .AsNoTracking()
            .Where(a => a.CreatedAt >= fromUtc && a.CreatedAt < toExclusiveUtc);

        if (request.UserId is { } userId)
        {
            query = query.Where(a => a.UserId == userId);
        }

        if (!string.IsNullOrWhiteSpace(request.Action))
        {
            // Không khớp mã nào thì trả FILE RỖNG chứ không 400 — xem chú thích ở
            // ExportAuditLogsRequest.Action. Viết thành Where(false) để cả hai nhánh đi chung
            // một đường, khỏi phải rẽ nhánh cả luồng xử lý.
            query = TryParseAuditAction(request.Action, out var action)
                ? query.Where(a => a.Action == action)
                : query.Where(_ => false);
        }

        return query;
    }

    /// <summary>
    /// Nạp họ tên và SĐT của những người có mặt trong trang kết quả, bằng một truy vấn riêng.
    ///
    /// Cố ý KHÔNG dùng <c>Include(a =&gt; a.User)</c>: nó kéo về cả cột <c>PasswordHash</c>
    /// (hash BCrypt) chỉ để lấy hai trường hiển thị. Cũng không chiếu thẳng qua navigation
    /// <c>a.User.FullName</c> trong câu truy vấn chính, vì hành vi của phép JOIN trái đó khác
    /// nhau giữa Npgsql và provider InMemory của test — hai truy vấn cho kết quả xác định trên
    /// cả hai, và ở trần 10.000 dòng thì chi phí thêm không đáng kể.
    /// </summary>
    private async Task<Dictionary<Guid, Actor>> LoadActorsAsync(
        IEnumerable<Guid?> userIds,
        CancellationToken cancellationToken)
    {
        var ids = userIds
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
        {
            return [];
        }

        return await _db.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.PhoneNumber })
            .ToDictionaryAsync(
                u => u.Id,
                u => new Actor(u.FullName, u.PhoneNumber),
                cancellationToken);
    }

    private static ServiceResult<AuditLogExportFile> TooManyRows(
        int total,
        DateOnly fromDate,
        DateOnly toDate,
        int cap,
        ReportExportFormat format)
    {
        var formatLabel = format == ReportExportFormat.Pdf ? "PDF" : "Excel";

        var message =
            $"Khoảng {fromDate:yyyy-MM-dd} → {toDate:yyyy-MM-dd} có {total} bản ghi, " +
            $"vượt trần {cap} dòng mỗi lần xuất file {formatLabel}. " +
            "Hãy thu hẹp khoảng thời gian, hoặc lọc thêm theo người thao tác / hành động.";

        // Gắn lỗi vào CẢ HAI ô ngày vì cả hai đều phải đổi mới hẹp lại được — đúng tinh thần
        // "lỗi theo từng trường để form gắn vào ô input" của mục D3.
        return ServiceResult<AuditLogExportFile>.Invalid(message, new Dictionary<string, string[]>
        {
            ["from"] = [message],
            ["to"] = [message],
        });
    }

    private byte[] BuildExcel(
        IReadOnlyList<ExportRow> rows,
        DateOnly fromDate,
        DateOnly toDate,
        DateTime generatedAtUtc,
        string? exportedBy)
    {
        // Khối tự mô tả. File xuất ra là chứng từ RỜI KHỎI hệ thống — nằm trong thư mục tải về,
        // được gửi kèm email — nên nó phải tự nói được nó phủ khoảng nào và ai xuất.
        //
        // Đây cũng là chỗ DUY NHẤT ghi nhận việc xuất đã xảy ra: AuditLogMiddleware chỉ ghi
        // POST/PUT/PATCH/DELETE, nên chính thao tác xuất này không để lại dòng nào ở AuditLogs.
        var model = new ReportExcelModel
        {
            SheetName = SheetName,
            Title = DocumentTitle,
            Subtitle =
                $"Khoảng thời gian: {fromDate:yyyy-MM-dd} → {toDate:yyyy-MM-dd} (UTC, trọn ngày) · " +
                $"Số dòng: {rows.Count} · " +
                $"Xuất lúc: {generatedAtUtc:yyyy-MM-dd HH:mm:ss} UTC · " +
                $"Người xuất: {exportedBy ?? UnknownActor}",
            Headers = Headers,
            Rows = rows
                .Select(row => new ReportExcelRow
                {
                    // Ô thời gian PHẢI gán DateTime thật, không gán chuỗi: gán chuỗi thì ô thành
                    // text và người kiểm toán mất khả năng sắp xếp, lọc theo thời gian — mất nửa
                    // giá trị của bản xuất. Các cột còn lại để nguyên kiểu text nên Excel không
                    // diễn giải lại "0912345678" thành số.
                    Cells =
                    [
                        ReportExcelCell.Date(row.CreatedAt, TimeNumberFormat),
                        ReportExcelCell.Text(row.UserFullName ?? UnknownActor),
                        ReportExcelCell.Text(row.UserPhoneNumber),
                        ReportExcelCell.Text(row.Action.ToString()),
                        ReportExcelCell.Text(row.Target),
                        ReportExcelCell.Text(row.IpAddress),
                        ReportExcelCell.Text(row.UserId?.ToString()),
                    ],
                })
                .ToList(),

            // Độ rộng đặt CỨNG, không gọi AdjustToContent(): hàm đó phải đo font — đúng chỗ sinh
            // lỗi "missing font" trên Linux — và chậm trên sheet lớn. Cứng thì kết quả giống
            // nhau ở máy dev lẫn trên CI.
            ColumnWidths = ColumnWidths,
        };

        return _reportExcelService.GenerateExcel(model);
    }

    /// <summary>
    /// Bản PDF của cùng một bảng. Thời gian giữ khung UTC và in dạng chuỗi
    /// <c>yyyy-MM-dd HH:mm:ss</c> — PDF là bản để đọc/đối chiếu, không phải bản để lọc như Excel;
    /// ghi chuỗi có cùng múi giờ với nhãn cột "(UTC)" để người kiểm toán không cộng nhầm 7 tiếng.
    /// </summary>
    private byte[] BuildPdf(
        IReadOnlyList<ExportRow> rows,
        DateOnly fromDate,
        DateOnly toDate,
        DateTime generatedAtUtc,
        string? exportedBy)
    {
        var tableRows = rows
            .Select(row => (IReadOnlyList<string>)
            [
                row.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                row.UserFullName ?? UnknownActor,
                row.UserPhoneNumber ?? string.Empty,
                row.Action.ToString(),
                row.Target ?? string.Empty,
                row.IpAddress ?? string.Empty,
                row.UserId?.ToString() ?? string.Empty,
            ])
            .ToList();

        var model = new ReportPdfModel
        {
            Title = DocumentTitle,
            Subtitle =
                $"Khoảng thời gian: {fromDate:yyyy-MM-dd} → {toDate:yyyy-MM-dd} (UTC, trọn ngày) · " +
                $"Số dòng: {rows.Count}",
            ExportedAt = generatedAtUtc,
            GeneratedBy = exportedBy,

            // Bảng rỗng vẫn hợp lệ: khoảng lọc không có bản ghi thì file phải có đầu cột để người
            // đọc thấy "không có dòng nào", không phải một trang giấy trắng.
            Table = new ReportTable
            {
                Headers = Headers,
                Rows = tableRows,
            },

            FooterNote = "Thời gian hiển thị theo UTC, trọn ngày; bản ghi mới nhất ở trên.",
        };

        return _reportPdfService.GeneratePdf(model);
    }

    /// <summary>
    /// So khớp với danh sách tên của enum thay vì dùng <c>Enum.TryParse</c> — cùng lý do
    /// <c>RouteService.TryParseRouteStatus</c> và <c>FareService.TryParsePassengerType</c>:
    /// TryParse chấp nhận cả chuỗi số (<c>"3"</c> ra <c>Delete</c>), trái quy ước A3 là trạng thái
    /// lưu dạng chuỗi đọc được. Bỏ qua hoa/thường khi đọc.
    /// </summary>
    private static bool TryParseAuditAction(string? value, out AuditAction action)
    {
        var trimmed = value?.Trim();

        foreach (var name in Enum.GetNames<AuditAction>())
        {
            if (string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                action = Enum.Parse<AuditAction>(name);
                return true;
            }
        }

        action = default;
        return false;
    }

    /// <summary>Người thao tác — chỉ hai trường hiển thị, không kéo theo PasswordHash.</summary>
    private sealed record Actor(string FullName, string PhoneNumber);

    /// <summary>Một dòng dữ liệu đã ghép xong người thao tác, sẵn sàng ghi vào sheet.</summary>
    private sealed record ExportRow(
        DateTime CreatedAt,
        AuditAction Action,
        string? Target,
        string? IpAddress,
        Guid? UserId,
        string? UserFullName,
        string? UserPhoneNumber);
}
