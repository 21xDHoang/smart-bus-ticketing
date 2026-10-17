using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="ITicketBoardingStopService"/> — ba luật đọc ở doc-comment interface.
///
/// Chỉ đọc <c>Trips</c> và <c>RouteStops</c>; KHÔNG đọc <c>Tickets.BoardingStopId</c> (trạm khách
/// chọn lúc mua) và KHÔNG ghi gì. Việc ghi thuộc
/// <see cref="ITicketValidationRecorder.RecordAsync"/> — endpoint chuyển giá trị sang đó.
///
/// Truy vấn theo khoá ngoại từng bảng, không Include navigation: provider InMemory của tầng test bỏ
/// rơi bản ghi gốc khi Include trỏ tới navigation không có dòng (cùng lý do đã ghi ở
/// <see cref="TicketValidationService"/>). Trên CSDL thật hai khoá ngoại này không thể mồ côi
/// (<c>Restrict</c>), nên mỗi truy vấn trúng đúng một dòng.
/// </summary>
public class TicketBoardingStopService : ITicketBoardingStopService
{
    /// <summary>
    /// Câu lỗi theo trường <c>boardingStopId</c>. Dùng cho cả <c>message</c> lẫn phần tử trong
    /// <c>errors</c> — cùng lối recorder: màn hình và thông báo không nói hai kiểu khác nhau.
    /// </summary>
    private const string SaiTuyenMessage = "Trạm lên xe không thuộc tuyến của chuyến.";

    private readonly AppDbContext _db;

    public TicketBoardingStopService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<Guid?>> ResolveAsync(
        Guid? requestedStopId,
        Guid? ticketTripId,
        TicketValidationResult result,
        CancellationToken cancellationToken = default)
    {
        // Luật 1 + 3: lượt bị từ chối không phải lượt lên xe; không khai trạm thì không ghi gì.
        if (result != TicketValidationResult.Valid || requestedStopId is not { } stopId)
        {
            return ServiceResult<Guid?>.Ok(null);
        }

        // Vé Valid nhưng không tra ra chuyến (chỉ có thể là dòng mồ côi của tầng test — CSDL thật
        // buộc Tickets.TripId NOT NULL và Restrict): không xác minh được tuyến thì không nhận trạm.
        if (ticketTripId is not { } tripId)
        {
            return SaiTuyen();
        }

        var routeId = await _db.Trips
            .AsNoTracking()
            .Where(t => t.Id == tripId)
            .Select(t => (Guid?)t.RouteId)
            .FirstOrDefaultAsync(cancellationToken);

        if (routeId is not { } route)
        {
            return SaiTuyen();
        }

        // Luật 2: thành viên RouteStops của tuyến chuyến trên vé — KHÔNG so Tickets.BoardingStopId.
        var thuocTuyen = await _db.RouteStops
            .AsNoTracking()
            .AnyAsync(rs => rs.RouteId == route && rs.StopId == stopId, cancellationToken);

        return thuocTuyen
            ? ServiceResult<Guid?>.Ok(stopId)
            : SaiTuyen();
    }

    private static ServiceResult<Guid?> SaiTuyen()
        => ServiceResult<Guid?>.Invalid(
            SaiTuyenMessage,
            new Dictionary<string, string[]> { ["boardingStopId"] = [SaiTuyenMessage] });
}
