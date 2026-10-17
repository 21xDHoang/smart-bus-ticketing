using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Trips;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IFleetTripGenerationService"/> — sinh chuyến hàng loạt rồi chia VÒNG TRÒN cho
/// nhiều xe. Nợ của đợt mở rộng đội xe 17/10/2026: <see cref="RouteTripsService.GenerateAsync"/> gán
/// mọi giờ cho một xe, chạy lại "Sinh chuyến" là tái hiện cảnh một xe chạy 62 chuyến/ngày trong khi
/// 59 xe đứng không.
///
/// <para>
/// <b>Vì sao service riêng:</b> không sửa <see cref="RouteTripsService"/> (file của Hiếu — luật E1) và
/// không đổi hành vi endpoint cũ. Hợp đồng mới ở docs/api-contract.md mục
/// "POST /routes/{routeId}/trips/generate-fleet".
/// </para>
///
/// <para>
/// <b>Cách chia:</b> duyệt các giờ khởi hành theo thứ tự tăng dần; mỗi giờ tìm xe RẢNH đầu tiên theo
/// vòng tròn bắt đầu từ con trỏ (con trỏ nhảy tới sau xe vừa nhận chuyến). "Rảnh" nghĩa là khung
/// <c>[giờ đi, giờ đến]</c> không chồng lấn chuyến đang hoạt động nào của xe đó, kể cả các chuyến vừa
/// chia trong cùng lượt. Hai chuyến nối đuôi (đến đúng lúc chuyến kia khởi hành) KHÔNG tính chồng —
/// cùng quy ước với <see cref="RouteTripsService.CreateAsync"/>.
/// </para>
///
/// <para>
/// <b>Vì sao "không còn xe rảnh" là 400 chứ không phải 409:</b> đây là tham số chọn thiếu xe của
/// người điều hành (cần ít nhất <c>ceil(thời gian chạy / tần suất)</c> xe), không phải dữ liệu xung
/// đột. Màn hình đọc <c>errors.busIds</c> để báo "chọn thêm xe hoặc giãn tần suất".
/// </para>
///
/// <para>
/// <b>Nguyên tử:</b> mọi kiểm tra chạy xong mới <c>AddRange</c> + một lượt <c>SaveChanges</c>. Một
/// giờ không xếp được xe thì chưa dòng nào vào CSDL.
/// </para>
/// </summary>
public class FleetTripGenerationService : IFleetTripGenerationService
{
    /// <summary>Trần chuyến mỗi lượt sinh — cùng con số 500 của endpoint một xe.</summary>
    private const int MaxGenerateTrips = 500;

    /// <summary>Trần số xe mỗi lượt — ô chọn xe của màn hình không bao giờ cần quá con số này.</summary>
    private const int MaxBusesPerGenerate = 100;

    /// <summary>Trần chuyến đang hoạt động của một tuyến trong một ngày (UTC) — cùng trần endpoint một xe.</summary>
    private const int MaxTripsPerRoutePerDay = 200;

    private const string RouteNotFoundMessage = "Không tìm thấy tuyến đường";
    private const string RouteInactiveMessage = "Tuyến đã ngừng khai thác nên không thể sinh chuyến";
    private const string BusNotFoundMessage = "Không tìm thấy xe buýt";
    private const string BusIdsEmptyMessage = "Danh sách xe không được để trống";
    private const string BusIdsDuplicateMessage = "Danh sách xe không được có xe trùng nhau";
    private const string BusIdsTooManyMessage = "Mỗi lượt sinh tối đa 100 xe";
    private const string BusNotActiveMessage = "Xe không trong trạng thái khai thác nên không thể gán vào chuyến";
    private const string DateRangeMessage = "Thời điểm kết thúc phải sau thời điểm bắt đầu";
    private const string FrequencyMessage = "Tần suất phải từ 1 đến 1440 phút";
    private const string TooManyTripsMessage = "Khoảng thời gian quá dài, một lần sinh tối đa 500 chuyến. Thu hẹp khoảng hoặc tăng tần suất";

