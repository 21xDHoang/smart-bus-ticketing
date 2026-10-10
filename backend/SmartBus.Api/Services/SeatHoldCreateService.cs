using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.SeatHolds;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="ISeatHoldCreateService"/> — hợp đồng đầy đủ ở mục "Giữ chỗ — /seat-holds"
/// của docs/api-contract.md.
///
/// Giữ ghế = ghi một dòng <see cref="SeatHold"/> <c>Status = Holding</c> cho MỖI ghế, tất cả cùng
/// một <c>SessionCode</c> do service sinh ra (cột này KHÔNG unique — docs/26 §1), cùng một
/// <c>ExpiresAt</c> = lúc tạo + 10 phút (cửa sổ giữ chỗ của US 3), kèm một dòng
/// <see cref="SeatHoldLog"/> <c>Action = Held</c> cho mỗi lượt vừa tạo (docs/26 §6).
///
/// Chống hai khách giữ cùng một ghế có HAI lớp, và cả hai đều cần (docs/26 §3):
///   1. Phép kiểm ở đây — trả 409 kèm số ghế vướng, ca thường gặp, khách hiểu ngay.
///   2. Partial unique index <c>(TripId, SeatId) WHERE Status = 'Holding'</c> của
///      <see cref="SeatHold"/> (AppDbContext.Seat.cs) — lớp chặn thật cho hai request đồng thời,
///      vì cả hai cùng đọc thấy ghế trống rồi cùng ghi. Request thua đâm vào index lúc
///      <c>SaveChanges</c> và nhận <see cref="DbUpdateException"/>.
///
/// Cả phiên ghi trong MỘT <c>SaveChangesAsync</c>: hỏng là hỏng cả phiên, không để lại phiên giữ
/// được nửa số ghế mà khách tưởng đã giữ đủ.
/// </summary>
public class SeatHoldCreateService : ISeatHoldCreateService
{
    private const string TripNotFoundMessage = "Không tìm thấy chuyến xe";
    private const string TripCancelledMessage = "Chuyến đã hủy, không thể giữ ghế";
    private const string TripCompletedMessage = "Chuyến đã hoàn thành, không thể giữ ghế";

    private const string EmptySeatIdsMessage = "Danh sách ghế không được để trống";
    private const string EmptySeatIdMessage = "Danh sách ghế có ghế không hợp lệ";
    private const string DuplicateSeatIdsMessage = "Danh sách ghế có ghế bị lặp lại";
    private const string SeatNotFoundMessage = "Danh sách ghế có ghế không tồn tại";
    private const string SeatNotOnTripBusMessage = "Danh sách ghế có ghế không thuộc xe của chuyến";

    /// <summary>
    /// Hai request đồng thời cùng giữ một ghế: cả hai vượt qua phép kiểm ở tầng service, request
    /// thua đâm vào partial unique index và nhận <see cref="DbUpdateException"/> — lúc đó không còn
    /// biết ghế nào vừa bị giành, nên câu thông báo là câu chung, không kèm số ghế như nhánh kiểm
    /// trước.
    /// </summary>
    private const string SeatTakenRaceMessage =
        "Một hoặc nhiều ghế vừa được người khác giữ, vui lòng chọn ghế khác.";

    /// <summary>Cửa sổ giữ chỗ của US 3 — cùng con số với lượt gia hạn của SeatHoldExtendService.</summary>
    private const int HoldMinutes = 10;

    /// <summary>
    /// Tiền tố mã phiên. Độ dài: 6 + 8 = 14 ký tự, thừa sức chứa <c>varchar(64)</c> của cột
    /// <c>SessionCode</c> (AppDbContext.Seat.cs) và khớp ví dụ <c>PHIEN-8f3a2c1d</c> trong hợp đồng.
    /// </summary>
    private const string SessionCodePrefix = "PHIEN-";

    private readonly AppDbContext _db;

