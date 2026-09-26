using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.AuditLogs;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Truy vấn danh sách nhật ký kiểm toán — task story 23, Nguyễn Duy Kiên.
/// Hợp đồng đầy đủ ở mục "Nhật ký kiểm toán" của docs/api-contract.md.
///
/// Chỉ ĐỌC bảng AuditLogs. Bốn bộ lọc và cách dựng truy vấn bám sát
/// <see cref="AuditLogExportService"/> để hai đường xem nhật ký cho ra cùng một kết quả với cùng
/// một bộ tham số — khác nhau chỉ ở chỗ đường này phân trang thay vì có trần dòng.
/// </summary>
public sealed class AuditLogQueryService : IAuditLogQueryService
{
    /// <summary>Bỏ trống cả from lẫn to thì lấy 30 ngày gần nhất, TÍNH CẢ hôm nay.</summary>
    private const int DefaultWindowDays = 30;

    /// <summary>Trùng con số của RouteService và AdminUserService — ba màn hình danh sách cùng cỡ trang.</summary>
    private const int DefaultPageSize = 10;

    private const string DateRangeMessage = "Ngày kết thúc phải bằng hoặc sau ngày bắt đầu";

    private readonly AppDbContext _db;

    public AuditLogQueryService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<AuditLogListResponse>> ListAsync(
        ListAuditLogsRequest request,
        CancellationToken cancellationToken)
    {
        var todayUtc = DateOnly.FromDateTime(DateTime.UtcNow);
        var fromDate = request.From ?? todayUtc.AddDays(-(DefaultWindowDays - 1));
        var toDate = request.To ?? todayUtc;

        if (toDate < fromDate)
        {
            return ServiceResult<AuditLogListResponse>.Invalid(
                DateRangeMessage,
                new Dictionary<string, string[]> { ["to"] = [DateRangeMessage] });
        }

        // Tính trang TRƯỚC khi dựng truy vấn: nhánh action không khớp mã nào vẫn phải trả về đúng
        // page/pageSize mà client đã gửi, chứ không phải 1/10 như mặc định.
        var page = request.Page ?? 1;
        var pageSize = request.PageSize ?? DefaultPageSize;

        var query = BuildQuery(request, fromDate, toDate);

        var total = await query.CountAsync(cancellationToken);

        // Nhân bằng long rồi mới ép về int. Page nhận tới int.MaxValue (xem [Range] ở
        // ListAuditLogsRequest) nên (page - 1) * pageSize tính bằng int sẽ TRÀN thành số âm, và
        // Skip với số âm bị coi như 0 — envelope sẽ nói "page": 2000000000 trong khi thực chất trả
        // về trang 1, tức nói dối. Kẹp về int.MaxValue là đủ: Total là int nên không bảng nào có
        // quá int.MaxValue dòng khớp, và Skip quá cuối bảng vốn đã trả về rỗng.
        var skip = (long)(page - 1) * pageSize;

        var rows = await query
            .OrderByDescending(a => a.CreatedAt)
            // Chốt thêm theo Id: middleware ghi nhiều bản ghi trong cùng một giây, thiếu khoá phụ
            // thì thứ tự giữa các trang không ổn định và phân trang bị trùng/thiếu dòng — cùng
            // chiêu RouteService và AuditLogExportService đã dùng.
            .ThenBy(a => a.Id)
            .Skip((int)Math.Min(skip, int.MaxValue))
            .Take(pageSize)
            .Select(a => new AuditLogRow(
                a.Id,
                a.UserId,
                a.Action,
                a.Target,
                a.IpAddress,
                a.CreatedAt))
            .ToListAsync(cancellationToken);

        var actors = await LoadActorsAsync(rows.Select(r => r.UserId), cancellationToken);

        var items = new List<AuditLogResponse>(rows.Count);

        foreach (var row in rows)
        {
            Actor? actor = row.UserId is { } userId && actors.TryGetValue(userId, out var found)
                ? found
                : null;

            items.Add(new AuditLogResponse
            {
                Id = row.Id,
                UserId = row.UserId,
                UserFullName = actor?.FullName,
                UserPhoneNumber = actor?.PhoneNumber,
                Action = row.Action.ToString(),
                Target = row.Target,
                IpAddress = row.IpAddress,
                CreatedAt = row.CreatedAt,
            });
        }

        return ServiceResult<AuditLogListResponse>.Ok(new AuditLogListResponse
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize,
        });
    }

    private IQueryable<AuditLog> BuildQuery(
        ListAuditLogsRequest request,
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
            // Không khớp mã nào thì trả DANH SÁCH RỖNG chứ không 400 — xem chú thích ở
            // ListAuditLogsRequest.Action. Viết thành Where(false) để cả hai nhánh đi chung một
            // đường, khỏi phải rẽ nhánh cả luồng xử lý.
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
    /// nhau giữa Npgsql và provider InMemory của test — hai truy vấn cho kết quả xác định trên cả
    /// hai, và ở cỡ trang tối đa 100 dòng thì chi phí thêm không đáng kể.
    ///
    /// Bản sao của <see cref="AuditLogExportService"/>: hàm ở đó là private nên không gọi chéo
    /// được, mà tách lớp dùng chung thì phải sửa file của người khác (quy ước E1).
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

    /// <summary>
    /// So khớp với danh sách tên của enum thay vì dùng <c>Enum.TryParse</c> — cùng lý do
    /// <c>RouteService.TryParseRouteStatus</c> và <c>FareService.TryParsePassengerType</c>:
    /// TryParse chấp nhận cả chuỗi số (<c>"3"</c> ra <c>Delete</c>), trái quy ước A3 là trạng thái
    /// lưu dạng chuỗi đọc được. Bỏ qua hoa/thường khi đọc.
    ///
    /// Bản sao của <see cref="AuditLogExportService"/> — xem lý do ở <see cref="LoadActorsAsync"/>.
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

    /// <summary>Một dòng nhật ký đã đọc xong, còn thiếu phần người thao tác.</summary>
    private sealed record AuditLogRow(
        Guid Id,
        Guid? UserId,
        AuditAction Action,
        string? Target,
        string? IpAddress,
        DateTime CreatedAt);
}
