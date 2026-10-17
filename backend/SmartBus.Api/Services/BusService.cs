using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Buses;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IBusService"/>. Story 14 — Trần Trung Hiếu.
/// Bám sát khuôn <see cref="RouteService"/>: cùng cách lọc, cùng cách chặn trùng khoá
/// nghiệp vụ, cùng cách xoá mềm.
/// </summary>
public class BusService : IBusService
{
    private const string BusNotFoundMessage = "Không tìm thấy xe buýt";
    private const string LicensePlateExistsMessage = "Biển số đã tồn tại";
    private const string StatusInvalidMessage = "Trạng thái không hợp lệ";

    private const int DefaultPageSize = 10;

    /// <summary>
    /// Cửa sổ "xe đang phụ trách tuyến nào": chuyến <c>Scheduled</c> chỉ được tính khi khởi hành
    /// trong khoảng [bây giờ, bây giờ + 7 ngày). Chuyến xa hơn không phải phân công hiện tại —
    /// hiển thị chúng sẽ làm cột tuyến đầy những chuyến của tháng sau.
    /// </summary>
    private static readonly TimeSpan AssignmentHorizon = TimeSpan.FromDays(7);

    private readonly AppDbContext _db;

    public BusService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<BusListResponse>> ListAsync(
        ListBusesRequest request,
        CancellationToken cancellationToken = default)
    {
        var page = request.Page ?? 1;
        var pageSize = request.PageSize ?? DefaultPageSize;

        var query = _db.Buses.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            // So khớp không phân biệt hoa thường bằng ToLower() thay vì EF.Functions.ILike:
            // ILike là hàm riêng của Npgsql, dùng nó thì test chạy trên provider InMemory sẽ đổ.
            var keyword = request.Search.Trim().ToLowerInvariant();

            query = query.Where(b =>
                b.LicensePlate.ToLower().Contains(keyword) ||
                b.BusType.ToLower().Contains(keyword));
        }

        if (!string.IsNullOrWhiteSpace(request.Status) &&
            TryParseBusStatus(request.Status, out var status))
        {
            query = query.Where(b => b.Status == status);
        }
        else if (!string.IsNullOrWhiteSpace(request.Status))
        {
            // Mã trạng thái không khớp "Active"/"Maintenance"/"Inactive": không báo lỗi mà trả
            // danh sách rỗng — cùng lối RouteService lọc trạng thái tuyến.
            return ServiceResult<BusListResponse>.Ok(new BusListResponse
            {
                Items = [],
                Total = 0,
                Page = page,
                PageSize = pageSize,
            });
        }

        var total = await query.CountAsync(cancellationToken);

        var buses = await query
            .OrderByDescending(b => b.CreatedAt)
            // Chốt thêm theo Id: nhiều xe tạo cùng lúc có thể trùng CreatedAt, thiếu khoá
            // phụ thì thứ tự giữa các trang không ổn định và phân trang bị trùng/thiếu dòng.
            .ThenBy(b => b.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return ServiceResult<BusListResponse>.Ok(new BusListResponse
        {
            Items = await ToResponsesAsync(buses, cancellationToken),
            Total = total,
            Page = page,
            PageSize = pageSize,
        });
    }

    public async Task<ServiceResult<BusResponse>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var bus = await _db.Buses
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

        if (bus is null)
        {
            return ServiceResult<BusResponse>.NotFound(BusNotFoundMessage);
        }

