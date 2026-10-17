using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Routes;
using SmartBus.Api.Entities;
using Route = SmartBus.Api.Entities.Route;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IRouteSuggestionService"/> — hợp đồng đầy đủ ở mục
/// "Gợi ý tuyến cho ngày đã chọn — /routes/suggestions" của docs/api-contract.md.
///
/// <para>
/// Khác <see cref="RouteSearchService"/> ở chỗ <b>ngày không lọc tuyến</b>. Ngày chỉ quyết định
/// lịch chạy: tuyến nối được hai trạm luôn có mặt trong <c>matches</c>, kèm
/// <c>hasTripsOnDate</c>, <c>nearestAvailableDate</c> và <c>emptyReason</c>. Nhờ vậy màn tra cứu
/// không còn rơi vào ô "Không tìm thấy tuyến phù hợp" chỉ vì ngày đó chưa có chuyến.
/// </para>
///
/// <para>
/// <b>Vì sao đọc toàn bộ trạm/RouteStops rồi lọc trong bộ nhớ.</b> Cùng lý do đã ghi ở
/// <see cref="StopSearchService"/> và <see cref="StopSuggestionService"/>: khớp tên không dấu cần
/// <c>unaccent</c>, mà extension và cột chuẩn hoá là việc migration của chủ CSDL (A7), còn hàm
/// riêng của Npgsql sẽ làm đổ bộ test InMemory. Số trạm của một thành phố chỉ vài trăm dòng;
/// hợp đồng còn cần dựng lịch chạy theo từng ngày — việc mà một truy vấn SQL duy nhất không làm
/// gọn hơn được.
/// </para>
/// </summary>
public class RouteSuggestionService : IRouteSuggestionService
{
    private const string SamePointMessage = "Điểm đi và điểm đến không được trùng nhau";

    private const string DateFormatMessage = "Ngày đi phải theo định dạng yyyy-MM-dd";

    /// <summary>Múi giờ Việt Nam — cùng hằng số và cùng lý do với <see cref="RouteSearchService"/>.</summary>
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    private static readonly StringComparer NameComparer =
        StringComparer.Create(CultureInfo.GetCultureInfo("vi"), ignoreCase: true);

    private const string DateFormat = "yyyy-MM-dd";

    private readonly AppDbContext _db;

    public RouteSuggestionService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<RouteSuggestionsResponse>> SearchAsync(
        RouteSuggestionsRequest request,
        CancellationToken cancellationToken = default)
    {
        var windowDays = request.WindowDays ?? RouteSuggestionsRequest.DefaultWindowDays;
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Add(VietnamOffset));

        var selectedDate = today;
        if (!string.IsNullOrWhiteSpace(request.Date))
        {
            if (!DateOnly.TryParseExact(
                    request.Date.Trim(),
                    DateFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsed))
            {
                return InvalidField("date", DateFormatMessage);
            }

            selectedDate = parsed;
        }

        var stops = await _db.Stops
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var originResolution = ResolveStop(
            request.OriginStopId,
            request.Origin,
            field: "origin",
            label: "Điểm đi",
            stops);

        if (!originResolution.Success)
        {
            return MapFailure(originResolution);
        }

        var destinationResolution = ResolveStop(
            request.DestinationStopId,
            request.Destination,
            field: "destination",
            label: "Điểm đến",
            stops);

        if (!destinationResolution.Success)
        {
            return MapFailure(destinationResolution);
        }

        var origin = originResolution.Data!;
        var destination = destinationResolution.Data!;

        if (origin.Id == destination.Id)
        {
            return InvalidField("destination", SamePointMessage);
        }

        var activeRoutes = await _db.Routes
            .AsNoTracking()
            .Where(r => r.Status == RouteStatus.Active)
            .ToListAsync(cancellationToken);

        var activeRouteIds = activeRoutes.Select(r => r.Id).ToList();

        // `Contains` trên danh sách rỗng dịch thành `WHERE false` — vẫn chạy một truy vấn rỗng,
        // nhưng tránh phải tạo nhánh kiểu khác nhau cho biến.
        var routeStops = await _db.RouteStops
            .AsNoTracking()
            .Where(rs => activeRouteIds.Contains(rs.RouteId))
            .ToListAsync(cancellationToken);

