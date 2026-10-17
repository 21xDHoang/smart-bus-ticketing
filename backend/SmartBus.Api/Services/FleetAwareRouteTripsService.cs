using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Trips;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Lớp bọc sửa NGỮ NGHĨA "trùng khung giờ" của <see cref="IRouteTripsService.CreateAsync"/> cho đội xe
/// nhiều chiếc (nợ của đợt mở rộng đội xe 17/10/2026, Phùng Duy Hoàng).
///
/// <para>
/// <b>Vấn đề:</b> <see cref="RouteTripsService.CreateAsync"/> coi MỌI chuyến cùng tuyến là xung đột.
/// Điều đó đúng khi tuyến chỉ có một xe, nhưng với tuyến đang chạy 5–12 xe thì hai chuyến cùng tuyến
/// khác giờ nhau (kể cả khung giờ chồng lấn do hai xe khác nhau chạy) là lịch bình thường. Hệ quả cũ:
/// thêm một chuyến lẻ vào tuyến đã có lịch luôn bị 409.
/// </para>
///
/// <para>
/// <b>Ngữ nghĩa mới</b> (docs/api-contract.md mục "Hai kiểm tra khi tạo lịch trình"):
/// <list type="bullet">
///   <item>cùng XE: khung giờ chồng lấn → 409 — một xe không chạy hai chuyến cùng lúc;</item>
///   <item>cùng TUYẾN: chỉ trùng ĐÚNG giờ khởi hành → 409 — hai chuyến cùng giờ trên cùng tuyến là
///   lịch trùng thật, còn khác giờ nhau thì xe nào lo chuyến nấy.</item>
/// </list>
/// Nối đuôi (đến đúng lúc chuyến kia khởi hành) không tính chồng, cùng <see cref="RouteTripsService"/>.
/// </para>
///
/// <para>
/// <b>Vì sao là decorator:</b> file <see cref="RouteTripsService"/> của Trần Trung Hiếu (luật E1). Lớp
/// này nằm giữa <see cref="ArrivalEstimatingRouteTripsService"/> và lớp trần, chỉ thay
/// <see cref="CreateAsync"/>; năm lượt còn lại chuyển tiếp nguyên vẹn. Chuỗi DI:
/// Notifying → ArrivalEstimating → FleetAware (lớp này) → RouteTripsService.
/// </para>
///
/// <para>
/// Các kiểm tra còn lại giữ nguyên thứ tự và mã lỗi của lớp trần: tuyến 404, xe 404/409, giờ đến
/// trước giờ đi 400 <c>errors.arrivalTime</c>, trần 200 chuyến/ngày (UTC) 400
/// <c>errors.departureTime</c>.
/// </para>
/// </summary>
public class FleetAwareRouteTripsService : IRouteTripsService
{
    private const string RouteNotFoundMessage = "Không tìm thấy tuyến đường";
    private const string BusNotFoundMessage = "Không tìm thấy xe buýt";
    private const string BusNotActiveMessage = "Xe không trong trạng thái khai thác nên không thể gán vào chuyến";
    private const string ArrivalBeforeDepartureMessage = "Giờ đến phải sau giờ khởi hành";
    private const string BusOverlapMessage = "Xe đã có chuyến khác trong khung giờ này";
    private const string RouteDuplicateMessage = "Đã có chuyến cùng tuyến khởi hành đúng giờ này";
    private const int MaxTripsPerRoutePerDay = 200;

    private readonly IRouteTripsService _inner;
    private readonly AppDbContext _db;

    public FleetAwareRouteTripsService(IRouteTripsService inner, AppDbContext db)
    {
        _inner = inner;
        _db = db;
    }

    public Task<ServiceResult<TripListResponse>> ListAsync(
        Guid routeId,
        ListTripsRequest request,
        CancellationToken cancellationToken = default)
        => _inner.ListAsync(routeId, request, cancellationToken);

    public Task<ServiceResult<TripResponse>> GetByIdAsync(
        Guid routeId,
        Guid id,
        CancellationToken cancellationToken = default)
        => _inner.GetByIdAsync(routeId, id, cancellationToken);

