using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.SeatHolds;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="ISeatHoldLookupService"/> — hợp đồng đầy đủ ở mục "Giữ chỗ — /seat-holds"
/// của docs/api-contract.md.
///
/// Chỉ đọc: không có thao tác ghi nào ở đây, nên cũng không có <c>SaveChangesAsync</c> — cùng lối
/// <see cref="MonthlyPassLookupService"/>. Điều kiện <c>UserId</c> nằm NGAY trong truy vấn nên
/// phiên của người khác không có đường lọt vào kết quả — không cần kiểm quyền sở hữu ở tầng trên.
/// </summary>
public class SeatHoldLookupService : ISeatHoldLookupService
{
    private const string SessionNotFoundMessage = "Không tìm thấy phiên giữ chỗ";

    private readonly AppDbContext _db;

    public SeatHoldLookupService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<SeatHoldSessionResponse>> GetBySessionCodeAsync(
        Guid userId,
        string sessionCode,
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.SeatHolds.AsNoTracking()
            .Where(h => h.SessionCode == sessionCode && h.UserId == userId)
            .OrderBy(h => h.CreatedAt)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return ServiceResult<SeatHoldSessionResponse>.NotFound(SessionNotFoundMessage);
        }

        // Trạng thái phiên là trạng thái CHUNG của mọi dòng (docs/26 §1: tạo / gia hạn / nhả / hết
        // hạn đều đổi theo nhóm). Dữ liệu lệch — khe hở có thật vì CSDL không chặn — thì ưu tiên
        // báo trạng thái "đang chặn ghế" trước, vì đó là điều người gọi cần biết sớm nhất.
        var status = StatusOf(rows.Select(r => r.Status));

        // Chốt "gia hạn tối đa 1 lần" (US 3) không có cột đếm trên SeatHolds — nó nằm ở unique
        // index (SeatHoldId, Action) của SeatHoldLogs (docs/26 §1): dòng Extended chỉ ghi được một
        // lần cho mỗi lượt giữ, nên hỏi bảng log là đủ biết còn lượt hay không. Hôm nay chưa có API
        // gia hạn nên chưa dòng Extended nào tồn tại — nhưng phiên đã kết thúc (Expired/Released/
        // Confirmed) vẫn phải trả canExtend = false.
        var holdIds = rows.Select(r => r.Id).ToList();

        var hasExtendedLog = await _db.SeatHoldLogs.AsNoTracking()
            .AnyAsync(l => holdIds.Contains(l.SeatHoldId) && l.Action == SeatHoldLogAction.Extended,
                cancellationToken);

        // Số ghế hiển thị theo đúng chỗ trên sơ đồ — cùng thứ tự vẽ của GET /trips/{id}/seats.
        var seatNumbers = await _db.Seats.AsNoTracking()
            .Where(s => rows.Select(r => r.SeatId).Contains(s.Id))
            .OrderBy(s => s.Floor)
            .ThenBy(s => s.RowIndex)
            .ThenBy(s => s.ColumnIndex)
            .Select(s => s.SeatNumber)
            .ToListAsync(cancellationToken);

        return ServiceResult<SeatHoldSessionResponse>.Ok(new SeatHoldSessionResponse
        {
            SessionCode = sessionCode,
            TripId = rows[0].TripId,
            SeatNumbers = seatNumbers,
            Status = status.ToString(),
            // Mọi dòng cùng phiên cùng hạn (đổi theo nhóm) nên hạn của dòng đầu là hạn của cả phiên.
            ExpiresAt = rows[0].ExpiresAt,
            CanExtend = status == SeatHoldStatus.Holding && !hasExtendedLog,
        });
    }

    /// <summary>
    /// Ưu tiên báo trạng thái "đang chặn ghế" trước (Holding), sau đó tới các trạng thái kết thúc
    /// theo vòng đời: Confirmed → Released → Expired.
    /// </summary>
    private static SeatHoldStatus StatusOf(IEnumerable<SeatHoldStatus> statuses)
    {
        if (statuses.Any(s => s == SeatHoldStatus.Holding))
        {
            return SeatHoldStatus.Holding;
        }

        if (statuses.Any(s => s == SeatHoldStatus.Confirmed))
        {
            return SeatHoldStatus.Confirmed;
        }

        if (statuses.Any(s => s == SeatHoldStatus.Released))
        {
            return SeatHoldStatus.Released;
        }

        return SeatHoldStatus.Expired;
    }
}
