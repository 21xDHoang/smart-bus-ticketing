using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="ISeatHoldExpiryService"/> — phần ruột của job quét hold hết hạn
/// (US 3, task của Vàng Thị Dăm).
///
/// Đọc kèm <see cref="SeatHoldStatus"/> trước khi sửa: <c>Holding</c> là trạng thái DUY NHẤT chặn
/// ghế (điều kiện của partial unique index trong AppDbContext.Seat.cs), nên nhả ghế chính là lật
/// trạng thái sang <c>Expired</c> — không xoá dòng. Dòng ở lại làm lịch sử, và
/// <see cref="SeatHoldLog"/> ghi lại sự kiện đó.
/// </summary>
public class SeatHoldExpiryService : ISeatHoldExpiryService
{
    private readonly AppDbContext _db;

    public SeatHoldExpiryService(AppDbContext db) => _db = db;

    public async Task<int> ExpireDueHoldsAsync(
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        // "Đến hạn nhả" = còn Holding mà ExpiresAt đã qua.
        //
        // So sánh là ExpiresAt < now (NGHIÊM NGẶT), không phải <=: đúng mốc ExpiresAt lượt giữ vẫn
        // còn hiệu lực. Khách được hứa "giữ chỗ 10 phút" (US 3), nhả sớm một tích tắc là rút lại
        // chỗ ngồi trong lúc khách còn đang trả tiền — cùng lối MonthlyPassExpiryService xét ValidTo.
        //
        // Chốt Status là thứ khiến lượt quét chạy LẶP LẠI được: lượt trước đã lật rồi thì lượt sau
        // không khớp điều kiện nữa, nên không đè UpdatedAt của những lượt đã hết hạn từ lâu.
        var quaHan = await _db.SeatHolds
            .Where(h => h.Status == SeatHoldStatus.Holding && h.ExpiresAt < now)
            .ToListAsync(cancellationToken);

        if (quaHan.Count == 0)
        {
            return 0;
        }

        foreach (var hold in quaHan)
        {
            hold.Status = SeatHoldStatus.Expired;

            // A4: lượt giữ có sửa dữ liệu nên có UpdatedAt. Ghi lại chính mốc đã dùng để xét hạn chứ
            // không gọi DateTime.UtcNow lần nữa — hai mốc lệch nhau vài mili giây trong cùng một lượt
            // quét là thứ không giải thích được lúc tra vết. Cùng lối MonthlyPassExpiryService.
            hold.UpdatedAt = now;

            _db.SeatHoldLogs.Add(new SeatHoldLog
            {
                SeatHoldId = hold.Id,
                UserId = hold.UserId,
                SessionCode = hold.SessionCode,
                Action = SeatHoldLogAction.Expired,
                CreatedAt = now,
            });
        }

        // Nạp entity rồi SaveChanges thay vì ExecuteUpdateAsync — quyết định có chủ ý, ba lý do:
        //   - ExecuteUpdate không kiểm chứng được bằng provider InMemory của bộ test (cả bộ test
        //     đang chạy trên đó), nên nhánh quan trọng nhất của job sẽ không có test nào phủ.
        //   - Phải ghi kèm một dòng SeatHoldLogs cho mỗi lượt vừa lật, mà nội dung dòng log lấy từ
        //     chính entity — đằng nào cũng phải nạp.
        //   - Cần đúng số dòng đã lật để job ghi log.
        //
        // Chi phí nạp entity không đáng kể: mỗi lượt chỉ có những lượt giữ vừa hết hạn kể từ lượt
        // trước, và chỉ mục IX_SeatHolds_ExpiresAt lo phần lọc.
        //
        // Hai bản app cùng chạy job thì cùng lật một lượt giữ và cùng ghi log — bản thứ hai đâm vào
        // unique index (SeatHoldId, Action) của SeatHoldLogs và rollback cả lượt; xem giải thích đầy
        // đủ ở AppDbContext.Seat.cs.
        await _db.SaveChangesAsync(cancellationToken);

        return quaHan.Count;
    }
}