        var routeStopsByRoute = routeStops
            .GroupBy(rs => rs.RouteId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var activeStopIds = routeStops
            .Select(rs => rs.StopId)
            .ToHashSet();

        // Tách ba nhóm trong một lượt: tuyến nối đúng hai trạm, tuyến chỉ đi qua điểm đi, tuyến
        // chỉ đi qua điểm đến. Nhóm sau chỉ dùng khi nhóm đầu rỗng.
        var matches = new List<Route>();
        var fromOriginOnly = new List<Route>();
        var toDestinationOnly = new List<Route>();

        foreach (var route in activeRoutes)
        {
            if (!routeStopsByRoute.TryGetValue(route.Id, out var rows))
            {
                continue;
            }

            var originRow = rows.FirstOrDefault(rs => rs.StopId == origin.Id);
            var destinationRow = rows.FirstOrDefault(rs => rs.StopId == destination.Id);

            if (originRow is not null
                && destinationRow is not null
                && originRow.StopOrder < destinationRow.StopOrder)
            {
                matches.Add(route);
                continue;
            }

            if (originRow is not null)
            {
                fromOriginOnly.Add(route);
            }

            if (destinationRow is not null)
            {
                toDestinationOnly.Add(route);
            }
        }

        // Alternative chỉ có nghĩa khi không có tuyến nối đúng hai trạm — cùng hợp đồng.
        List<Route> fromOriginRoutes = matches.Count == 0 ? fromOriginOnly : [];
        var fromOriginIds = fromOriginRoutes.Select(r => r.Id).ToHashSet();

        // Tuyến vừa đi qua điểm đi vừa đi qua điểm đến nhưng ngược chiều sẽ nằm ở cả hai nhóm;
        // loại khỏi toDestination để màn hình không hiện hai lần cùng một tuyến.
        List<Route> toDestinationRoutes = matches.Count == 0
            ? toDestinationOnly.Where(r => !fromOriginIds.Contains(r.Id)).ToList()
            : [];

        var responseRouteIds = matches
            .Concat(fromOriginRoutes)
            .Concat(toDestinationRoutes)
            .Select(r => r.Id)
            .Distinct()
            .ToList();

        // Trạm và giá của mọi tuyến sẽ trả về — gộp một lượt thay vì hỏi từng tuyến.
        var detailRouteStops = await _db.RouteStops
            .AsNoTracking()
            .Include(rs => rs.Stop)
            .Where(rs => responseRouteIds.Contains(rs.RouteId))
            .OrderBy(rs => rs.RouteId)
            .ThenBy(rs => rs.StopOrder)
            .ThenBy(rs => rs.Id)
            .ToListAsync(cancellationToken);

        var detailStopsByRoute = detailRouteStops
            .GroupBy(rs => rs.RouteId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var minPrices = await _db.Fares
            .AsNoTracking()
            .Where(f => responseRouteIds.Contains(f.RouteId))
            .GroupBy(f => f.RouteId)
            .Select(g => new { RouteId = g.Key, MinPrice = (decimal?)g.Min(f => f.Price) })
            .ToListAsync(cancellationToken);

        var minPriceByRoute = minPrices.ToDictionary(m => m.RouteId, m => m.MinPrice);

        // ── Lịch chạy ─────────────────────────────────────────────────────────
        // Cửa sổ tính từ HÔM NAY, không phải từ selectedDate: đây là lịch bán vé thực tế mà
        // background job đã sinh chuyến, không phải lịch lý thuyết quanh ngày hành khách chọn.
        var bookingHorizonEnd = today.AddDays(windowDays - 1);

        var windowStartUtc = DayStartUtc(today);
        var windowEndUtc = DayStartUtc(today.AddDays(windowDays));

        var windowTrips = await _db.Trips
            .AsNoTracking()
            .Where(t => responseRouteIds.Contains(t.RouteId)
                        && t.Status == TripStatus.Scheduled
                        && t.DepartureTime >= windowStartUtc
                        && t.DepartureTime < windowEndUtc)
            .ToListAsync(cancellationToken);

        // Ngày ngoài cửa sổ vẫn cần biết có chuyến hay không để hasTripsOnDate nói đúng. Chỉ
        // hỏi thêm đúng một ngày, không nới cửa sổ.
        var selectedDateInWindow = selectedDate >= today && selectedDate <= bookingHorizonEnd;
        List<Trip> selectedDateTrips = !selectedDateInWindow && responseRouteIds.Count > 0
            ? await _db.Trips
                .AsNoTracking()
                .Where(t => responseRouteIds.Contains(t.RouteId)
                            && t.Status == TripStatus.Scheduled
                            && t.DepartureTime >= DayStartUtc(selectedDate)
                            && t.DepartureTime < DayStartUtc(selectedDate.AddDays(1)))
                .ToListAsync(cancellationToken)
            : [];

        var tripsByRouteAndDay = windowTrips
            .Concat(selectedDateTrips)
            .GroupBy(t => (t.RouteId, Day: LocalDay(t.DepartureTime)))
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(t => t.DepartureTime).ToList());

        var matchSuggestions = matches
            .Select(route => BuildSuggestion(
                route,
                detailStopsByRoute.GetValueOrDefault(route.Id) ?? [],
                minPriceByRoute.GetValueOrDefault(route.Id),
                today,
                windowDays,
                selectedDate,
                tripsByRouteAndDay))
            .OrderBy(s => s.RouteCode)
            .ThenBy(s => s.RouteId)
            .ToList();

        var fromOriginSuggestions = fromOriginRoutes
            .Select(route => BuildSuggestion(
                route,
                detailStopsByRoute.GetValueOrDefault(route.Id) ?? [],
                minPriceByRoute.GetValueOrDefault(route.Id),
                today,
                windowDays,
                selectedDate,
                tripsByRouteAndDay))
            .OrderBy(s => s.RouteCode)
            .ThenBy(s => s.RouteId)
            .ToList();

        var toDestinationSuggestions = toDestinationRoutes
            .Select(route => BuildSuggestion(
                route,
                detailStopsByRoute.GetValueOrDefault(route.Id) ?? [],
                minPriceByRoute.GetValueOrDefault(route.Id),
                today,
                windowDays,
                selectedDate,
                tripsByRouteAndDay))
            .OrderBy(s => s.RouteCode)
            .ThenBy(s => s.RouteId)
            .ToList();

        return ServiceResult<RouteSuggestionsResponse>.Ok(new RouteSuggestionsResponse
        {
            SelectedDate = ToIsoDate(selectedDate),
            BookingHorizonEnd = ToIsoDate(bookingHorizonEnd),
            Origin = ToInfo(origin, activeStopIds.Contains(origin.Id)),
            Destination = ToInfo(destination, activeStopIds.Contains(destination.Id)),
            Matches = matchSuggestions,
            Alternatives = new RouteSuggestionsAlternatives
            {
                FromOrigin = fromOriginSuggestions,
                ToDestination = toDestinationSuggestions,
            },
            EmptyReason = DetermineEmptyReason(
                activeRoutes.Count,
                activeStopIds,
                origin.Id,
                destination.Id,
                matchSuggestions,
                selectedDate,
                bookingHorizonEnd),
        });
    }

