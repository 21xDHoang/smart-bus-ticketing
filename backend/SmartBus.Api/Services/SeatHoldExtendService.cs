using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.SeatHolds;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="ISeatHoldExtendService"/> — hợp đồng đầy đủ ở mục "Giữ chỗ — /seat-holds"
/// của docs/api-contract.md.
///
/// Gia hạn = cập nhật hạn <c>ExpiresAt</c> của CẢ NHÓM dòng cùng <c>SessionCode</c> đang
/// <see cref="SeatHoldStatus.Holding"/> (docs/26 §1: tạo / gia hạn / nhả / hết hạn đều đổi theo
/// nhóm) và ghi một dòng <see cref="SeatHoldLog"/> <c>Action = Extended</c> cho mỗi lượt vừa gia
/// hạn (docs/26 §6).
///
/// Luật "tối đa 1 lần" (US 3) có chốt ở tầng CSDL — unique index <c>(SeatHoldId, Action)</c> của
/// <see cref="SeatHoldLog"/> (AppDbContext.Seat.cs) — nhưng service vẫn kiểm tra trước để trả lỗi
/// có thông báo, không để khách nhận lỗi unique thô (docs/26 §6). Chốt đó cũng là lớp chặn cuối
/// cho hai request gia hạn đồng thời: request thua đâm vào unique khi <c>SaveChanges</c>, bắt
/// <see cref="DbUpdateException"/> và trả cùng câu 409.
/// </summary>
public class SeatHoldExtendService : ISeatHoldExtendService
{
    private const string SessionNotFoundMessage = "Không tìm thấy phiên giữ chỗ";
    private const string SessionEndedMessage = "Phiên giữ chỗ đã kết thúc, không thể gia hạn.";
    private const string AlreadyExtendedMessage =
        "Phiên giữ chỗ đã được gia hạn rồi — mỗi phiên chỉ được gia hạn tối đa 1 lần.";

    /// <summary>Mỗi lần gia hạn cộng thêm đúng một cửa sổ giữ chỗ 10 phút của US 3.</summary>
    private const int ExtensionMinutes = 10;

    private readonly AppDbContext _db;

    public SeatHoldExtendService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<SeatHoldSessionResponse>> ExtendAsync(
        Guid userId,
        string sessionCode,
        CancellationToken cancellationToken = default)
    {
        // Nạp entity (tracked) chứ không AsNoTracking — gia hạn là thao tác GHI lên chính các dòng
        // này. Điều kiện UserId nằm NGAY trong truy vấn nên phiên của người khác không có đường
        // lọt vào kết quả — không cần kiểm quyền sở hữu ở tầng trên (cùng lối SeatHoldLookupService).
        var rows = await _db.SeatHolds
            .Where(h => h.SessionCode == sessionCode && h.UserId == userId)
            .OrderBy(h => h.CreatedAt)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return ServiceResult<SeatHoldSessionResponse>.NotFound(SessionNotFoundMessage);
        }

        // Gia hạn chỉ tác động lên dòng ĐANG GIỮ: dòng đã Expired / Released / Confirmed là đã xong
        // vòng đời, không hồi sinh. Không còn dòng nào Holding = phiên đã kết thúc → 409.
        var holdingRows = rows.Where(r => r.Status == SeatHoldStatus.Holding).ToList();

        if (holdingRows.Count == 0)
        {
            return ServiceResult<SeatHoldSessionResponse>.Conflict(SessionEndedMessage);
        }

        // Kiểm tra lượt gia hạn trước khi ghi (docs/26 §6): dòng Extended chỉ ghi được ĐÚNG MỘT LẦN
        // cho mỗi lượt giữ nhờ unique index (SeatHoldId, Action) — hỏi bảng log là đủ biết còn lượt.
        var holdingIds = holdingRows.Select(r => r.Id).ToList();

        var alreadyExtended = await _db.SeatHoldLogs.AsNoTracking()
            .AnyAsync(l => holdingIds.Contains(l.SeatHoldId) && l.Action == SeatHoldLogAction.Extended,
                cancellationToken);

        if (alreadyExtended)
        {
            return ServiceResult<SeatHoldSessionResponse>.Conflict(AlreadyExtendedMessage);
        }

        // Hạn mới = thời điểm gia hạn + 10 phút, KHÔNG phải hạn cũ + 10 phút: khách luôn nhận trọn
        // vẹn một cửa sổ 10 phút mới kể từ lúc bấm gia hạn — giải thích đầy đủ ở mục "Vì sao" của
        // docs/api-contract.md.
        var now = DateTime.UtcNow;
        var newExpiresAt = now.AddMinutes(ExtensionMinutes);

        foreach (var hold in holdingRows)
        {
            hold.ExpiresAt = newExpiresAt;

            // A4: lượt giữ có sửa dữ liệu nên có UpdatedAt. Ghi cùng mốc now đã dùng cho hạn mới —
            // hai mốc lệch vài mili giây trong cùng một request là thứ không giải thích được lúc
            // tra vết (cùng lối SeatHoldExpiryService).
            hold.UpdatedAt = now;

            _db.SeatHoldLogs.Add(new SeatHoldLog
            {
                SeatHoldId = hold.Id,
                UserId = hold.UserId,
                SessionCode = hold.SessionCode,
                Action = SeatHoldLogAction.Extended,
                CreatedAt = now,
            });
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Hai request gia hạn đồng thời cùng vượt qua kiểm tra "đã gia hạn chưa": request thua
            // đâm vào unique index (SeatHoldId, Action) của SeatHoldLogs (AppDbContext.Seat.cs) và
            // nhận DbUpdateException ở đây. Trả cùng câu 409 của ca "đã gia hạn rồi" thay vì lỗi
            // unique thô — người gọi không phải phân biệt hai đường. Provider InMemory của bộ test
            // không cưỡng chế unique nên nhánh này chỉ chạy ở PostgreSQL (docs/26 §1).
            return ServiceResult<SeatHoldSessionResponse>.Conflict(AlreadyExtendedMessage);
        }

        // Trả về đúng hình dạng SeatHoldSession chung của bề mặt, với hạn mới và canExtend = false
        // — vừa dùng hết lượt. Phần dựng response giống SeatHoldLookupService; chép lại cố ý vì
        // hai task đứng ở hai nhánh riêng, mỗi task một controller/service.
        var status = StatusOf(rows.Select(r => r.Status));

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
            // Hạn của dòng đang giữ — mọi dòng Holding vừa được đặt cùng một hạn mới (đổi theo nhóm).
            ExpiresAt = holdingRows[0].ExpiresAt,
            CanExtend = false,
        });
    }

    /// <summary>
    /// Ưu tiên báo trạng thái "đang chặn ghế" trước (Holding), sau đó tới các trạng thái kết thúc
    /// theo vòng đời: Confirmed → Released → Expired — cùng hàm của SeatHoldLookupService.
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
