using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Trips;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Giờ đến từng trạm — đọc thì suy ra, ghi thì chỉ lưu NGOẠI LỆ.
///
/// <para>
/// Vì sao không lưu sẵn một dòng cho mỗi (chuyến, trạm): một tuyến 10 trạm sinh 20 chuyến/ngày
/// sẽ đẻ 200 dòng mỗi ngày, trong khi phần lớn giờ đến chỉ là hàm của km. Lưu ngoại lệ giữ bảng
/// nhỏ và — quan trọng hơn — sửa khoảng cách trên tuyến là giờ dự kiến của mọi chuyến tự đúng lại,
/// không phải chạy job cập nhật.
/// </para>
///
/// <para>
/// Công thức giờ trung gian: <c>DepartureTime + round(cumulativeKm / totalKm × travelMinutes)</c>,
/// làm tròn tới phút gần nhất và kẹp trong khoảng (0, travelMinutes) để không đè lên hai mốc neo.
/// Trạm đầu luôn đúng <c>DepartureTime</c>; bến cuối luôn đúng <c>ArrivalTime</c> (đã lưu, hoặc
/// ước lượng 25 km/h khi trống). Hai mốc neo không bao giờ bị ghi đè.
/// </para>
/// </summary>
public class TripStopTimeService : ITripStopTimeService
{
    /// <summary>Khoá lỗi theo từng dòng — cùng cấu trúc { message, errors } của dự án.</summary>
    private const string ItemsField = "items";

    private readonly AppDbContext _db;
    private readonly ILogger<TripStopTimeService> _logger;