    private readonly AppDbContext _db;

    public FleetTripGenerationService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<GenerateFleetTripsResponse>> GenerateAsync(
        Guid routeId,
        GenerateFleetTripsRequest request,
        CancellationToken cancellationToken = default)
    {
        var route = await _db.Routes.AsNoTracking()
            .Where(r => r.Id == routeId)
            .Select(r => new { r.DistanceKm, r.Status })
            .FirstOrDefaultAsync(cancellationToken);

        if (route is null)
        {
            return ServiceResult<GenerateFleetTripsResponse>.NotFound(RouteNotFoundMessage);
        }

        if (route.Status != RouteStatus.Active)
        {
            return ServiceResult<GenerateFleetTripsResponse>.Conflict(RouteInactiveMessage);
        }

        var requestedIds = request.BusIds?.ToList() ?? [];

        if (requestedIds.Count == 0)
        {
            return InvalidField("busIds", BusIdsEmptyMessage);
        }

        if (requestedIds.Distinct().Count() != requestedIds.Count)
        {
            return InvalidField("busIds", BusIdsDuplicateMessage);
        }

        if (requestedIds.Count > MaxBusesPerGenerate)
        {
            return InvalidField("busIds", BusIdsTooManyMessage);
        }

        // AsNoTracking + chỉ gán BusId (không gán navigation): bus ở đây là dữ liệu đọc, gán
        // navigation vào Trip mới có thể khiến AddRange kéo theo INSERT Bus nếu bus chưa được track.
        var buses = await _db.Buses.AsNoTracking()
            .Where(b => requestedIds.Contains(b.Id))
            .Select(b => new { b.Id, b.LicensePlate, b.Status })
            .ToListAsync(cancellationToken);

        if (buses.Count != requestedIds.Count)
        {
            return ServiceResult<GenerateFleetTripsResponse>.NotFound(BusNotFoundMessage);
        }

        var busById = buses.ToDictionary(b => b.Id);
        var orderedBuses = requestedIds.Select(id => busById[id]).ToList();

        var inactive = orderedBuses.FirstOrDefault(b => b.Status != BusStatus.Active);

        if (inactive is not null)
        {
            return ServiceResult<GenerateFleetTripsResponse>.Conflict(
                $"{BusNotActiveMessage} (xe {inactive.LicensePlate})");
        }

        var start = request.StartTime!.Value;
        var end = request.EndTime!.Value;

        if (end <= start)
        {
            return InvalidField("endTime", DateRangeMessage);
        }

        // Model validation đã chặn 1..1440; kiểm tra lại để service không chia cho 0 khi bị gọi thẳng.
        if (request.FrequencyMinutes is < 1 or > 1440)
        {
            return InvalidField("frequencyMinutes", FrequencyMessage);
        }

        // Chuyến đầu đúng mốc bắt đầu, mỗi bước tần suất thêm một chuyến, chuyến cuối không vượt
        // quá mốc kết thúc: count = floor((end - start) / tần suất) + 1.
        var count = (long)(end - start).TotalMinutes / request.FrequencyMinutes + 1;

        if (count > MaxGenerateTrips)
        {
            return InvalidField("endTime", TooManyTripsMessage);
        }

        var startUtc = start.UtcDateTime;
        var lastDeparture = startUtc.AddMinutes((count - 1) * request.FrequencyMinutes);

        var departures = new DateTime[(int)count];
        for (var i = 0; i < departures.Length; i++)
        {
            departures[i] = startUtc.AddMinutes((long)i * request.FrequencyMinutes);
        }

        // ── Trần chuyến/ngày của tuyến: mỗi ngày (UTC) bị lịch trình chạm tới, tổng chuyến đang
        //    hoạt động (đã có + sắp sinh) không được vượt 200. ──
        var firstDay = departures[0].Date;
        var lastDay = departures[^1].Date;
        var existingRouteDepartures = await _db.Trips.AsNoTracking()
            .Where(t => t.RouteId == routeId
                && (t.Status == TripStatus.Scheduled || t.Status == TripStatus.Running)
                && t.DepartureTime >= firstDay && t.DepartureTime < lastDay.AddDays(1))
            .Select(t => t.DepartureTime)
            .ToListAsync(cancellationToken);

        for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
        {
            var existingOnDay = existingRouteDepartures.Count(d => d >= day && d < day.AddDays(1));
            var generatedOnDay = departures.Count(d => d >= day && d < day.AddDays(1));

            if (existingOnDay + generatedOnDay > MaxTripsPerRoutePerDay)
            {
                return InvalidField("endTime", DayLimitMessage(day));
            }
        }

        // ── Tuyến chỉ cấm TRÙNG ĐÚNG GIỜ KHỞI HÀNH với chuyến đang hoạt động. Hai chuyến cùng tuyến
        //    khác giờ nhau (kể cả khác xe mà khung giờ chồng lấn) là lịch chạy bình thường. ──
        var existingDepartureSet = existingRouteDepartures.ToHashSet();

        foreach (var departure in departures)
        {
            if (existingDepartureSet.Contains(departure))
            {
                return ServiceResult<GenerateFleetTripsResponse>.Conflict(
                    $"Đã có chuyến cùng tuyến khởi hành lúc {FormatUtc(departure)}");
            }
        }

        // ── Chuyến đang hoạt động của các xe được chọn, có thể chồng lấn dải sắp sinh. Lấy cả
        //    chuyến bắt đầu trước startUtc nhưng còn kéo dài vào dải; chuyến DepartureTime == startUtc
        //    phải lấy cả khi ArrivalTime null (một mốc) — đó là trường hợp trùng giờ khởi hành. ──
        var travelMinutes = ArrivalEstimatingRouteTripsService.EstimateTravelMinutes(route.DistanceKm);
        var travel = TimeSpan.FromMinutes(travelMinutes);
        var windowEnd = lastDeparture.Add(travel);
        var busIds = orderedBuses.Select(b => b.Id).ToList();

        var existingBusTrips = await _db.Trips.AsNoTracking()
            .Where(t => busIds.Contains(t.BusId)
                && (t.Status == TripStatus.Scheduled || t.Status == TripStatus.Running)
                && t.DepartureTime < windowEnd
                && (t.DepartureTime == startUtc || (t.ArrivalTime ?? t.DepartureTime) > startUtc))
            .Select(t => new
            {
                t.BusId,
                Start = t.DepartureTime,
                End = t.ArrivalTime ?? t.DepartureTime,
            })
            .ToListAsync(cancellationToken);

        var occupiedByBus = orderedBuses.ToDictionary(
            bus => bus.Id,
            _ => new List<(DateTime Start, DateTime End)>());

        foreach (var trip in existingBusTrips)
        {
            occupiedByBus[trip.BusId].Add((trip.Start, trip.End));
        }

        var assignedBusIds = new Guid[departures.Length];
        var cursor = 0;

        for (var i = 0; i < departures.Length; i++)
        {
            var departure = departures[i];
            var arrival = departure.Add(travel);
            var chosen = -1;

            // Duyệt đúng một vòng quanh danh sách xe, bắt đầu từ con trỏ. Xe nào đã nhận chuyến
            // trong lượt này cũng đã có khung giờ trong occupiedByBus nên tự động bị loại.
            for (var offset = 0; offset < orderedBuses.Count; offset++)
            {
                var index = (cursor + offset) % orderedBuses.Count;
                var intervals = occupiedByBus[orderedBuses[index].Id];

                if (intervals.Any(interval => Overlaps(interval.Start, interval.End, departure, arrival)))
                {
                    continue;
                }

                chosen = index;
                intervals.Add((departure, arrival));
                break;
            }

            if (chosen < 0)
            {
                return InvalidField(
                    "busIds",
                    $"Không còn xe rảnh cho chuyến khởi hành lúc {FormatUtc(departure)}"
                    + " — chọn thêm xe hoặc giãn tần suất");
            }

            // Con trỏ nhảy tới sau xe vừa nhận chuyến RỒI mới sang giờ kế tiếp; nhờ vậy lượt chia là
            // "vòng tròn" chứ không dồn hết vào xe đầu danh sách.
            assignedBusIds[i] = orderedBuses[chosen].Id;
            cursor = (chosen + 1) % orderedBuses.Count;
        }

        var tripCounts = orderedBuses.ToDictionary(bus => bus.Id, _ => 0);
        var trips = new List<Trip>(departures.Length);

        for (var i = 0; i < departures.Length; i++)
        {
            var busId = assignedBusIds[i];
            tripCounts[busId]++;

            trips.Add(new Trip
            {
                RouteId = routeId,
                BusId = busId,
                DepartureTime = departures[i],
                ArrivalTime = departures[i].Add(travel),
            });
        }

        _db.Trips.AddRange(trips);
        await _db.SaveChangesAsync(cancellationToken);

        var items = new List<TripResponse>(trips.Count);
        foreach (var trip in trips)
        {
            items.Add(ToResponse(trip, busById[trip.BusId].LicensePlate));
        }

        return ServiceResult<GenerateFleetTripsResponse>.Ok(new GenerateFleetTripsResponse
        {
            Items = items,
            Total = items.Count,
            BusLoads = orderedBuses.Select(bus => new FleetBusLoadResponse
            {
                BusId = bus.Id,
                BusLicensePlate = bus.LicensePlate,
                TripCount = tripCounts[bus.Id],
            }).ToList(),
        });
    }

