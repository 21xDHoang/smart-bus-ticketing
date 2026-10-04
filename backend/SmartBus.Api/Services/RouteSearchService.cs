using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Routes;
using SmartBus.Api.Entities;
using Route = SmartBus.Api.Entities.Route;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IRouteSearchService"/> — hợp đồng đầy đủ ở mục "Tra cứu tuyến — /routes/search"
/// của docs/api-contract.md.
///
/// Truy vấn trung tâm là self-join trên bảng nối RouteStops, đúng hình dạng mà chủ CSDL đã chuẩn
/// bị chỉ mục sẵn (<c>IX_RouteStops_StopId_RouteId_StopOrder</c>, ghi chú ở Data/AppDbContext.Route.cs):
///
///   FROM "RouteStops" a
///   JOIN "RouteStops" b ON b."RouteId" = a."RouteId" AND b."StopId" = @to
///   JOIN "Routes"      r ON r."Id"      = a."RouteId"
///   WHERE a."StopId" = @from AND a."StopOrder" < b."StopOrder"
///
/// Khác một chi tiết: đầu vào là TÊN điểm đi/điểm đến (form tra cứu gửi chuỗi tự do), nên trước
/// self-join có một bước giải từ khoá ra tập <c>StopId</c> khớp tên trạm.
/// </summary>
public class RouteSearchService : IRouteSearchService
{
    private const string SamePointMessage = "Điểm đi và điểm đến không được trùng nhau";

    private const string DateFormatMessage = "Ngày đi phải theo định dạng yyyy-MM-dd";

    /// <summary>Múi giờ Việt Nam — cùng hằng số và cùng lý do với <see cref="TripGenerationService"/>.</summary>
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    private readonly AppDbContext _db;

    public RouteSearchService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<IReadOnlyList<RouteSearchResult>>> SearchAsync(
        RouteSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        // [Required] ở RouteSearchRequest chặn null trước khi vào đây; tới đây chỉ còn lọc khoảng
        // trắng — "   " qua được [Required] nhưng không phải một điểm đi hợp lệ.
        var origin = request.Origin?.Trim() ?? string.Empty;
        var destination = request.Destination?.Trim() ?? string.Empty;

        if (origin.Length == 0)
        {
            return InvalidField("origin", "Điểm đi không được để trống");
        }

        if (destination.Length == 0)
        {
            return InvalidField("destination", "Điểm đến không được để trống");
        }

        // Hai đầu mút trùng nhau thì không có hành trình hợp lệ nào — chặn sớm, cùng lối form
        // tra cứu chặn phía client. Gắn lỗi vào trường thứ hai, cùng lối "to sớm hơn from" của
        // GET /trips/search gắn vào errors.to.
        if (origin.Equals(destination, StringComparison.OrdinalIgnoreCase))
        {
            return InvalidField("destination", SamePointMessage);
        }

        DateOnly? travelDate = null;
        if (request.Date is { } dateText)
        {
            if (!DateOnly.TryParseExact(
                    dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return InvalidField("date", DateFormatMessage);
            }

            travelDate = parsed;
        }

        // Bước 1 — giải từ khoá ra tập trạm. So khớp không phân biệt hoa thường bằng ToLower()
        // thay vì EF.Functions.ILike: ILike là hàm riêng của Npgsql, dùng nó thì test chạy trên
        // provider InMemory sẽ đổ (cùng lý do RouteService đã ghi khi làm bộ lọc GET /routes).
        // Khớp theo TÊN trạm, không theo origin/destination của tuyến: hành khách tìm từ trạm
        // giữa tuyến ("Công viên 23/9" → "Chợ Lớn") vẫn ra đúng tuyến.
        var originKeyword = origin.ToLowerInvariant();
        var originStopIds = await _db.Stops
            .AsNoTracking()
            .Where(s => s.Name.ToLower().Contains(originKeyword))
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        // Từ khoá không trỏ tới trạm nào là bộ lọc mềm — trả mảng rỗng, không phải 404: cùng lối
        // "status không khớp mã nào → danh sách rỗng" của GET /routes.
        if (originStopIds.Count == 0)
        {
            return EmptyResult();
        }

        var destinationKeyword = destination.ToLowerInvariant();
        var destinationStopIds = await _db.Stops
            .AsNoTracking()
            .Where(s => s.Name.ToLower().Contains(destinationKeyword))
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        if (destinationStopIds.Count == 0)
        {
            return EmptyResult();
        }

        // Bước 2 — self-join trên bảng nối: tuyến chứa CẢ trạm đi lẫn trạm đến, trạm đi đứng
        // TRƯỚC trạm đến theo stopOrder. Join bảng Routes lấy đúng tuyến đang khai thác — cũng
        // là thứ đưa tuyến mồ côi ra khỏi kết quả một cách tự nhiên (cùng lối TripSearchService).
        // Self-join sinh một dòng cho mỗi cặp (trạm đi, trạm đến) khớp, nên Distinct ở cuối.
        var routeQuery = from a in _db.RouteStops.AsNoTracking()
                         join b in _db.RouteStops.AsNoTracking() on a.RouteId equals b.RouteId
                         join r in _db.Routes.AsNoTracking() on a.RouteId equals r.Id
                         where originStopIds.Contains(a.StopId)
                               && destinationStopIds.Contains(b.StopId)
                               && a.StopOrder < b.StopOrder
                               && r.Status == RouteStatus.Active
                         select r;

        // Bước 3 — có ngày đi thì chỉ giữ tuyến có ít nhất một chuyến Scheduled khởi hành trong
        // trọn ngày đó GIỜ VIỆT NAM (cùng quy ước "lọc theo ngày" của GET /trips/search: nửa đêm
        // giờ Việt Nam là 17:00 UTC hôm trước). Tách thành một truy vấn riêng thay vì correlated
        // subquery: chạy được trên cả Npgsql lẫn InMemory của bộ test, và phép đọc theo cột thời
        // gian chỉ quét một ngày.
        if (travelDate is { } day)
        {
            var fromUtc = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), VietnamOffset).UtcDateTime;
            var toUtc = new DateTimeOffset(day.AddDays(1).ToDateTime(TimeOnly.MinValue), VietnamOffset).UtcDateTime;

            var routeIdsWithTrips = await _db.Trips
                .AsNoTracking()
                .Where(t => t.Status == TripStatus.Scheduled
                            && t.DepartureTime >= fromUtc
                            && t.DepartureTime < toUtc)
                .Select(t => t.RouteId)
                .Distinct()
                .ToListAsync(cancellationToken);

            routeQuery = routeQuery.Where(r => routeIdsWithTrips.Contains(r.Id));
        }