    public TripStopTimeService(AppDbContext db, ILogger<TripStopTimeService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ServiceResult<TripStopTimesResponse>> GetAsync(
        Guid routeId,
        Guid tripId,
        CancellationToken cancellationToken = default)
    {
        var trip = await _db.Trips
            .AsNoTracking()
            .Include(t => t.Route)
            .FirstOrDefaultAsync(t => t.Id == tripId && t.RouteId == routeId, cancellationToken);

        if (trip is null)
        {
            return ServiceResult<TripStopTimesResponse>.NotFound("Không tìm thấy chuyến xe.");
        }

        var stops = await LoadStopsAsync(routeId, cancellationToken);
        var overrides = await _db.TripStopTimeOverrides
            .AsNoTracking()
            .Where(o => o.TripId == tripId)
            .ToDictionaryAsync(o => o.StopId, o => o.ArrivalTime, cancellationToken);

        return ServiceResult<TripStopTimesResponse>.Ok(BuildResponse(trip, stops, overrides));
    }

    public async Task<ServiceResult<TripStopTimesResponse>> UpdateAsync(
        Guid routeId,
        Guid tripId,
        UpdateTripStopTimesRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Items is null)
        {
            return ItemError("Danh sách giờ đến từng trạm không được để trống.");
        }

        var trip = await _db.Trips
            .Include(t => t.Route)
            .FirstOrDefaultAsync(t => t.Id == tripId && t.RouteId == routeId, cancellationToken);

        if (trip is null)
        {
            return ServiceResult<TripStopTimesResponse>.NotFound("Không tìm thấy chuyến xe.");
        }

        if (trip.Status is TripStatus.Cancelled or TripStatus.Completed)
        {
            return ServiceResult<TripStopTimesResponse>.Conflict(
                "Chuyến đã huỷ hoặc đã chạy xong nên không sửa được giờ đến từng trạm.");
        }

        var stops = await LoadStopsAsync(routeId, cancellationToken);
        var indexByStopId = stops
            .Select((stop, index) => (stop.StopId, Index: index))
            .ToDictionary(item => item.StopId, item => item.Index);

        var finalOverrides = new Dictionary<Guid, DateTime>();
        var seenStopIds = new HashSet<Guid>();

        foreach (var item in request.Items)
        {
            if (item.StopId is not { } stopId || stopId == Guid.Empty)
            {
                return ItemError("Có dòng thiếu trạm dừng.");
            }

            if (!seenStopIds.Add(stopId))
            {
                return ItemError("Danh sách có trạm bị lặp lại.");
            }

            if (!indexByStopId.TryGetValue(stopId, out var index))
            {
                return ItemError("Có trạm không thuộc tuyến của chuyến.");
            }

            // Trạm đầu / bến cuối là mốc neo của chuyến — sửa ở giờ chạy, không sửa ở đây.
            if (index == 0 || index == stops.Count - 1)
            {
                return ItemError(
                    "Chỉ sửa được giờ của trạm trung gian; giờ trạm đầu và bến cuối sửa ở giờ chạy của chuyến.");
            }

            if (item.ArrivalTime is { } arrivalTime)
            {
                finalOverrides[stopId] = arrivalTime.UtcDateTime;
            }
        }

        var response = BuildResponse(trip, stops, finalOverrides);
        var timelineError = ValidateTimeline(response, finalOverrides.Keys.ToHashSet());
        if (timelineError is not null)
        {
            return ItemError(timelineError);
        }

        try
        {
            await PersistOverridesAsync(tripId, finalOverrides, cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // Hai quản trị viên lưu cùng lúc: unique (TripId, StopId) chặn dòng thứ hai.
            _logger.LogWarning(
                ex,
                "Xung đột khi lưu giờ đến từng trạm cho chuyến {TripId}",
                tripId);
            return ServiceResult<TripStopTimesResponse>.Conflict(
                "Giờ đến từng trạm vừa được người khác cập nhật. Vui lòng tải lại rồi thử lại.");
        }

        return ServiceResult<TripStopTimesResponse>.Ok(response);
    }

    private async Task<List<StopRow>> LoadStopsAsync(Guid routeId, CancellationToken cancellationToken)
        => await _db.RouteStops
            .AsNoTracking()
            .Where(rs => rs.RouteId == routeId)
            .OrderBy(rs => rs.StopOrder)
            .ThenBy(rs => rs.StopId)
            .Select(rs => new StopRow(
                rs.StopId,
                rs.Stop!.Name,
                rs.Stop!.Address,
                rs.StopOrder,
                rs.DistanceKm))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Ghi đè thay thế toàn phần: mốc nào không còn trong <paramref name="finalOverrides"/> thì
    /// xoá; mốc cũ giữ nguyên <c>CreatedAt</c> và chỉ đổi khi giờ thật sự khác.
    /// </summary>
    private async Task PersistOverridesAsync(
        Guid tripId,
        IReadOnlyDictionary<Guid, DateTime> finalOverrides,
        CancellationToken cancellationToken)
    {
        var existing = await _db.TripStopTimeOverrides
            .Where(o => o.TripId == tripId)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var existingStopIds = new HashSet<Guid>();

        foreach (var row in existing)
        {
            existingStopIds.Add(row.StopId);

            if (finalOverrides.TryGetValue(row.StopId, out var arrivalTime))
            {
                if (row.ArrivalTime != arrivalTime)
                {
                    row.ArrivalTime = arrivalTime;
                    row.UpdatedAt = now;
                }
            }
            else
            {
                _db.TripStopTimeOverrides.Remove(row);
            }
        }

        foreach (var (stopId, arrivalTime) in finalOverrides)
        {
            if (existingStopIds.Contains(stopId))
            {
                continue;
            }

            _db.TripStopTimeOverrides.Add(new TripStopTimeOverride
            {
                TripId = tripId,
                StopId = stopId,
                ArrivalTime = arrivalTime,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Dựng dòng thời gian từ dữ liệu chuyến + km của tuyến + các mốc ghi đè.
    /// Hàm thuần (không truy vấn) để GET và PUT dùng chung một nguồn số.
    /// </summary>
    private static TripStopTimesResponse BuildResponse(
        Trip trip,
        IReadOnlyList<StopRow> stops,
        IReadOnlyDictionary<Guid, DateTime> overrides)
    {
        var departureTime = trip.DepartureTime;
        var totalDistanceKm = stops.Skip(1).Sum(stop => stop.DistanceKm);
        var hasDistanceData = stops.Count >= 2 && totalDistanceKm > 0;

        // Bến cuối: giờ đã lưu là nguồn sự thật; trống thì ước lượng từ chiều dài tuyến.
        DateTime? effectiveArrivalTime = trip.ArrivalTime;
        var arrivalTimeIsEstimated = false;
        int? travelMinutes = null;

        if (trip.ArrivalTime is { } storedArrival)
        {
            var storedMinutes = (storedArrival - departureTime).TotalMinutes;
            if (storedMinutes >= 1)
            {
                travelMinutes = (int)Math.Round(storedMinutes, MidpointRounding.AwayFromZero);
            }
        }
        else if (trip.Route is { DistanceKm: > 0 } route)
        {
            travelMinutes = ArrivalEstimatingRouteTripsService.EstimateTravelMinutes(route.DistanceKm);
            effectiveArrivalTime = departureTime.AddMinutes(travelMinutes.Value);
            arrivalTimeIsEstimated = true;
        }

        var items = new List<TripStopTimeResponse>(stops.Count);
        var cumulativeDistanceKm = 0m;

        for (var index = 0; index < stops.Count; index++)
        {
            var stop = stops[index];
            var isFirst = index == 0;
            var isLast = index == stops.Count - 1;

            if (!isFirst)
            {
                cumulativeDistanceKm += stop.DistanceKm;
            }

            DateTime? derivedArrivalTime;
            DateTime? arrivalTime;
            var isManual = false;

            if (isFirst)
            {
                // Trạm đầu là mốc neo — luôn đúng giờ khởi hành, không bao giờ bị ghi đè.
                derivedArrivalTime = departureTime;
                arrivalTime = departureTime;
            }
            else if (isLast)
            {
                // Bến cuối là mốc neo — luôn đúng giờ đến của chuyến.
                derivedArrivalTime = effectiveArrivalTime;
                arrivalTime = effectiveArrivalTime;
            }
            else
            {
                derivedArrivalTime = DeriveIntermediateArrival(
                    departureTime,
                    cumulativeDistanceKm,
                    totalDistanceKm,
                    travelMinutes,
                    hasDistanceData);

                if (overrides.TryGetValue(stop.StopId, out var manualArrival))
                {
                    arrivalTime = manualArrival;
                    isManual = true;
                }
                else
                {
                    arrivalTime = derivedArrivalTime;
                }
            }

            items.Add(new TripStopTimeResponse
            {
                StopId = stop.StopId,
                StopName = stop.StopName,
                StopAddress = stop.StopAddress,
                StopOrder = stop.StopOrder,
                // Trạm đầu luôn trả 0 — dữ liệu cũ có thể còn số khác dù quy ước là 0.
                DistanceKm = isFirst ? 0 : stop.DistanceKm,
                CumulativeDistanceKm = cumulativeDistanceKm,
                ArrivalTime = arrivalTime,
                DerivedArrivalTime = derivedArrivalTime,
                IsManual = isManual,
                IsFirst = isFirst,
                IsLast = isLast,
            });
        }

        return new TripStopTimesResponse
        {
            TripId = trip.Id,
            RouteId = trip.RouteId,
            DepartureTime = departureTime,
            ArrivalTime = effectiveArrivalTime,
            ArrivalTimeIsEstimated = arrivalTimeIsEstimated,
            HasDistanceData = hasDistanceData,
            TotalDistanceKm = totalDistanceKm > 0 ? totalDistanceKm : 0,
            Stops = items,
        };
    }

    /// <summary>
    /// Giờ dự kiến của một trạm trung gian. Trả null khi không có đủ dữ liệu km / giờ chạy —
    /// KHÔNG bịa giờ. Kẹp trong (0, travelMinutes) để không đè lên trạm đầu / bến cuối.
    /// </summary>
    private static DateTime? DeriveIntermediateArrival(
        DateTime departureTime,
        decimal cumulativeDistanceKm,
        decimal totalDistanceKm,
        int? travelMinutes,
        bool hasDistanceData)
    {
        if (!hasDistanceData || travelMinutes is not >= 2 || totalDistanceKm <= 0)
        {
            return null;
        }

        var ratio = cumulativeDistanceKm / totalDistanceKm;
        var minutes = (int)Math.Round(
            ratio * travelMinutes.Value,
            MidpointRounding.AwayFromZero);

        minutes = Math.Clamp(minutes, 1, travelMinutes.Value - 1);
        return departureTime.AddMinutes(minutes);
    }

    /// <summary>
    /// Kiểm tra thứ tự của các mốc GHI ĐÈ so với hai mốc neo và các mốc ghi đè khác.
    ///
    /// <para>
    /// Không so sánh hai mốc cùng là giờ dự kiến: khi chuyến quá ngắn (ít phút hơn số trạm),
    /// phép làm tròn có thể cho hai trạm cùng một phút — đó là giới hạn dữ liệu, không phải lỗi
    /// của người sửa. Nhưng hễ một trong hai mốc là do người đặt thì thứ tự phải tuyệt đối đúng.
    /// </para>
    /// </summary>
    private static string? ValidateTimeline(
        TripStopTimesResponse response,
        IReadOnlySet<Guid> manualStopIds)
    {
        DateTime? previousTime = response.DepartureTime;
        var previousIsManual = false;

        foreach (var stop in response.Stops)
        {
            if (stop.IsFirst)
            {
                previousTime = stop.ArrivalTime ?? previousTime;
                previousIsManual = false;
                continue;
            }

            if (stop.ArrivalTime is not { } arrivalTime)
            {
                // Không có dữ liệu km thì không có mốc để so — bỏ qua, không bịa lỗi.
                continue;
            }

            if (stop.IsLast)
            {
                if (previousTime is { } lastPrevious && previousIsManual && lastPrevious >= arrivalTime)
                {
                    return "Giờ đến trạm trung gian phải trước giờ đến bến cuối.";
                }

                break;
            }

            var isManual = manualStopIds.Contains(stop.StopId);

            if (previousTime is { } previous && arrivalTime <= previous
                && (isManual || previousIsManual))
            {
                return $"Giờ đến trạm “{stop.StopName}” phải sau mốc của trạm trước đó.";
            }

            previousTime = arrivalTime;
            previousIsManual = isManual;
        }

        return null;
    }

    private static ServiceResult<TripStopTimesResponse> ItemError(string message)
        => ServiceResult<TripStopTimesResponse>.Invalid(
            message,
            new Dictionary<string, string[]> { [ItemsField] = [message] });

    private sealed record StopRow(
        Guid StopId,
        string StopName,
        string StopAddress,
        int StopOrder,
        decimal DistanceKm);
}