    /// <summary>
    /// Thêm chuyến lẻ với ngữ nghĩa trùng giờ đã sửa: cùng xe thì cấm chồng khung giờ, cùng tuyến
    /// thì chỉ cấm trùng đúng giờ khởi hành.
    /// </summary>
    public async Task<ServiceResult<TripResponse>> CreateAsync(
        Guid routeId,
        CreateTripRequest request,
        CancellationToken cancellationToken = default)
    {
        var routeExists = await _db.Routes.AsNoTracking()
            .AnyAsync(r => r.Id == routeId, cancellationToken);

        if (!routeExists)
        {
            return ServiceResult<TripResponse>.NotFound(RouteNotFoundMessage);
        }

        // AsNoTracking + chỉ gán BusId vào Trip mới: gán navigation vào entity mới có thể khiến
        // SaveChanges kéo theo INSERT Bus nếu bus chưa được track.
        var bus = await _db.Buses.AsNoTracking()
            .Where(b => b.Id == request.BusId!.Value)
            .Select(b => new { b.Id, b.LicensePlate, b.Status })
            .FirstOrDefaultAsync(cancellationToken);

        if (bus is null)
        {
            return ServiceResult<TripResponse>.NotFound(BusNotFoundMessage);
        }

        if (bus.Status != BusStatus.Active)
        {
            return ServiceResult<TripResponse>.Conflict(BusNotActiveMessage);
        }

        var departureUtc = request.DepartureTime!.Value.UtcDateTime;
        var arrivalUtc = request.ArrivalTime?.UtcDateTime;

        if (arrivalUtc is { } arrival && arrival < departureUtc)
        {
            return InvalidField("arrivalTime", ArrivalBeforeDepartureMessage);
        }

        // Trần chuyến/ngày (UTC) của tuyến — giới hạn theo ngày, báo trước xung đột.
        var day = departureUtc.Date;
        var tripsOnDay = await _db.Trips.AsNoTracking()
            .CountAsync(t => t.RouteId == routeId
                && (t.Status == TripStatus.Scheduled || t.Status == TripStatus.Running)
                && t.DepartureTime >= day && t.DepartureTime < day.AddDays(1),
                cancellationToken);

        if (tripsOnDay >= MaxTripsPerRoutePerDay)
        {
            return InvalidField("departureTime", DayLimitMessage(day));
        }

        // Cùng TUYẾN: chỉ chặn trùng đúng giờ khởi hành.
        var routeDuplicate = await _db.Trips.AsNoTracking()
            .Where(t => t.RouteId == routeId
                && (t.Status == TripStatus.Scheduled || t.Status == TripStatus.Running)
                && t.DepartureTime == departureUtc)
            .OrderBy(t => t.DepartureTime)
            .FirstOrDefaultAsync(cancellationToken);

        if (routeDuplicate is not null)
        {
            return ServiceResult<TripResponse>.Conflict(
                $"{RouteDuplicateMessage} (chuyến hiện có khởi hành lúc {FormatUtc(routeDuplicate.DepartureTime)})");
        }

        // Cùng XE: khung giờ chồng lấn. Chưa có giờ đến thì coi chuyến mới là một mốc — vẫn bắt được
        // chuyến cũ bắt đầu trước và kéo dài qua mốc đó. Nối đuôi không tính chồng.
        var effectiveEnd = arrivalUtc ?? departureUtc;
        var busConflict = await _db.Trips.AsNoTracking()
            .Where(t => t.BusId == bus.Id
                && (t.Status == TripStatus.Scheduled || t.Status == TripStatus.Running)
                && (t.DepartureTime == departureUtc
                    || (t.DepartureTime < effectiveEnd && (t.ArrivalTime ?? t.DepartureTime) > departureUtc)))
            .OrderBy(t => t.DepartureTime)
            .FirstOrDefaultAsync(cancellationToken);

        if (busConflict is not null)
        {
            return ServiceResult<TripResponse>.Conflict(
                $"{BusOverlapMessage} (chuyến hiện có khởi hành lúc {FormatUtc(busConflict.DepartureTime)})");
        }

        var trip = new Trip
        {
            RouteId = routeId,
            BusId = bus.Id,
            DepartureTime = departureUtc,
            ArrivalTime = arrivalUtc,
        };

        _db.Trips.Add(trip);
        await _db.SaveChangesAsync(cancellationToken);

        return ServiceResult<TripResponse>.Ok(ToResponse(trip, bus.LicensePlate));
    }

    public Task<ServiceResult<TripResponse>> UpdateAsync(
        Guid routeId,
        Guid id,
        UpdateTripRequest request,
        CancellationToken cancellationToken = default)
        => _inner.UpdateAsync(routeId, id, request, cancellationToken);

    public Task<ServiceResult<TripResponse>> DeleteAsync(
        Guid routeId,
        Guid id,
        CancellationToken cancellationToken = default)
        => _inner.DeleteAsync(routeId, id, cancellationToken);

    public Task<ServiceResult<GenerateTripsResponse>> GenerateAsync(
        Guid routeId,
        GenerateTripsRequest request,
        CancellationToken cancellationToken = default)
        => _inner.GenerateAsync(routeId, request, cancellationToken);

    private static string DayLimitMessage(DateTime day)
        => $"Tuyến đã đạt trần {MaxTripsPerRoutePerDay} chuyến trong ngày {day:yyyy-MM-dd} (UTC)";

    private static string FormatUtc(DateTime time)
        => time.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    private static ServiceResult<TripResponse> InvalidField(string field, string message)
        => ServiceResult<TripResponse>.Invalid(
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
