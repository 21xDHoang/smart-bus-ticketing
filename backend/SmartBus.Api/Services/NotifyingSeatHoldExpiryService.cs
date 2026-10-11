using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Lớp bọc phát sự kiện realtime cho <see cref="ISeatHoldExpiryService"/> — task *"Đồng bộ trạng thái
/// ghế realtime giữa các phiên (SignalR)"*, dòng 11 Sprint 3 (US 2).
///
/// Khác hai lớp bọc anh em ở chỗ đây là vế KHÓ: <see cref="ISeatHoldExpiryService.ExpireDueHoldsAsync"/>
/// chỉ trả về SỐ LƯỢT vừa lật, không cho biết lật ở những chuyến nào — mà sự kiện realtime lại phải
/// đi theo chuyến. Lớp bọc vì vậy tự hỏi CSDL một câu NHẸ trước khi gọi service thật: "những chuyến
/// nào đang có lượt giữ sắp được lật?", giữ danh sách đó, gọi service thật, rồi phát sự kiện cho
/// từng chuyến nếu lượt quét lật thật.
///
/// Vì sao hỏi TRƯỚC chứ không hỏi lại sau lượt quét: sau khi lật thì các dòng đã là Expired, không còn
/// cách nào biết chuyến nào vừa đổi nếu không đối chiếu trước/sau. Danh sách hỏi trước chỉ là ƯỚC
/// LƯỢNG (có thể thừa nếu lượt quét hỏng giữa đường), và thừa thì vô hại: client gọi lại sơ đồ ghế,
/// diff thấy không đổi. Thiếu mới là có hại — nên quy tắc ở đây là "thà thừa".
///
/// <see cref="SeatHoldExpiryBackgroundService"/> tiêm qua interface nên đi xuyên lớp bọc này; đăng ký
/// ở Program.cs phải là AddScoped cho interface với factory, xem khối "Đồng bộ ghế realtime".
/// </summary>
public class NotifyingSeatHoldExpiryService : ISeatHoldExpiryService
{
    private readonly SeatHoldExpiryService _inner;

    private readonly AppDbContext _db;

    private readonly ISeatMapNotifier _notifier;

    public NotifyingSeatHoldExpiryService(
        SeatHoldExpiryService inner,
        AppDbContext db,
        ISeatMapNotifier notifier)
    {
        _inner = inner;
        _db = db;
        _notifier = notifier;
    }

    public async Task<int> ExpireDueHoldsAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        // Đúng điều kiện của SeatHoldExpiryService (Holding + ExpiresAt < now, so sánh NGHIÊM NGẶT —
        // đúng mốc ExpiresAt vẫn còn hiệu lực) nhưng chỉ hỏi TripId và DISTINCT: một chuyến có nhiều
        // ghế hết hạn cùng lượt chỉ cần MỘT sự kiện, không phải một sự kiện mỗi ghế.
        var affectedTripIds = await _db.SeatHolds.AsNoTracking()
            .Where(h => h.Status == SeatHoldStatus.Holding && h.ExpiresAt < now)
            .Select(h => h.TripId)
            .Distinct()
            .ToListAsync(cancellationToken);

        // Một mốc `now` duy nhất dùng cho CẢ câu hỏi trước lẫn lượt gọi service thật: hai mốc lệch
        // nhau vài mili giây có thể khiến câu hỏi trước bỏ sót đúng lượt giữ mà lượt quét vừa chạm tới.
        var expiredCount = await _inner.ExpireDueHoldsAsync(now, cancellationToken);

        // Lượt quét không lật gì thì không phát: danh sách hỏi trước chỉ là ước lượng, con số thật của
        // service mới là điều kiện phát. Service thật ném lỗi (CSDL chập chờn) thì lỗi nổi lên nguyên
        // vẹn và cũng không phát gì — mọi thay đổi nếu có đều đã rollback.
        if (expiredCount > 0)
        {
            foreach (var tripId in affectedTripIds)
            {
                await _notifier.SeatMapChangedAsync(tripId);
            }
        }

        return expiredCount;
    }
}
