using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Trips;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IRouteTripsService"/>. Story 13 "Lập lịch trình" — Trần Trung Hiếu.
///
/// Lịch trình định kỳ không tách bảng Schedule (quy ước A8.3): "ngày áp dụng + giờ khởi hành +
/// tần suất" của lịch trình chính là ba tham số của <see cref="GenerateAsync"/> — mốc bắt đầu
/// mang ngày áp dụng, mốc kết thúc chặn trên của dải giờ, tần suất là khoảng cách giữa hai
/// chuyến liên tiếp tính bằng phút.
/// </summary>
public class RouteTripsService : IRouteTripsService
{
    private const string RouteNotFoundMessage = "Không tìm thấy tuyến đường";
    private const string TripNotFoundMessage = "Không tìm thấy chuyến xe";
    private const string BusNotFoundMessage = "Không tìm thấy xe buýt";
    private const string BusNotActiveMessage = "Xe không trong trạng thái khai thác nên không thể gán vào chuyến";
    private const string ArrivalBeforeDepartureMessage = "Giờ đến phải sau giờ khởi hành";
    private const string DateRangeMessage = "Thời điểm kết thúc phải sau thời điểm bắt đầu";
    private const string TooManyTripsMessage = "Khoảng thời gian quá dài, một lần sinh tối đa 500 chuyến. Thu hẹp khoảng hoặc tăng tần suất";
    private const string TripCompletedMessage = "Chuyến đã hoàn thành, không thể hủy";
    private const string StatusInvalidMessage = "Trạng thái không hợp lệ";

    /// <summary>
    /// Trần số chuyến mỗi lần gọi generate. Chọn 500 vì một ngày chạy thật theo tần suất thường
    /// (10–30 phút) chỉ sinh vài chục chuyến; lịch trình nhiều ngày thì tách thành nhiều lần
    /// gọi, mỗi lần một ngày. Trần này chặn một khoảng thời gian gõ nhầm sinh hàng vạn dòng Trips.
    /// </summary>
    private const int MaxGenerateTrips = 500;

    /// <summary>Trùng con số của RouteService và AdminUserService — các màn hình danh sách cùng cỡ trang.</summary>
    private const int DefaultPageSize = 10;

    private readonly AppDbContext _db;

    public RouteTripsService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<TripListResponse>> ListAsync(
        Guid routeId,
        ListTripsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await RouteExistsAsync(routeId, cancellationToken))
        {
            return ServiceResult<TripListResponse>.NotFound(RouteNotFoundMessage);
        }

        if (request.From is { } from && request.To is { } to && to < from)
        {
            return InvalidField<TripListResponse>("to", DateRangeMessage);
        }

        var page = request.Page ?? 1;
        var pageSize = request.PageSize ?? DefaultPageSize;

        var fromUtc = request.From?.UtcDateTime;
        var toUtc = request.To?.UtcDateTime;

        var query = _db.Trips.AsNoTracking().Where(t => t.RouteId == routeId);

        if (fromUtc is not null)
        {
            query = query.Where(t => t.DepartureTime >= fromUtc);
        }

        if (toUtc is not null)
        {
            query = query.Where(t => t.DepartureTime <= toUtc);
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!TryParseTripStatus(request.Status, out var status))
            {
                // Mã trạng thái không khớp: không báo lỗi mà trả danh sách rỗng — cùng lối
                // RouteService lọc trạng thái bằng chuỗi so khớp trực tiếp.
                return ServiceResult<TripListResponse>.Ok(new TripListResponse
                {
                    Items = [],
                    Total = 0,
                    Page = page,
                    PageSize = pageSize,
                });
            }