        var routes = await routeQuery
            .Distinct()
            .OrderBy(r => r.Code)
            .ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);

        if (routes.Count == 0)
        {
            return EmptyResult();
        }

        // Bước 4 — trạm và giá của các tuyến khớp, gộp một lượt thay vì gọi N lần (N tuyến thường
        // chỉ vài dòng, nhưng bảng nối và bảng giá không đáng bị hỏi đi hỏi lại).
        var routeIds = routes.Select(r => r.Id).ToList();

        var routeStops = await _db.RouteStops
            .AsNoTracking()
            .Include(rs => rs.Stop)
            .Where(rs => routeIds.Contains(rs.RouteId))
            .OrderBy(rs => rs.RouteId)
            .ThenBy(rs => rs.StopOrder)
            .ThenBy(rs => rs.Id)
            .ToListAsync(cancellationToken);

        // "Giá từ" = giá thấp nhất trong bảng giá của tuyến, mọi đối tượng ưu đãi — màn hình
        // tra cứu hiển thị một con số niêm yết, chọn đối tượng là bước của màn đặt vé (Sprint 3).
        var minPrices = await _db.Fares
            .AsNoTracking()
            .Where(f => routeIds.Contains(f.RouteId))
            .GroupBy(f => f.RouteId)
            .Select(g => new { RouteId = g.Key, MinPrice = (decimal?)g.Min(f => f.Price) })
            .ToListAsync(cancellationToken);

        var stopsByRoute = routeStops
            .GroupBy(rs => rs.RouteId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var minPriceByRoute = minPrices.ToDictionary(m => m.RouteId, m => m.MinPrice);

        var results = routes.Select(route => new RouteSearchResult
        {
            RouteId = route.Id,
            RouteCode = route.Code,
            RouteName = route.Name,
            Origin = route.Origin,
            Destination = route.Destination,
            DistanceKm = route.DistanceKm,
            Stops = stopsByRoute.TryGetValue(route.Id, out var stops)
                ? stops.Select(ToStop).ToList()
                : [],
            // Tuyến chưa cấu hình giá thì không có dòng trong từ điển — null, không phải lỗi.
            MinPrice = minPriceByRoute.GetValueOrDefault(route.Id),
        }).ToList();

        return ServiceResult<IReadOnlyList<RouteSearchResult>>.Ok(results);
    }

    private static ServiceResult<IReadOnlyList<RouteSearchResult>> EmptyResult()
        => ServiceResult<IReadOnlyList<RouteSearchResult>>.Ok([]);

    private static ServiceResult<IReadOnlyList<RouteSearchResult>> InvalidField(string field, string message)
        => ServiceResult<IReadOnlyList<RouteSearchResult>>.Invalid(
            message,
            new Dictionary<string, string[]> { [field] = [message] });

    private static RouteSearchStop ToStop(RouteStop routeStop) => new()
    {
        StopId = routeStop.StopId,

        // Trạm bị xoá cứng trong khi dòng nối còn lại là chuyện không xảy ra được (FK Restrict
        // chặn — quy ước A5), nhưng vẫn để nhánh dự phòng thay vì dùng ! — cùng lối
        // RouteStopService.ToResponse.
        StopName = routeStop.Stop?.Name ?? string.Empty,
        StopOrder = routeStop.StopOrder,
    };
}