    /// <summary>
    /// Dựng một dòng tuyến gợi ý, kèm lịch chạy trong cửa sổ và ngày gần nhất.
    /// </summary>
    private static RouteSuggestion BuildSuggestion(
        Route route,
        IReadOnlyList<RouteStop> stops,
        decimal? minPrice,
        DateOnly today,
        int windowDays,
        DateOnly selectedDate,
        IReadOnlyDictionary<(Guid RouteId, DateOnly Day), List<Trip>> tripsByRouteAndDay)
    {
        var availableDates = new List<DateOnly>(windowDays);

        for (var offset = 0; offset < windowDays; offset++)
        {
            var day = today.AddDays(offset);

            if (tripsByRouteAndDay.ContainsKey((route.Id, day)))
            {
                availableDates.Add(day);
            }
        }

        // Hoà khoảng cách thì chọn ngày TƯƠNG LAI: hành khách đang tìm chuyến để đi, gợi ý ngày
        // đã qua không dùng được.
        var nearestDate = availableDates
            .OrderBy(day => Math.Abs(day.DayNumber - selectedDate.DayNumber))
            .ThenBy(day => day < selectedDate ? 1 : 0)
            .ThenBy(day => day.DayNumber)
            .FirstOrDefault();

        List<Trip> nearestTrips = [];
        if (nearestDate != default)
        {
            nearestTrips = tripsByRouteAndDay.GetValueOrDefault((route.Id, nearestDate)) ?? [];
        }

        var hasTripsOnDate = tripsByRouteAndDay.ContainsKey((route.Id, selectedDate));

        return new RouteSuggestion
        {
            RouteId = route.Id,
            RouteCode = route.Code,
            RouteName = route.Name,
            Origin = route.Origin,
            Destination = route.Destination,
            DistanceKm = route.DistanceKm,
            Stops = stops.Select(ToStop).ToList(),
            MinPrice = minPrice,
            HasTripsOnDate = hasTripsOnDate,
            RunsDaily = availableDates.Count == windowDays,
            AvailableDates = availableDates.Select(ToIsoDate).ToList(),
            NearestAvailableDate = nearestDate == default ? null : ToIsoDate(nearestDate),
            NextDeparture = nearestTrips.Count == 0 ? null : ToVietnam(nearestTrips[0].DepartureTime),
            DepartureCountOnNearestDate = nearestTrips.Count,
        };
    }

