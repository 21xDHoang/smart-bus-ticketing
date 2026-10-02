using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Trips;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="ITripSearchService"/> — hợp đồng đầy đủ ở mục "GET /trips/search" của
/// docs/api-contract.md.
///
/// Bám khuôn <see cref="TripLookupService"/>: cùng câu thông báo cho tuyến, cùng lối kiểm tra
/// khoảng ngày, cùng cách ghép bảng tường minh. Khác ở chỗ tuyến đã đọc được ngay từ đầu
/// (<c>routeId</c> bắt buộc) nên mã/tên tuyến lấy từ chính dòng vừa đọc, không phải join lại.
/// </summary>
public class TripSearchService : ITripSearchService
{
    private const string RouteNotFoundMessage = "Không tìm thấy tuyến đường";
    private const string DateRangeMessage = "Thời điểm kết thúc phải sau thời điểm bắt đầu";

    private readonly AppDbContext _db;

    public TripSearchService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<IReadOnlyList<TripSearchResult>>> SearchAsync(
        SearchTripsRequest request,
        CancellationToken cancellationToken = default)
    {
        // [Required] ở SearchTripsRequest đã chặn thiếu routeId trước khi vào đây — cùng lối các
        // service khác: Controller lo hình dạng request, service lo nghiệp vụ.
        var routeId = request.RouteId!.Value;

        // routeId là tham chiếu cứng: GUID trỏ tới tuyến không tồn tại là lỗi gọi, không phải
        // "không có chuyến nào" — cùng câu trả lời của GET /trips khi hỏi về tuyến X.
        var route = await _db.Routes
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == routeId, cancellationToken);

        if (route is null)
        {
            return ServiceResult<IReadOnlyList<TripSearchResult>>.NotFound(RouteNotFoundMessage);
        }

        // Cùng lối TripLookupService: chỉ báo lỗi khi CẢ HAI mốc cùng có mặt — thiếu một mốc là
        // khoảng mở, không phải khoảng sai.
        if (request.From is { } from && request.To is { } to && to < from)
        {
            return InvalidField<IReadOnlyList<TripSearchResult>>("to", DateRangeMessage);
        }

        var fromUtc = request.From?.UtcDateTime;
        var toUtc = request.To?.UtcDateTime;

        // Chỉ chuyến còn lên lịch mới là lựa chọn để đặt vé — chuyến đã chạy/đang chạy/đã hủy tự
        // rời khỏi kết quả. Hợp đồng vì thế không có tham số status ở endpoint này.
        var query = _db.Trips
            .AsNoTracking()
            .Where(t => t.RouteId == routeId && t.Status == TripStatus.Scheduled);

        if (fromUtc is not null)
        {
            query = query.Where(t => t.DepartureTime >= fromUtc);
        }

        if (toUtc is not null)
        {
            query = query.Where(t => t.DepartureTime <= toUtc);
        }

        // Giá vé phổ thông của tuyến — con số niêm yết duy nhất của màn hình kết quả. Mỗi cặp
        // (tuyến, đối tượng) có đúng một dòng (unique ở AppDbContext.Route.cs); tuyến chưa cấu
        // hình giá là trạng thái dữ liệu bình thường → null, không phải lỗi.
        var standardPrice = await _db.Fares
            .AsNoTracking()
            .Where(f => f.RouteId == routeId && f.PassengerType == PassengerType.Standard)
            .Select(f => (decimal?)f.Price)
            .FirstOrDefaultAsync(cancellationToken);

        // Ghép tường minh bằng join thay vì Include: máy chủ CSDL chỉ trả đúng những cột được
        // chọn, và join trong cũng là thứ đưa chuyến mồ côi (xe không còn) ra khỏi kết quả một
        // cách tự nhiên — cùng lối TripLookupService. Tuyến đã đọc ở trên nên chỉ còn ghép xe.
        var joined = from trip in query
                     join b in _db.Buses.AsNoTracking() on trip.BusId equals b.Id
                     select new
                     {
                         trip.Id,
                         trip.DepartureTime,
                         trip.ArrivalTime,
                         b.BusType,
                         b.Capacity,
                     };

        var rows = await joined
            // Thời gian biểu đọc theo thứ tự chạy; trùng giờ khởi hành (chuyến sinh hàng loạt
            // cùng giây) xếp tiếp theo Id để hai lần gọi ra cùng một kết quả.
            .OrderBy(t => t.DepartureTime)
            .ThenBy(t => t.Id)
            .ToListAsync(cancellationToken);

        return ServiceResult<IReadOnlyList<TripSearchResult>>.Ok(rows.Select(row => new TripSearchResult
        {
            Id = row.Id,
            RouteId = routeId,
            RouteCode = route.Code,
            RouteName = route.Name,
            DepartureTime = row.DepartureTime,
            ArrivalTime = row.ArrivalTime,
            Price = standardPrice,
            // Chưa có bảng vé/giữ chỗ (Sprint 3 mới migrate) nên chưa có gì để trừ — hôm nay luôn
            // bằng sức chứa. Hợp đồng đã chốt hình dạng trường; khi bảng vé vào, chỉ dòng này đổi.
            SeatsRemaining = row.Capacity,
            Capacity = row.Capacity,
            BusType = row.BusType,
        }).ToList());
    }

    private static ServiceResult<T> InvalidField<T>(string field, string message)
        => ServiceResult<T>.Invalid(
            message,
            new Dictionary<string, string[]> { [field] = [message] });
}
