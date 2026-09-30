using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Trips;

namespace SmartBus.Api.Services;

/// <summary>
/// Chi tiết một chuyến xe — story 13, Vàng Thị Dăm.
/// Hợp đồng đầy đủ ở mục "Chuyến xe — /trips" của docs/api-contract.md.
///
/// Chỉ đọc: không có thao tác ghi nào ở đây, nên cũng không có <c>SaveChangesAsync</c> và không có
/// <c>try/catch DbUpdateException</c> như các service CRUD khác — không có gì để xung đột.
/// </summary>
public class TripService : ITripService
{
    private const string TripNotFoundMessage = "Không tìm thấy chuyến xe";

    private readonly AppDbContext _db;

    public TripService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<TripDetailResponse>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        // Ghép tường minh bằng join thay vì Include: Include kéo về cả entity Route lẫn Bus chỉ để
        // đọc vài cột, còn join thì máy chủ CSDL chỉ trả đúng những cột được chọn. Join cũng là thứ
        // đưa chuyến mồ côi (tuyến hoặc xe không còn) vào nhánh 404 một cách tự nhiên — join trong
        // là inner join, không khớp được thì không có dòng nào.
        var row = await (
            from trip in _db.Trips.AsNoTracking()
            join route in _db.Routes.AsNoTracking() on trip.RouteId equals route.Id
            join bus in _db.Buses.AsNoTracking() on trip.BusId equals bus.Id
            where trip.Id == id
            select new
            {
                Trip = trip,
                Route = route,
                Bus = bus,
            }).FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return ServiceResult<TripDetailResponse>.NotFound(TripNotFoundMessage);
        }

        // Thứ tự trạm của chuyến lấy từ RouteStops của TUYẾN, không phải bảng riêng của chuyến
        // (quy ước A9 — không có bảng TripStops): cùng một tuyến thì mọi chuyến đều dừng đúng dãy
        // trạm đó theo StopOrder.
        //
        // Xếp tiếp theo StopId khi hai dòng cùng StopOrder — CSDL không ràng buộc unique trên
        // (RouteId, StopOrder) nên khe hở đó là có thật (xem ghi chú ở mục "Trạm trên tuyến" của
        // api-contract.md); xếp thêm một khoá nữa thì thứ tự hiển thị luôn ổn định giữa các lần gọi.
        var stops = await (
            from routeStop in _db.RouteStops.AsNoTracking()
            join stop in _db.Stops.AsNoTracking() on routeStop.StopId equals stop.Id
            where routeStop.RouteId == row.Route.Id
            orderby routeStop.StopOrder, routeStop.StopId
            select new TripStopResponse
            {
                StopId = routeStop.StopId,
                StopName = stop.Name,
                StopAddress = stop.Address,
                Latitude = stop.Latitude,
                Longitude = stop.Longitude,
                StopOrder = routeStop.StopOrder,
                DistanceKm = routeStop.DistanceKm,
            }).ToListAsync(cancellationToken);

        return ServiceResult<TripDetailResponse>.Ok(new TripDetailResponse
        {
            Id = row.Trip.Id,
            RouteId = row.Route.Id,
            RouteCode = row.Route.Code,
            RouteName = row.Route.Name,
            Origin = row.Route.Origin,
            Destination = row.Route.Destination,
            DepartureTime = row.Trip.DepartureTime,
            ArrivalTime = row.Trip.ArrivalTime,
            Status = row.Trip.Status.ToString(),
            BusId = row.Bus.Id,
            LicensePlate = row.Bus.LicensePlate,
            BusType = row.Bus.BusType,
            Capacity = row.Bus.Capacity,
            BusStatus = row.Bus.Status.ToString(),
            Stops = stops,
        });
    }
}