    /// <summary>
    /// Mã lý do trống theo đúng thứ tự ưu tiên của hợp đồng: không có tuyến active → trạm không
    /// được phục vụ → không tuyến nào nối được → có tuyến nhưng ngày không có chuyến.
    /// </summary>
    private static string? DetermineEmptyReason(
        int activeRouteCount,
        IReadOnlySet<Guid> activeStopIds,
        Guid originStopId,
        Guid destinationStopId,
        IReadOnlyList<RouteSuggestion> matches,
        DateOnly selectedDate,
        DateOnly bookingHorizonEnd)
    {
        if (activeRouteCount == 0)
        {
            return RouteSuggestionEmptyReasons.NoActiveRoute;
        }

        if (!activeStopIds.Contains(originStopId))
        {
            return RouteSuggestionEmptyReasons.OriginNotServed;
        }

        if (!activeStopIds.Contains(destinationStopId))
        {
            return RouteSuggestionEmptyReasons.DestinationNotServed;
        }

        if (matches.Count == 0)
        {
            return RouteSuggestionEmptyReasons.NoRouteBetweenStops;
        }

        if (matches.Any(match => match.HasTripsOnDate))
        {
            return null;
        }

        return selectedDate > bookingHorizonEnd
            ? RouteSuggestionEmptyReasons.DateBeyondHorizon
            : RouteSuggestionEmptyReasons.NoTripsThatDay;
    }

    /// <summary>
    /// Giải một đầu từ id hoặc tên. Id được ưu tiên; tên khớp <b>không dấu</b>, ưu tiên khớp
    /// chính xác rồi mới tới chứa. Không giải được → 404 (khác /routes/search trả rỗng): màn
    /// hình đã cho chọn trạm từ /stops/suggestions, nên một id lạ là lỗi dữ liệu.
    /// </summary>
    private static ServiceResult<Stop> ResolveStop(
        Guid? id,
        string? name,
        string field,
        string label,
        IReadOnlyList<Stop> stops)
    {
        if (id is { } stopId)
        {
            var byId = stops.FirstOrDefault(s => s.Id == stopId);

            return byId is null
                ? ServiceResult<Stop>.NotFound($"Không tìm thấy trạm {label.ToLowerInvariant()}")
                : ServiceResult<Stop>.Ok(byId);
        }

        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            var message = $"{label} không được để trống";

            return ServiceResult<Stop>.Invalid(
                message,
                new Dictionary<string, string[]> { [field] = [message] });
        }

        var needle = Normalize(trimmed);
        var exact = stops
            .Where(s => Normalize(s.Name) == needle)
            .ToList();

        var candidates = exact.Count > 0
            ? exact
            : stops.Where(s => Normalize(s.Name).Contains(needle, StringComparison.Ordinal)).ToList();

        if (candidates.Count == 0)
        {
            return ServiceResult<Stop>.NotFound($"Không tìm thấy trạm {label.ToLowerInvariant()}");
        }

        return ServiceResult<Stop>.Ok(
            candidates
                .OrderBy(s => s.Name, NameComparer)
                .ThenBy(s => s.Id)
                .First());
    }

    private static ServiceResult<RouteSuggestionsResponse> MapFailure<T>(ServiceResult<T> failure)
        => failure.ErrorKind == ServiceErrorKind.NotFound
            ? ServiceResult<RouteSuggestionsResponse>.NotFound(failure.Error!)
            : ServiceResult<RouteSuggestionsResponse>.Invalid(failure.Error!, failure.Errors!);

    private static ServiceResult<RouteSuggestionsResponse> InvalidField(string field, string message)
        => ServiceResult<RouteSuggestionsResponse>.Invalid(
            message,
            new Dictionary<string, string[]> { [field] = [message] });

    private static StopSuggestionInfo ToInfo(Stop stop, bool onActiveRoute) => new()
    {
        Id = stop.Id,
        Name = stop.Name,
        Address = stop.Address,
        OnActiveRoute = onActiveRoute,
    };

    private static RouteSearchStop ToStop(RouteStop routeStop) => new()
    {
        StopId = routeStop.StopId,
        StopName = routeStop.Stop?.Name ?? string.Empty,
        StopOrder = routeStop.StopOrder,
    };

    private static string ToIsoDate(DateOnly day) => day.ToString(DateFormat, CultureInfo.InvariantCulture);

    private static DateOnly LocalDay(DateTime utc)
        => DateOnly.FromDateTime(DateTime.SpecifyKind(utc, DateTimeKind.Utc).Add(VietnamOffset));

    private static DateTime DayStartUtc(DateOnly day)
        => new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), VietnamOffset).UtcDateTime;

    private static DateTimeOffset ToVietnam(DateTime utc)
        => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToOffset(VietnamOffset);

    /// <summary>
    /// Chuẩn hoá tên trạm để khớp: viết thường, bỏ dấu, gộp khoảng trắng. Bản sao có chủ đích của
    /// <c>StopSearchService.Normalize</c> — cùng lý do đã ghi ở <see cref="StopSuggestionService"/>.
    /// </summary>
    private static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(character == 'đ' ? 'd' : character);
        }

        return string.Join(
            ' ',
            builder.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
