using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.SeatHolds;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="ISeatHoldReleaseService"/> — hợp đồng đầy đủ ở mục "Giữ chỗ — /seat-holds"
/// của docs/api-contract.md.
///
/// Nhả = lật CẢ NHÓM dòng cùng <c>SessionCode</c> đang <see cref="SeatHoldStatus.Holding"/> sang
/// <see cref="SeatHoldStatus.Released"/> (docs/26 §1: tạo / gia hạn / nhả / hết hạn đều đổi theo
/// nhóm) và ghi một dòng <see cref="SeatHoldLog"/> <c>Action = Released</c> cho mỗi lượt vừa nhả
/// (docs/26 §6).
///
/// KHÁC API gia hạn ở hai điểm có chủ đích, cùng một lý do "nhả là thao tác KẾT THÚC":
///   • Không kiểm <c>ExpiresAt</c> — nhận cả phiên vừa quá hạn mà job nền chưa quét (khe hở dưới
///     một phút giữa hai lượt quét), để ghế về sơ đồ ngay chứ không đợi lượt quét kế tiếp.
///   • Phiên đã Expired / Released từ trước trả 200 với đúng trạng thái hiện tại (idempotent),
///     không trả 409: đích đến của người gọi đã đạt, gọi lại bao nhiêu lần vẫn xong.
/// </summary>
public class SeatHoldReleaseService : ISeatHoldReleaseService
{
    private const string SessionNotFoundMessage = "Không tìm thấy phiên giữ chỗ";
    private const string SessionConfirmedMessage = "Phiên giữ chỗ đã chốt thành vé, không thể nhả.";

    private readonly AppDbContext _db;

    public SeatHoldReleaseService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<SeatHoldSessionResponse>> ReleaseAsync(
        Guid userId,
        string sessionCode,
        CancellationToken cancellationToken = default)
    {
        // Nạp entity (tracked) chứ không AsNoTracking — nhả là thao tác GHI lên chính các dòng này.
        // Điều kiện UserId nằm NGAY trong truy vấn nên phiên của người khác không có đường lọt vào
        // kết quả — không cần kiểm quyền sở hữu ở tầng trên (cùng lối SeatHoldExtendService).
        var rows = await _db.SeatHolds
            .Where(h => h.SessionCode == sessionCode && h.UserId == userId)
            .OrderBy(h => h.CreatedAt)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return ServiceResult<SeatHoldSessionResponse>.NotFound(SessionNotFoundMessage);
        }

        // Kiểm Confirmed TRƯỚC cả dòng Holding: phiên đã chốt thành vé là phiên đã xong. Nhả riêng
        // dòng Holding còn sót (nếu dữ liệu lệch) là viết Released vào một phiên khách đã trả tiền
        // cho CẢ phiên — chặn nguyên phiên để con người xem dữ liệu lệch, không tự đoán ý. Giải
        // thích đầy đủ ở mục "Vì sao Confirmed là 409 mà không nhả" của docs/api-contract.md.
        if (rows.Any(r => r.Status == SeatHoldStatus.Confirmed))
        {
            return ServiceResult<SeatHoldSessionResponse>.Conflict(SessionConfirmedMessage);
        }

        var holdingRows = rows.Where(r => r.Status == SeatHoldStatus.Holding).ToList();

        if (holdingRows.Count > 0)
        {
            // Cố ý KHÔNG kiểm ExpiresAt: phiên vừa quá hạn mà job nền (quét mỗi phút) chưa lật vẫn
            // còn Holding ở đây, và nhả ngay là đúng thứ nút "Huỷ" cần — nhả sớm vô hại vì đích
            // đến giống hệt lượt quét của job, chỉ khác người ghi log (Released thay vì Expired).
            var now = DateTime.UtcNow;

            foreach (var hold in holdingRows)
            {
                hold.Status = SeatHoldStatus.Released;

                // A4: lượt giữ có sửa dữ liệu nên có UpdatedAt. Ghi cùng mốc now dùng cho log —
                // hai mốc lệch vài mili giây trong cùng một request là thứ không giải thích được
                // lúc tra vết (cùng lối SeatHoldExtendService).
                hold.UpdatedAt = now;

                _db.SeatHoldLogs.Add(new SeatHoldLog
                {
                    SeatHoldId = hold.Id,
                    UserId = hold.UserId,
                    SessionCode = hold.SessionCode,
                    Action = SeatHoldLogAction.Released,
                    CreatedAt = now,
                });
            }

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Hai request nhả đồng thời cùng thấy Holding: request thua đâm vào unique index
                // (SeatHoldId, Action) của SeatHoldLogs (AppDbContext.Seat.cs) và nhận
                // DbUpdateException ở đây. KHÁC API gia hạn (cũng bắt DbUpdateException nhưng trả
                // 409 "đã gia hạn rồi"): đâm unique nghĩa là request kia ĐÃ nhả xong — đích đến đã
                // đạt nên rơi xuống dưới trả 200 như ca thành công; dòng tracked đã mang
                // Status = Released, khớp trạng thái CSDL sau lượt nhả thắng. Provider InMemory của
                // bộ test không cưỡng chế unique nên nhánh này chỉ chạy ở PostgreSQL (docs/26 §1).
            }
        }

        // Không còn Holding (đã Expired / Released từ trước) cũng rơi xuống đây: trả đúng trạng
        // thái hiện tại của phiên, không ghi gì — cố ý idempotent, xem đầu file.
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
            // Nhả KHÔNG đổi ExpiresAt — hạn cũ giữ nguyên, chỉ trạng thái đổi (docs/26 §1). Mọi
            // dòng cùng phiên chung một hạn nên rows[0] là đại diện đủ.
            ExpiresAt = rows[0].ExpiresAt,
            CanExtend = false,
        });
    }

    /// <summary>
    /// Ưu tiên báo trạng thái "đang chặn ghế" trước (Holding), sau đó tới các trạng thái kết thúc
    /// theo vòng đời: Confirmed → Released → Expired — cùng hàm của SeatHoldLookupService và
    /// SeatHoldExtendService.
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