            query = query.Where(t => t.Status == status);
        }

        var total = await query.CountAsync(cancellationToken);

        // Nhân bằng long rồi mới ép về int — cùng chiêu AuditLogQueryService: (page - 1) * pageSize
        // tính bằng int sẽ tràn thành số âm khi page tiến gần int.MaxValue.
        var skip = (long)(page - 1) * pageSize;

        var trips = await query
            .Include(t => t.Bus)
            // Lịch trình đọc theo thứ tự chạy chứ không theo thời điểm tạo — khác RouteService
            // xếp theo CreatedAt giảm dần: màn hình lập lịch trình đọc như một cuốn thời gian biểu.
            .OrderBy(t => t.DepartureTime)
            // Chốt thêm theo Id: các chuyến sinh hàng loạt cùng giây có thể trùng giờ khởi hành
            // nếu gọi generate hai lần trùng dải giờ, thiếu khoá phụ thì phân trang trùng/thiếu dòng.
            .ThenBy(t => t.Id)
            .Skip((int)Math.Min(skip, int.MaxValue))
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return ServiceResult<TripListResponse>.Ok(new TripListResponse
        {
            Items = trips.Select(ToResponse).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize,
        });
    }

    public async Task<ServiceResult<TripResponse>> GetByIdAsync(
        Guid routeId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var trip = await _db.Trips
            .AsNoTracking()
            .Include(t => t.Bus)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        // Chuyến của tuyến khác cũng là 404 — cùng lối FaresController: routeId phải khớp.
        return trip is null || trip.RouteId != routeId
            ? ServiceResult<TripResponse>.NotFound(TripNotFoundMessage)
            : ServiceResult<TripResponse>.Ok(ToResponse(trip));
    }

    public async Task<ServiceResult<TripResponse>> CreateAsync(
        Guid routeId,
        CreateTripRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await RouteExistsAsync(routeId, cancellationToken))
        {
            return ServiceResult<TripResponse>.NotFound(RouteNotFoundMessage);
        }

        var bus = await _db.Buses.FirstOrDefaultAsync(b => b.Id == request.BusId!.Value, cancellationToken);

        if (bus is null || bus.Status != BusStatus.Active)
        {
            return BusMissingOrInactive<TripResponse>(bus);
        }

        var departureUtc = request.DepartureTime!.Value.UtcDateTime;
        var arrivalUtc = request.ArrivalTime?.UtcDateTime;

        if (arrivalUtc is { } arrival && arrival < departureUtc)
        {
            return InvalidField<TripResponse>("arrivalTime", ArrivalBeforeDepartureMessage);
        }

        var trip = new Trip
        {
            RouteId = routeId,
            BusId = bus.Id,
            // Gán thẳng navigation để ToResponse có biển số mà không phải truy vấn lại.
            Bus = bus,
            DepartureTime = departureUtc,
            ArrivalTime = arrivalUtc,
        };

        _db.Trips.Add(trip);
        await _db.SaveChangesAsync(cancellationToken);

        return ServiceResult<TripResponse>.Ok(ToResponse(trip));
    }

    public async Task<ServiceResult<TripResponse>> UpdateAsync(
        Guid routeId,
        Guid id,
        UpdateTripRequest request,
        CancellationToken cancellationToken = default)
    {
        var trip = await _db.Trips
            .Include(t => t.Bus)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (trip is null || trip.RouteId != routeId)
        {
            return ServiceResult<TripResponse>.NotFound(TripNotFoundMessage);
        }

        Bus? bus = trip.Bus;

        if (trip.BusId != request.BusId!.Value)
        {
            // Chỉ kiểm tra trạng thái xe khi ĐỔI xe: xe đang chạy chuyến này mà chuyển sang bảo
            // dưỡng sau khi sinh chuyến thì không nên chặn sửa giờ — chuyến đã tồn tại rồi.
            bus = await _db.Buses.FirstOrDefaultAsync(b => b.Id == request.BusId.Value, cancellationToken);

            if (bus is null || bus.Status != BusStatus.Active)
            {
                return BusMissingOrInactive<TripResponse>(bus);
            }
        }

        var departureUtc = request.DepartureTime!.Value.UtcDateTime;
        var arrivalUtc = request.ArrivalTime?.UtcDateTime;

        if (arrivalUtc is { } arrival && arrival < departureUtc)
        {
            return InvalidField<TripResponse>("arrivalTime", ArrivalBeforeDepartureMessage);
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!TryParseTripStatus(request.Status, out var status))
            {
                return InvalidField<TripResponse>("status", StatusMessage());
            }

            trip.Status = status;
        }

        trip.BusId = bus!.Id;
        trip.Bus = bus;
        trip.DepartureTime = departureUtc;
        // Bỏ trống = gán null chứ không phải giữ nguyên: đây là thao tác sửa toàn phần như PUT,
        // không phải PATCH — cùng lối PUT /routes/{id} ghi đè đủ mọi trường bắt buộc.
        trip.ArrivalTime = arrivalUtc;
        trip.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return ServiceResult<TripResponse>.Ok(ToResponse(trip));
    }

    public async Task<ServiceResult<TripResponse>> DeleteAsync(
        Guid routeId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var trip = await _db.Trips
            .Include(t => t.Bus)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (trip is null || trip.RouteId != routeId)
        {
            return ServiceResult<TripResponse>.NotFound(TripNotFoundMessage);
        }

        // Chuyến đã chạy xong là dữ liệu quá khứ — huỷ nó là viết lại lịch sử, chặn hẳn.
        if (trip.Status == TripStatus.Completed)
        {
            return ServiceResult<TripResponse>.Conflict(TripCompletedMessage);
        }

        // Huỷ chuyến = chuyển về Cancelled, giữ bản ghi vì vé đã bán vẫn tham chiếu tới (A4 cấm
        // thêm cột IsDeleted, dùng đúng cột trạng thái sẵn có). Chuyến đã Cancelled gọi lại thì
        // trả về luôn — idempotent, cùng lối RouteService.DeleteAsync.
        if (trip.Status != TripStatus.Cancelled)
        {
            trip.Status = TripStatus.Cancelled;
            trip.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return ServiceResult<TripResponse>.Ok(ToResponse(trip));
    }

    public async Task<ServiceResult<GenerateTripsResponse>> GenerateAsync(
        Guid routeId,
        GenerateTripsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await RouteExistsAsync(routeId, cancellationToken))
        {
            return ServiceResult<GenerateTripsResponse>.NotFound(RouteNotFoundMessage);
        }

        var bus = await _db.Buses.FirstOrDefaultAsync(b => b.Id == request.BusId!.Value, cancellationToken);

        if (bus is null || bus.Status != BusStatus.Active)
        {
            return BusMissingOrInactive<GenerateTripsResponse>(bus);
        }

        var start = request.StartTime!.Value;
        var end = request.EndTime!.Value;

        if (end <= start)
        {
            return InvalidField<GenerateTripsResponse>("endTime", DateRangeMessage);
        }

        // Chuyến đầu xuất phát đúng mốc bắt đầu, mỗi bước tần suất thêm một chuyến, chuyến cuối
        // không vượt quá mốc kết thúc: count = floor((end - start) / tần suất) + 1.
        var count = (long)(end - start).TotalMinutes / request.FrequencyMinutes + 1;

        if (count > MaxGenerateTrips)
        {
            return InvalidField<GenerateTripsResponse>("endTime", TooManyTripsMessage);
        }

        var startUtc = start.UtcDateTime;
        var trips = new List<Trip>((int)count);

        for (var k = 0L; k < count; k++)
        {
            trips.Add(new Trip
            {
                RouteId = routeId,
                BusId = bus.Id,
                Bus = bus,
                DepartureTime = startUtc.AddMinutes(k * request.FrequencyMinutes),
            });
        }

        _db.Trips.AddRange(trips);
        await _db.SaveChangesAsync(cancellationToken);

        return ServiceResult<GenerateTripsResponse>.Ok(new GenerateTripsResponse
        {
            Items = trips.Select(ToResponse).ToList(),
            Total = trips.Count,
        });
    }

    private Task<bool> RouteExistsAsync(Guid routeId, CancellationToken cancellationToken)
        => _db.Routes.AnyAsync(r => r.Id == routeId, cancellationToken);

    /// <summary>
    /// Hai nhánh "xe không tồn tại" và "xe không Active" khác nhau ở mã lỗi (404 so với 409) —
    /// nhận chính kết quả tra cứu để chọn nhánh, không tra cứu lần thứ hai.
    /// </summary>
    private static ServiceResult<T> BusMissingOrInactive<T>(Bus? bus)
        => bus is null
            ? ServiceResult<T>.NotFound(BusNotFoundMessage)
            : ServiceResult<T>.Conflict(BusNotActiveMessage);

    /// <summary>
    /// So khớp với danh sách tên của enum thay vì dùng <c>Enum.TryParse</c> — cùng lý do
    /// RouteService làm với RouteStatus: TryParse chấp nhận cả chuỗi số, trái quy ước A3.
    /// Bỏ qua hoa/thường khi đọc, nhưng giá trị lưu xuống CSDL luôn là tên chuẩn của enum.
    /// </summary>
    private static bool TryParseTripStatus(string? value, out TripStatus status)
    {
        var trimmed = value?.Trim();

        foreach (var name in Enum.GetNames<TripStatus>())
        {
            if (string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                status = Enum.Parse<TripStatus>(name);
                return true;
            }
        }

        status = default;
        return false;
    }

    /// <summary>
    /// Danh sách mã hợp lệ lấy từ chính enum, không chép tay vào câu chữ — thêm hay bớt một
    /// trạng thái thì thông báo lỗi tự đúng theo.
    /// </summary>
    private static string StatusMessage()
        => $"{StatusInvalidMessage}. Chấp nhận: {string.Join(", ", Enum.GetNames<TripStatus>())}";

    private static ServiceResult<T> InvalidField<T>(string field, string message)
        => ServiceResult<T>.Invalid(
            message,
            new Dictionary<string, string[]> { [field] = [message] });

    private static TripResponse ToResponse(Trip trip) => new()
    {
        Id = trip.Id,
        RouteId = trip.RouteId,
        BusId = trip.BusId,
        BusLicensePlate = trip.Bus?.LicensePlate ?? string.Empty,
        DepartureTime = trip.DepartureTime,
        ArrivalTime = trip.ArrivalTime,
        // Tên chuỗi của enum — đúng giá trị đang nằm trong cột Status
        // (HasConversion<string> ở AppDbContext.Trip.cs).
        Status = trip.Status.ToString(),
        CurrentStopId = trip.CurrentStopId,
        CurrentLat = trip.CurrentLat,
        CurrentLng = trip.CurrentLng,
        PositionUpdatedAt = trip.PositionUpdatedAt,
        CreatedAt = trip.CreatedAt,
        UpdatedAt = trip.UpdatedAt,
    };
}
