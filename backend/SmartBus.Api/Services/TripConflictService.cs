using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Trips;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="ITripConflictService"/>. Chỉ đọc — không có <c>SaveChangesAsync</c> và không
/// có <c>try/catch DbUpdateException</c> như các service CRUD khác: không có gì để xung đột ghi.
/// </summary>
public class TripConflictService : ITripConflictService
{
    private const string ArrivalBeforeDepartureMessage = "Giờ đến phải sau giờ khởi hành";
    private const string DepartureRequiredMessage = "Giờ khởi hành không được để trống";

    private readonly AppDbContext _db;

    public TripConflictService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<TripConflictCheckResponse>> CheckAsync(
        TripConflictCheckRequest request,
        CancellationToken cancellationToken = default)
    {
        // DTO có [Required] cho lúc bind từ HTTP, nhưng service này còn được gọi từ service khác
        // (API gán chuyến, API đổi xe) — ở đó model binding không chạy, nên chặn null tại đây.
        if (request.DepartureTime is not { } departureTime)
        {
            return InvalidField<TripConflictCheckResponse>("departureTime", DepartureRequiredMessage);
        }

        var departureUtc = departureTime.UtcDateTime;
        var arrivalUtc = request.ArrivalTime?.UtcDateTime;

        if (arrivalUtc is { } arrival && arrival < departureUtc)
        {
            return InvalidField<TripConflictCheckResponse>("arrivalTime", ArrivalBeforeDepartureMessage);
        }

        // Không truyền tài nguyên nào thì không có gì để trùng — trả rỗng chứ không báo lỗi:
        // đây là phép tra cứu, không phải lệnh.
        if (request.BusId is null && request.DriverId is null)
        {
            return ServiceResult<TripConflictCheckResponse>.Ok(new TripConflictCheckResponse());
        }

        var windowEnd = arrivalUtc ?? departureUtc;

        // ── Khung giờ + trạng thái: y hệt phép kiểm tra "trùng khung giờ" của RouteTripsService
        //    (điều kiện DepartureTime == departureUtc bắt ca hai chuyến "mốc" cùng giờ khởi hành —
        //    khoảng của cả hai đều rỗng nên phép so khoảng không tự bắt được). ──
        var query = _db.Trips.AsNoTracking()
            .Where(t => (t.Status == TripStatus.Scheduled || t.Status == TripStatus.Running)
                && (t.DepartureTime == departureUtc
                    || (t.DepartureTime < windowEnd && (t.ArrivalTime ?? t.DepartureTime) > departureUtc)));

        // ── Lọc theo tài nguyên: vế xe và vế tài xế là HOẶC, không phải VÀ — chuyến trùng xe
        //    nhưng khác tài xế vẫn là xung đột điều xe. Nhánh null phải tách hẳn: so sánh
        //    `t.DriverId == null` trong truy vấn là so với "chưa phân công", không phải "bỏ qua
        //    vế này". ──
        if (request.BusId is { } busId && request.DriverId is { } driverId)
        {
            query = query.Where(t => t.BusId == busId || t.DriverId == driverId);
        }
        else if (request.BusId is { } busOnly)
        {
            query = query.Where(t => t.BusId == busOnly);
        }
        else if (request.DriverId is { } driverOnly)
        {
            query = query.Where(t => t.DriverId == driverOnly);
        }

        // Chuyến đang được gán không tự báo trùng chính nó.
        if (request.ExcludeTripId is { } excludeTripId)
        {
            query = query.Where(t => t.Id != excludeTripId);
        }

        // Ghép tường minh thay vì Include — cùng lối TripLookupService: máy chủ CSDL chỉ trả đúng
        // những cột được chọn, và join trong cũng đưa chuyến mồ côi ra khỏi kết quả (tuyến xoá mềm
        // chứ không xoá cứng nên dòng mồ côi gần như không tồn tại, nhưng luật đọc vẫn là luật join).
        var rows = await (from trip in query
                          join r in _db.Routes.AsNoTracking() on trip.RouteId equals r.Id
                          select new ConflictRow
                          {
                              Id = trip.Id,
                              RouteId = r.Id,
                              RouteCode = r.Code,
                              RouteName = r.Name,
                              BusId = trip.BusId,
                              DriverId = trip.DriverId,
                              DepartureTime = trip.DepartureTime,
                              ArrivalTime = trip.ArrivalTime,
                              Status = trip.Status,
                          })
            // Thời gian biểu đọc theo thứ tự chạy; trùng giờ khởi hành xếp tiếp theo Id để kết quả
            // ổn định giữa các lần gọi — cùng lối TripLookupService.
            .OrderBy(row => row.DepartureTime)
            .ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);

        return ServiceResult<TripConflictCheckResponse>.Ok(new TripConflictCheckResponse
        {
            // Chia hai danh sách ở bộ nhớ: câu truy vấn lọc theo (xe HOẶC tài xế) nên một dòng có
            // thể khớp cả hai vế, và phải xuất hiện ở cả hai danh sách chứ không bị vế nào nuốt.
            BusConflicts = request.BusId is { } busFilter
                ? rows.Where(row => row.BusId == busFilter).Select(ToResponse).ToList()
                : [],
            DriverConflicts = request.DriverId is { } driverFilter
                ? rows.Where(row => row.DriverId == driverFilter).Select(ToResponse).ToList()
                : [],
        });
    }

    private static ConflictingTripResponse ToResponse(ConflictRow row) => new()
    {
        Id = row.Id,
        RouteId = row.RouteId,
        RouteCode = row.RouteCode,
        RouteName = row.RouteName,
        BusId = row.BusId,
        DriverId = row.DriverId,
        DepartureTime = row.DepartureTime,
        ArrivalTime = row.ArrivalTime,
        // Tên chuỗi của enum — đổi ở bộ nhớ chứ không trong câu truy vấn, cùng lý do
        // TripLookupService: dịch ToString() của enum ra SQL là việc provider phải đoán.
        Status = row.Status.ToString(),
    };

    private static ServiceResult<T> InvalidField<T>(string field, string message)
        => ServiceResult<T>.Invalid(
            message,
            new Dictionary<string, string[]> { [field] = [message] });

    /// <summary>
    /// Dòng thô đọc từ CSDL trước khi đổi enum sang chuỗi ở bộ nhớ — cùng vai trò anonymous type
    /// trong TripLookupService, nhưng đặt tên để dùng lại được ở bước chia hai danh sách.
    /// </summary>
    private sealed class ConflictRow
    {
        public Guid Id { get; set; }

        public Guid RouteId { get; set; }

        public string RouteCode { get; set; } = string.Empty;

        public string RouteName { get; set; } = string.Empty;

        public Guid BusId { get; set; }

        public Guid? DriverId { get; set; }

        public DateTime DepartureTime { get; set; }

        public DateTime? ArrivalTime { get; set; }

        public TripStatus Status { get; set; }
    }
}