    /// <summary>
    /// Chồng lấn giữa khung đã có và khung mới. Giờ đi trùng nhau luôn tính chồng (kể cả chuyến cũ
    /// chưa có giờ đến — coi là một mốc); nối đuôi <c>cũ.End == mới.Start</c> không tính.
    /// </summary>
    private static bool Overlaps(DateTime existingStart, DateTime existingEnd, DateTime newStart, DateTime newEnd)
        => existingStart == newStart
            || (existingStart < newEnd && existingEnd > newStart);

    private static string DayLimitMessage(DateTime day)
        => $"Tuyến đã đạt trần {MaxTripsPerRoutePerDay} chuyến trong ngày {day:yyyy-MM-dd} (UTC)";

    /// <summary>Giờ hiển thị trong thông báo lỗi — UTC, ghi rõ để không nhầm với giờ Việt Nam.</summary>
    private static string FormatUtc(DateTime time)
        => time.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    private static ServiceResult<GenerateFleetTripsResponse> InvalidField(string field, string message)
        => ServiceResult<GenerateFleetTripsResponse>.Invalid(
            message,
            new Dictionary<string, string[]> { [field] = [message] });

    private static TripResponse ToResponse(Trip trip, string busLicensePlate) => new()
    {
        Id = trip.Id,
        RouteId = trip.RouteId,
        BusId = trip.BusId,
        BusLicensePlate = busLicensePlate,
        DriverId = trip.DriverId,
        DriverName = trip.Driver?.FullName,
        DepartureTime = trip.DepartureTime,
        ArrivalTime = trip.ArrivalTime,
        Status = trip.Status.ToString(),
        CurrentStopId = trip.CurrentStopId,
        CurrentLat = trip.CurrentLat,
        CurrentLng = trip.CurrentLng,
        PositionUpdatedAt = trip.PositionUpdatedAt,
        CreatedAt = trip.CreatedAt,
        UpdatedAt = trip.UpdatedAt,
    };
}