    public SeatHoldCreateService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<SeatHoldSessionResponse>> CreateAsync(
        Guid userId,
        CreateSeatHoldRequest request,
        CancellationToken cancellationToken = default)
    {
        // ── Kiểm hình dạng danh sách ghế. Lặp lại [MinLength] của DTO vì service còn có thể được
        //    gọi từ service khác — ở đó model binding không chạy, không có gì chặn hộ (cùng lối
        //    TripDriverAssignmentService). ──
        if (request.SeatIds.Count == 0)
        {
            return InvalidSeatIds(EmptySeatIdsMessage);
        }

        // Guid.Empty không trỏ tới ghế nào, mà lọt xuống truy vấn thì chỉ đổ ra "không tìm thấy ghế"
        // — chặn sớm để câu lỗi nói đúng bệnh của dữ liệu gửi lên.
        if (request.SeatIds.Contains(Guid.Empty))
        {
            return InvalidSeatIds(EmptySeatIdMessage);
        }

        // Chặn hẳn thay vì âm thầm bỏ dòng lặp: gửi lên một danh sách có ghế lặp là client hiểu sai
        // dữ liệu của chính nó — cùng lối TripDriverAssignmentService với TripIds.
        if (request.SeatIds.Distinct().Count() != request.SeatIds.Count)
        {
            return InvalidSeatIds(DuplicateSeatIdsMessage);
        }

        // ── Chuyến phải tồn tại và còn nhận giữ ghế. ──
        var trip = await _db.Trips.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.TripId, cancellationToken);

        if (trip is null)
        {
            return ServiceResult<SeatHoldSessionResponse>.NotFound(TripNotFoundMessage);
        }

        // Giữ ghế trên chuyến đã huỷ/đã chạy xong là giữ một chỗ không còn tồn tại — cùng câu chữ và
        // cùng lối chặn 409 của ITripAssignmentService / IRouteTripsService khi đổi xe, đổi tài xế.
        if (trip.Status == TripStatus.Cancelled)
        {
            return ServiceResult<SeatHoldSessionResponse>.Conflict(TripCancelledMessage);
        }

        if (trip.Status == TripStatus.Completed)
        {
            return ServiceResult<SeatHoldSessionResponse>.Conflict(TripCompletedMessage);
        }

        // ── Ghế phải tồn tại và thuộc ĐÚNG xe của chuyến. ──
        var seats = await _db.Seats.AsNoTracking()
            .Where(s => request.SeatIds.Contains(s.Id))
            .ToListAsync(cancellationToken);

        if (seats.Count != request.SeatIds.Count)
        {
            return InvalidSeatIds(SeatNotFoundMessage);
        }

        // Một ghế của xe khác là CẢ danh sách bị chặn, không âm thầm bỏ qua rồi giữ phần còn lại —
        // cùng lối "một chuyến của tuyến khác là cả lô bị chặn" của TripDriverAssignmentService.
        // Đây cũng là trần số ghế: không thể giữ nhiều ghế hơn sức chứa của xe.
        if (seats.Any(s => s.BusId != trip.BusId))
        {
            return InvalidSeatIds(SeatNotOnTripBusMessage);
        }

        // ── Ghế đã có người giữ chưa. Điều kiện Status = Holding nằm ngay trong truy vấn, khớp
        //    đúng bộ lọc của partial unique index — dòng Confirmed/Released/Expired không chặn. ──
        var heldSeatIds = await _db.SeatHolds.AsNoTracking()
            .Where(h => h.TripId == trip.Id
                && request.SeatIds.Contains(h.SeatId)
                && h.Status == SeatHoldStatus.Holding)
            .Select(h => h.SeatId)
            .ToListAsync(cancellationToken);

        if (heldSeatIds.Count > 0)
        {
            return ServiceResult<SeatHoldSessionResponse>.Conflict(SeatTakenMessage(seats, heldSeatIds));
        }

        // ── Sinh phiên và ghi cả nhóm trong một lượt. ──
        // Mã phiên sinh ở ĐÂY, không nhận từ client: nó là khoá tra cứu của cả phiên (GET, gia hạn,
        // nhả đều hỏi theo nó) mà cột SessionCode không unique, nên hai khách gửi trùng mã sẽ trộn
        // hai phiên vào nhau — chốt phải nằm ở chỗ duy nhất sinh ra mã.
        var sessionCode = SessionCodePrefix + Guid.NewGuid().ToString("N")[..8];

        // MỘT mốc now cho ExpiresAt, CreatedAt và log — hai mốc lệch vài mili giây trong cùng một
        // request là thứ không giải thích được lúc tra vết (cùng lối SeatHoldExtendService).
        var now = DateTime.UtcNow;
        var expiresAt = now.AddMinutes(HoldMinutes);

