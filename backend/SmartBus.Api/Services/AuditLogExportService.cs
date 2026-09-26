using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.AuditLogs;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Dựng file Excel nhật ký kiểm toán — task story 23, Phùng Duy Hoàng.
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

    /// <summary>Độ rộng cột đặt cứng — xem chú thích ở <see cref="BuildWorkbook"/>.</summary>
    private static readonly double[] ColumnWidths = [22, 24, 16, 12, 30, 16, 38];

    private readonly AppDbContext _db;

    public AuditLogExportService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<AuditLogExportFile>> ExportAsync(
        ExportAuditLogsRequest request,
        string? exportedBy,
        CancellationToken cancellationToken)
    {
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

        if (total > MaxRows)
        {
            return TooManyRows(total, fromDate, toDate);
        }

        var raw = await query
            .OrderByDescending(a => a.CreatedAt)
            .ThenBy(a => a.Id)
            .Take(MaxRows + 1)
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
        if (raw.Count > MaxRows)
        {
            return TooManyRows(total, fromDate, toDate);
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
            Content = BuildWorkbook(rows, fromDate, toDate, generatedAtUtc, exportedBy),

            // Tên file ASCII không dấu là CỐ Ý — xem chú thích ở AuditLogExportController.Export.
            // Cùng một biến generatedAtUtc dùng cho cả tên file lẫn dòng mô tả, nên hai chỗ
            // không bao giờ lệch nhau.
            FileName = $"nhat-ky-kiem-toan_{generatedAtUtc:yyyyMMdd-HHmmss}Z.xlsx",
        });
    }

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
        DateOnly toDate)
    {
        var message =
            $"Khoảng {fromDate:yyyy-MM-dd} → {toDate:yyyy-MM-dd} có {total} bản ghi, " +
            $"vượt trần {MaxRows} dòng mỗi lần xuất. Hãy thu hẹp khoảng thời gian, " +
            "hoặc lọc thêm theo người thao tác / hành động.";

        // Gắn lỗi vào CẢ HAI ô ngày vì cả hai đều phải đổi mới hẹp lại được — đúng tinh thần
        // "lỗi theo từng trường để form gắn vào ô input" của mục D3.
        return ServiceResult<AuditLogExportFile>.Invalid(message, new Dictionary<string, string[]>
        {
            ["from"] = [message],
            ["to"] = [message],
        });
    }

    private static byte[] BuildWorkbook(
        IReadOnlyList<ExportRow> rows,
        DateOnly fromDate,
        DateOnly toDate,
        DateTime generatedAtUtc,
        string? exportedBy)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SheetName);

        // Khối tự mô tả. File xuất ra là chứng từ RỜI KHỎI hệ thống — nằm trong thư mục tải về,
        // được gửi kèm email — nên nó phải tự nói được nó phủ khoảng nào và ai xuất.
        //
        // Đây cũng là chỗ DUY NHẤT ghi nhận việc xuất đã xảy ra: AuditLogMiddleware chỉ ghi
        // POST/PUT/PATCH/DELETE, nên chính thao tác xuất này không để lại dòng nào ở AuditLogs.
        sheet.Cell(1, 1).Value = DocumentTitle;
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 14;

        sheet.Cell(2, 1).Value =
            $"Khoảng thời gian: {fromDate:yyyy-MM-dd} → {toDate:yyyy-MM-dd} (UTC, trọn ngày) · " +
            $"Số dòng: {rows.Count} · " +
            $"Xuất lúc: {generatedAtUtc:yyyy-MM-dd HH:mm:ss} UTC · " +
            $"Người xuất: {exportedBy ?? UnknownActor}";

        for (var i = 0; i < Headers.Length; i++)
        {
            var cell = sheet.Cell(HeaderRow, i + 1);
            cell.Value = Headers[i];
            cell.Style.Font.Bold = true;
        }

        var rowIndex = HeaderRow + 1;

        foreach (var row in rows)
        {
            // Ô thời gian PHẢI gán DateTime thật, không gán chuỗi: gán chuỗi thì ô thành text và
            // người kiểm toán mất khả năng sắp xếp, lọc theo thời gian — mất nửa giá trị của
            // bản xuất. Các cột còn lại để nguyên kiểu text nên Excel không diễn giải lại
            // "0912345678" thành số.
            var timeCell = sheet.Cell(rowIndex, 1);
            timeCell.Value = row.CreatedAt;
            timeCell.Style.DateFormat.Format = TimeNumberFormat;

            sheet.Cell(rowIndex, 2).Value = row.UserFullName ?? UnknownActor;
            sheet.Cell(rowIndex, 3).Value = row.UserPhoneNumber ?? string.Empty;
            sheet.Cell(rowIndex, 4).Value = row.Action.ToString();
            sheet.Cell(rowIndex, 5).Value = row.Target ?? string.Empty;
            sheet.Cell(rowIndex, 6).Value = row.IpAddress ?? string.Empty;
            sheet.Cell(rowIndex, 7).Value = row.UserId?.ToString() ?? string.Empty;

            rowIndex++;
        }

        // Độ rộng đặt CỨNG, không gọi AdjustToContent(): hàm đó phải đo font — đúng chỗ sinh lỗi
        // "missing font" trên Linux — và chậm trên sheet lớn. Cứng thì kết quả giống nhau ở máy
        // dev lẫn trên CI.
        for (var i = 0; i < ColumnWidths.Length; i++)
        {
            sheet.Column(i + 1).Width = ColumnWidths[i];
        }

        sheet.Range(HeaderRow, 1, HeaderRow, Headers.Length).SetAutoFilter();
        sheet.SheetView.FreezeRows(HeaderRow);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return stream.ToArray();
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