        var responses = await ToResponsesAsync([bus], cancellationToken);
        return ServiceResult<BusResponse>.Ok(responses[0]);
    }

    public async Task<ServiceResult<BusResponse>> CreateAsync(
        CreateBusRequest request,
        CancellationToken cancellationToken = default)
    {
        var licensePlate = request.LicensePlate.Trim();

        if (await LicensePlateTakenAsync(licensePlate, exceptBusId: null, cancellationToken))
        {
            return InvalidField("licensePlate", LicensePlateExistsMessage);
        }

        var bus = new Bus
        {
            LicensePlate = licensePlate,
            BusType = request.BusType.Trim(),
            Capacity = request.Capacity,
        };

        _db.Buses.Add(bus);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Hai request cùng biển số chạy song song đều lọt qua bước kiểm tra ở trên.
            // Ràng buộc unique trên cột LicensePlate là lớp chặn cuối cùng.
            return InvalidField("licensePlate", LicensePlateExistsMessage);
        }

        var responses = await ToResponsesAsync([bus], cancellationToken);
        return ServiceResult<BusResponse>.Ok(responses[0]);
    }

    public async Task<ServiceResult<BusResponse>> UpdateAsync(
        Guid id,
        UpdateBusRequest request,
        CancellationToken cancellationToken = default)
    {
        var bus = await _db.Buses.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (bus is null)
        {
            return ServiceResult<BusResponse>.NotFound(BusNotFoundMessage);
        }

        var licensePlate = request.LicensePlate.Trim();

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!TryParseBusStatus(request.Status, out var status))
            {
                return InvalidField("status", StatusMessage());
            }

            bus.Status = status;
        }

        if (licensePlate != bus.LicensePlate &&
            await LicensePlateTakenAsync(licensePlate, exceptBusId: id, cancellationToken))
        {
            return InvalidField("licensePlate", LicensePlateExistsMessage);
        }

        bus.LicensePlate = licensePlate;
        bus.BusType = request.BusType.Trim();
        bus.Capacity = request.Capacity;
        bus.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return InvalidField("licensePlate", LicensePlateExistsMessage);
        }

        var responses = await ToResponsesAsync([bus], cancellationToken);
        return ServiceResult<BusResponse>.Ok(responses[0]);
    }

    public async Task<ServiceResult<BusResponse>> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var bus = await _db.Buses.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (bus is null)
        {
            return ServiceResult<BusResponse>.NotFound(BusNotFoundMessage);
        }

        // Xoá mềm = chuyển về Inactive (quy ước A4: cấm thêm cột IsDeleted, và Bus đã có sẵn
        // cột trạng thái cho đúng việc này). Xe đã Inactive gọi lại thì trả về luôn —
        // idempotent, cùng lối RouteService.DeleteAsync.
        if (bus.Status != BusStatus.Inactive)
        {
            bus.Status = BusStatus.Inactive;
            bus.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        var responses = await ToResponsesAsync([bus], cancellationToken);
        return ServiceResult<BusResponse>.Ok(responses[0]);
    }

    /// <summary>
    /// Ghép thông tin phân công (tuyến, chuyến đang chạy/kế tiếp, ghế) vào response của một lượt
    /// xe. Làm theo LÔ cho cả trang: 4 truy vấn cho N xe, không phải 4 truy vấn mỗi xe.
    ///
    /// Vì sao đọc từ <c>Trips</c> chứ không có bảng gán xe ↔ tuyến: quy ước A9 đóng băng danh sách
    /// bảng, và chuyến đã mang đủ ba thông tin (xe, tuyến, giờ). Thêm bảng thứ hai sẽ tạo câu hỏi
    /// "sửa bảng gán thì chuyến đã sinh có đổi theo không?" — cùng lý do quyết định A8.3 không tách
    /// bảng Schedule.
    /// </summary>
    private async Task<List<BusResponse>> ToResponsesAsync(
        IReadOnlyList<Bus> buses,
        CancellationToken cancellationToken)
    {
        var responses = buses.Select(ToResponse).ToList();

        if (buses.Count == 0)
        {
            return responses;
        }

        var now = DateTime.UtcNow;
        var horizon = now + AssignmentHorizon;
        var busIds = buses.Select(b => b.Id).ToList();
        var busTypes = buses.Select(b => b.BusType).Distinct().ToList();

        // Chỉ lấy chuyến đang chạy + chuyến sắp chạy trong cửa sổ. Một xe có thể có hàng nghìn
        // chuyến đã chạy — kéo hết về rồi lọc trong bộ nhớ là trả giá vô ích cho mỗi lần mở trang.
        var trips = await _db.Trips
            .AsNoTracking()
            .Where(t => busIds.Contains(t.BusId) &&
                (t.Status == TripStatus.Running ||
                 (t.Status == TripStatus.Scheduled && t.DepartureTime >= now && t.DepartureTime < horizon)))
            .Select(t => new
            {
                TripId = t.Id,
                t.BusId,
                t.RouteId,
                RouteCode = t.Route!.Code,
                RouteName = t.Route!.Name,
                t.DepartureTime,
                t.DriverId,
                DriverName = t.Driver == null ? null : t.Driver.FullName,
                t.Status,
                CurrentStopName = t.CurrentStop == null ? null : t.CurrentStop.Name,
            })
            .ToListAsync(cancellationToken);

        var seatCounts = await _db.Seats
            .AsNoTracking()
            .Where(s => busIds.Contains(s.BusId))
            .GroupBy(s => s.BusId)
            .Select(g => new { BusId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.BusId, x => x.Count, cancellationToken);

        // Sơ đồ ghế là của LOẠI XE, không phải của từng xe (unique theo BusType). Một lượt đọc
        // cho tất cả loại xe đang có trên trang.
        var layouts = await _db.SeatLayouts
            .AsNoTracking()
            .Where(l => busTypes.Contains(l.BusType))
            .ToListAsync(cancellationToken);
        var layoutByBusType = layouts
            .GroupBy(l => l.BusType)
            .ToDictionary(g => g.Key, g => g.First());

        var tripsByBus = trips.ToLookup(t => t.BusId);

        foreach (var response in responses)
        {
            var busTrips = tripsByBus[response.Id].ToList();

            var current = busTrips
                .Where(t => t.Status == TripStatus.Running)
                .OrderBy(t => t.DepartureTime)
                .FirstOrDefault();

            var next = busTrips
                .Where(t => t.Status == TripStatus.Scheduled)
                .OrderBy(t => t.DepartureTime)
                .FirstOrDefault();

            // Một tuyến = một dòng, kể cả khi xe chạy tuyến đó nhiều chuyến. Sắp theo chuyến gần
            // nhất để tuyến sắp phục vụ đứng trước; tuyến chỉ còn chuyến đang chạy xếp cuối.
            response.AssignedRoutes = busTrips
                .GroupBy(t => t.RouteId)
                .Select(g => new BusRouteAssignmentResponse
                {
                    RouteId = g.Key,
                    RouteCode = g.First().RouteCode,
                    RouteName = g.First().RouteName,
                    TripCount = g.Count(),
                    NextDepartureTime = g
                        .Where(t => t.Status == TripStatus.Scheduled)
                        .Select(t => (DateTime?)t.DepartureTime)
                        .Min(),
                })
                .OrderBy(r => r.NextDepartureTime ?? DateTime.MaxValue)
                .ThenBy(r => r.RouteCode, StringComparer.Ordinal)
                .ToList();

            response.CurrentTrip = current is null ? null : new BusTripSummaryResponse
            {
                TripId = current.TripId,
                RouteId = current.RouteId,
                RouteCode = current.RouteCode,
                RouteName = current.RouteName,
                DepartureTime = current.DepartureTime,
                DriverId = current.DriverId,
                DriverName = current.DriverName,
                Status = current.Status.ToString(),
                CurrentStopName = current.CurrentStopName,
            };

            response.NextTrip = next is null ? null : new BusTripSummaryResponse
            {
                TripId = next.TripId,
                RouteId = next.RouteId,
                RouteCode = next.RouteCode,
                RouteName = next.RouteName,
                DepartureTime = next.DepartureTime,
                DriverId = next.DriverId,
                DriverName = next.DriverName,
                Status = next.Status.ToString(),
                // Trạm hiện tại là chuyện của chuyến ĐANG chạy; chuyến Scheduled chưa có vị trí.
                CurrentStopName = null,
            };

            layoutByBusType.TryGetValue(response.BusType, out var layout);
            var seatsCreated = seatCounts.GetValueOrDefault(response.Id);

            response.SeatSummary = new BusSeatSummaryResponse
            {
                SeatsCreated = seatsCreated,
                Capacity = response.Capacity,
                // >= chứ không phải == : dữ liệu cũ có thể đã sinh nhiều hơn sức chứa khai báo.
                // Đó vẫn là "đã có đủ ghế", chỉ là con số khai báo cần xem lại.
                IsComplete = seatsCreated >= response.Capacity,
                Layout = layout is null ? null : new BusSeatLayoutSummaryResponse
                {
                    NumberOfFloors = layout.NumberOfFloors,
                    RowsPerFloor = layout.RowsPerFloor,
                    ColumnsPerRow = layout.ColumnsPerRow,
                    TotalSeats = layout.TotalSeats,
                    VipSeatCount = CountVipSeats(layout.VipSeatPositions),
                },
            };
        }

        return responses;
    }

    /// <summary>
    /// Đếm ghế VIP từ chuỗi toạ độ "1-1-1;1-1-2". Dùng Split thay vì lưu sẵn số lượng vì
    /// <c>VipSeatPositions</c> là nguồn duy nhất — thêm cột đếm là hai nguồn có thể lệch nhau.
    /// </summary>
    private static int CountVipSeats(string vipSeatPositions)
        => vipSeatPositions
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Length;

    /// <summary>Biển số là khoá nghiệp vụ duy nhất toàn hệ thống — có ràng buộc unique ở CSDL.</summary>
    private Task<bool> LicensePlateTakenAsync(string licensePlate, Guid? exceptBusId, CancellationToken cancellationToken)
    {
        var query = _db.Buses.Where(b => b.LicensePlate == licensePlate);

        if (exceptBusId is not null)
        {
            var exceptId = exceptBusId.Value;
            query = query.Where(b => b.Id != exceptId);
        }

        return query.AnyAsync(cancellationToken);
    }

    /// <summary>
    /// So khớp với danh sách tên của enum thay vì dùng <c>Enum.TryParse</c> — cùng lý do
    /// RouteService làm với RouteStatus: TryParse chấp nhận cả chuỗi số, trái quy ước A3.
    /// Bỏ qua hoa/thường khi đọc, nhưng giá trị lưu xuống CSDL luôn là tên chuẩn của enum.
    /// </summary>
    private static bool TryParseBusStatus(string? value, out BusStatus status)
    {
        var trimmed = value?.Trim();

        foreach (var name in Enum.GetNames<BusStatus>())
        {
            if (string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                status = Enum.Parse<BusStatus>(name);
                return true;
            }
        }

        status = default;
        return false;
    }

    /// <summary>
    /// Danh sách mã hợp lệ lấy từ chính enum, không chép tay vào câu chữ — thêm hay bớt một
    /// trạng thái thì thông báo lỗi tự đúng theo, không có chỗ nào để quên sửa.
    /// </summary>
    private static string StatusMessage()
        => $"{StatusInvalidMessage}. Chấp nhận: {string.Join(", ", Enum.GetNames<BusStatus>())}";

    private static ServiceResult<BusResponse> InvalidField(string field, string message)
        => ServiceResult<BusResponse>.Invalid(
            message,
            new Dictionary<string, string[]> { [field] = [message] });

    private static BusResponse ToResponse(Bus bus) => new()
    {
        Id = bus.Id,
        LicensePlate = bus.LicensePlate,
        BusType = bus.BusType,
        Capacity = bus.Capacity,
        // Tên chuỗi của enum — đúng giá trị đang nằm trong cột Status
        // (HasConversion<string> ở AppDbContext.Trip.cs).
        Status = bus.Status.ToString(),
        CreatedAt = bus.CreatedAt,
        UpdatedAt = bus.UpdatedAt,
    };
}