        foreach (var seat in seats)
        {
            var hold = new SeatHold
            {
                TripId = trip.Id,
                SeatId = seat.Id,
                UserId = userId,
                SessionCode = sessionCode,
                Status = SeatHoldStatus.Holding,
                ExpiresAt = expiresAt,
                CreatedAt = now,
                // A4: dòng mới chưa sửa nên UpdatedAt = null.
            };

            _db.SeatHolds.Add(hold);

            // docs/26 §6: mốc MỞ ĐẦU của vòng đời cũng phải vào nhật ký. Thiếu dòng Held thì câu hỏi
            // "tài khoản này đã giữ chỗ bao nhiêu lần" (job cảnh báo của Dăm, đếm thẳng trên bảng log
            // theo chỉ mục (UserId, CreatedAt)) đếm thiếu đúng những lượt giữ thành công.
            _db.SeatHoldLogs.Add(new SeatHoldLog
            {
                // SeatHoldId gán thẳng từ hold.Id (đã có sẵn nhờ initializer của entity) thay vì
                // gán navigation SeatHold — cùng lối SeatHoldExtendService, tránh để EF phải suy ra
                // thứ tự chèn giữa hai bảng.
                SeatHoldId = hold.Id,
                UserId = userId,
                SessionCode = sessionCode,
                Action = SeatHoldLogAction.Held,
                CreatedAt = now,
            });
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Hai request giữ ghế đồng thời cùng vượt qua phép kiểm ở trên: request thua đâm vào
            // partial unique index (TripId, SeatId) WHERE Status = 'Holding' của SeatHolds
            // (AppDbContext.Seat.cs) và nhận DbUpdateException ở đây. Trả 409 thay vì lỗi unique thô
            // — người gọi không phải phân biệt hai đường. Provider InMemory của bộ test không dựng
            // unique index nên nhánh này chỉ chạy ở PostgreSQL (docs/26 §1, §3).
            return ServiceResult<SeatHoldSessionResponse>.Conflict(SeatTakenRaceMessage);
        }

        // Trả về đúng hình dạng SeatHoldSession chung của bề mặt. Sắp số ghế theo toạ độ sơ đồ ngay
        // trên danh sách vừa nạp thay vì truy vấn lại Seats: cùng bộ ba khoá và cùng chiều với
        // SeatHoldLookupService (Floor → RowIndex → ColumnIndex), mà không tốn thêm một vòng CSDL
        // cho dữ liệu đã nằm trong tay.
        var seatNumbers = seats
            .OrderBy(s => s.Floor)
            .ThenBy(s => s.RowIndex)
            .ThenBy(s => s.ColumnIndex)
            .Select(s => s.SeatNumber)
            .ToList();

        return ServiceResult<SeatHoldSessionResponse>.Ok(new SeatHoldSessionResponse
        {
            SessionCode = sessionCode,
            TripId = trip.Id,
            SeatNumbers = seatNumbers,
            Status = SeatHoldStatus.Holding.ToString(),
            ExpiresAt = expiresAt,
            // Phiên vừa sinh chưa có dòng log Extended nào — còn nguyên lượt gia hạn của US 3.
            CanExtend = true,
        });
    }

    /// <summary>
    /// Câu 409 của ca "ghế đã có người giữ" kèm số ghế vướng, sắp theo toạ độ sơ đồ để khách đối
    /// chiếu được với màn hình chọn ghế. Danh sách rỗng (dữ liệu lệch) thì rơi về câu chung.
    /// </summary>
    private static string SeatTakenMessage(List<Seat> seats, List<Guid> heldSeatIds)
    {
        var numbers = seats
            .Where(s => heldSeatIds.Contains(s.Id))
            .OrderBy(s => s.Floor)
            .ThenBy(s => s.RowIndex)
            .ThenBy(s => s.ColumnIndex)
            .Select(s => s.SeatNumber)
            .ToList();

        return numbers.Count == 0
            ? SeatTakenRaceMessage
            : $"Ghế {string.Join(", ", numbers)} đang được giữ cho chuyến này.";
    }

    private static ServiceResult<SeatHoldSessionResponse> InvalidSeatIds(string message)
        => ServiceResult<SeatHoldSessionResponse>.Invalid(
            message,
            new Dictionary<string, string[]> { ["seatIds"] = [message] });
}
