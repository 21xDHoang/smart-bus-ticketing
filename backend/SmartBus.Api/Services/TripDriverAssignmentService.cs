using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Trips;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="ITripDriverAssignmentService"/>. US 14 "Phân công điều xe" — Nguyễn Duy Kiên.
///
/// Đây là ĐIỂM GỌI mà <see cref="ITripConflictService"/> chờ từ lúc được viết ra: service đó lo
/// phép kiểm tra trùng lịch, còn việc quyết định chặn hay chỉ cảnh báo là của nơi gọi — ở đây là
/// CẢNH BÁO (trả trong <c>conflicts</c>, không chặn), cùng triết lý với việc
/// <c>PUT /routes/{routeId}/trips/{id}</c> cố ý không chặn trùng khung giờ.
///
/// Xác thực tài xế và lấy họ tên đều giao cho <see cref="IDriverService"/>, không tự viết lại truy
/// vấn "User mang vai trò Driver" (quy ước A8.4 — không có bảng Drivers riêng).
/// </summary>
public class TripDriverAssignmentService : ITripDriverAssignmentService
{
    private const string RouteNotFoundMessage = "Không tìm thấy tuyến đường";
    private const string TripNotFoundMessage = "Không tìm thấy chuyến xe";
    private const string TripNotAssignableMessage = "Chuyến đã hoàn thành hoặc đã huỷ, không thể phân công tài xế";
    private const string DriverNotActiveMessage = "Tài khoản tài xế đang bị khoá nên không thể phân công vào chuyến";
    private const string DuplicateTripIdsMessage = "Danh sách chuyến có chuyến bị lặp lại";
    private const string TooManyTripsMessage = "Một lần gán tối đa 200 chuyến";

    /// <summary>
    /// Trần số chuyến mỗi lần gán. Lấy đúng trần 200 chuyến đang hoạt động/ngày của một tuyến
    /// (<c>RouteTripsService.MaxTripsPerRoutePerDay</c>): một lô gán vượt quá số chuyến tối đa mà
    /// một tuyến có thể có trong một ngày là vô nghĩa, nên đây là chốt chặn tự nhiên thay vì một
    /// con số tuỳ ý. Cũng chặn luôn một request gõ nhầm kéo theo cả nghìn dòng.
    /// </summary>
    private const int MaxTripsPerAssignment = 200;

    private readonly AppDbContext _db;
    private readonly ITripConflictService _conflictService;
    private readonly IDriverService _driverService;

    public TripDriverAssignmentService(
        AppDbContext db,
        ITripConflictService conflictService,
        IDriverService driverService)
    {
        _db = db;
        _conflictService = conflictService;
        _driverService = driverService;
    }

    public async Task<ServiceResult<DriverAssignmentResponse>> AssignAsync(
        Guid routeId,
        AssignDriverToTripsRequest request,
        CancellationToken cancellationToken = default)
    {
        // ── Kiểm tra hình dạng lô. Lặp lại [MinLength]/[MaxLength] của DTO vì service còn có thể
        //    được gọi từ service khác — ở đó model binding không chạy, không có gì chặn hộ. ──
        if (request.TripIds.Count > MaxTripsPerAssignment)
        {
            return InvalidField<DriverAssignmentResponse>("tripIds", TooManyTripsMessage);
        }

        // Chặn hẳn thay vì âm thầm bỏ dòng lặp: cùng lối RouteStopService.ReorderAsync dùng
        // SetEquals — client gửi lên một lô có chuyến lặp là client hiểu sai dữ liệu của chính nó.
        if (request.TripIds.Distinct().Count() != request.TripIds.Count)
        {
            return InvalidField<DriverAssignmentResponse>("tripIds", DuplicateTripIdsMessage);
        }

        // ── Tuyến phải tồn tại. Đọc luôn mã/tên tuyến để dựng cảnh báo trùng lịch giữa các chuyến
        //    trong lô (ConflictingTripResponse cần routeCode/routeName). ──
        var route = await _db.Routes
            .AsNoTracking()
            .Where(r => r.Id == routeId)
            .Select(r => new { r.Id, r.Code, r.Name })
            .FirstOrDefaultAsync(cancellationToken);

        if (route is null)
        {
            return ServiceResult<DriverAssignmentResponse>.NotFound(RouteNotFoundMessage);
        }

        // ── Nạp các chuyến cần gán. Theo dõi (tracking) vì sẽ sửa DriverId ngay trên entity. ──
        var trips = await _db.Trips
            .Include(t => t.Bus)
            .Where(t => request.TripIds.Contains(t.Id))
            .ToListAsync(cancellationToken);

        // Thiếu id nào là có id không tồn tại. Gộp chung với nhánh "thuộc tuyến khác" bên dưới:
        // cả hai đều là "chuyến này không nằm trong tập được phép gán của tuyến này" → 404.
        if (trips.Count != request.TripIds.Count)
        {
            return ServiceResult<DriverAssignmentResponse>.NotFound(TripNotFoundMessage);
        }

        // routeId phải khớp — cùng lối GET/PUT/DELETE /routes/{routeId}/trips/{id}. Một chuyến của
        // tuyến khác là CẢ LÔ bị chặn, không âm thầm bỏ qua rồi gán phần còn lại.
        if (trips.Any(t => t.RouteId != routeId))
        {
            return ServiceResult<DriverAssignmentResponse>.NotFound(TripNotFoundMessage);
        }

        // Gán tài xế cho chuyến đã huỷ/đã chạy xong là vô nghĩa (không còn ai lái) — chặn cả lô,
        // cùng lối "Chuyến đã hoàn thành, không thể hủy" của RouteTripsService.
        if (trips.Any(t => t.Status is TripStatus.Completed or TripStatus.Cancelled))
        {
            return ServiceResult<DriverAssignmentResponse>.Conflict(TripNotAssignableMessage);
        }

        // ── Tài xế: IDriverService lọc sẵn "User mang vai trò Driver", nên tài khoản không phải
        //    tài xế (kể cả GUID không tồn tại) đều ra 404 với đúng câu của DriverService. ──
        var driverResult = await _driverService.GetByIdAsync(request.DriverId!.Value, cancellationToken);

        if (driverResult.Data is null)
        {
            // GetByIdAsync chỉ có hai kết cục: Ok kèm dữ liệu, hoặc NotFound kèm "Không tìm thấy
            // tài xế" — dùng lại nguyên câu đó thay vì chép lại literal.
            return ServiceResult<DriverAssignmentResponse>.NotFound(driverResult.Error!);
        }

        var driver = driverResult.Data;

        // Tài khoản đã khoá thì không lái được chuyến nào — cùng lối "Xe không trong trạng thái
        // khai thác nên không thể gán vào chuyến" của RouteTripsService.
        if (!driver.IsActive)
        {
            return ServiceResult<DriverAssignmentResponse>.Conflict(DriverNotActiveMessage);
        }

        // ── Cảnh báo trùng lịch, tính TRƯỚC khi sửa bất cứ dòng nào: vế "so với CSDL" phải nhìn
        //    thấy trạng thái thật đang lưu, không phải trạng thái sắp ghi. ──
        var conflicts = trips.ToDictionary(
            trip => trip.Id,
            _ => new Dictionary<Guid, ConflictingTripResponse>());

        foreach (var trip in trips)
        {
            var check = await _conflictService.CheckAsync(
                new TripConflictCheckRequest
                {
                    // Cố ý KHÔNG truyền BusId: endpoint này chỉ đổi tài xế. Soi thêm vế xe sẽ dội
                    // lại những lần trùng xe có sẵn (do PUT /routes/{routeId}/trips/{id} cố ý
                    // không kiểm tra) thành tiếng ồn không liên quan tới thao tác đang làm.
                    DriverId = driver.Id,
                    DepartureTime = AsUtcOffset(trip.DepartureTime),
                    ArrivalTime = trip.ArrivalTime is { } arrival ? AsUtcOffset(arrival) : null,
                    ExcludeTripId = trip.Id,
                },
                cancellationToken);

            if (!check.Success)
            {
                // Không chạm tới được với dữ liệu do API sinh ra (POST/PUT đều chặn giờ đến trước
                // giờ khởi hành) — chỉ một dòng Trips hỏng tay mới rơi vào đây. Trả lỗi thay vì
                // lặng lẽ bỏ qua cảnh báo, để dữ liệu hỏng nổi lên thành 400 chứ không im lặng.
                return ServiceResult<DriverAssignmentResponse>.Invalid(check.Error!, check.Errors!);
            }

            foreach (var conflicting in check.Data!.DriverConflicts)
            {
                conflicts[trip.Id][conflicting.Id] = conflicting;
            }
        }

        AddConflictsInsideBatch(trips, driver.Id, route.Code, route.Name, conflicts);

        // ── Gán. Chuyến đã đúng tài xế đó từ trước thì không đụng tới: giữ nguyên updatedAt, và
        //    gọi lại y hệt lần hai vì thế là idempotent (assignedCount về 0). ──
        var now = DateTime.UtcNow;
        var assignedCount = 0;

        foreach (var trip in trips)
        {
            if (trip.DriverId == driver.Id)
            {
                continue;
            }

            trip.DriverId = driver.Id;
            trip.UpdatedAt = now;
            assignedCount++;
        }

        if (assignedCount > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        var byId = trips.ToDictionary(trip => trip.Id);

        return ServiceResult<DriverAssignmentResponse>.Ok(new DriverAssignmentResponse
        {
            DriverId = driver.Id,
            DriverName = driver.FullName,
            AssignedCount = assignedCount,
            // Theo đúng thứ tự client gửi lên, không theo thứ tự CSDL trả về — client đối chiếu
            // được từng dòng với ô đã chọn trên màn hình.
            Items = request.TripIds
                .Select(id => ToItem(byId[id], driver.Id, driver.FullName, conflicts[id].Values))
                .ToList(),
        });
    }

    /// <summary>
    /// Trùng lịch GIỮA CÁC CHUYẾN TRONG CÙNG LÔ — nửa mà <see cref="ITripConflictService"/> không
    /// thấy được: nó so với các chuyến ĐÃ mang tài xế này trong CSDL, còn cả lô đang gán thì chưa.
    ///
    /// Phép so là ĐÚNG phép so mà <c>RouteTripsService.GenerateAsync</c> dùng cho lô chuyến vừa sinh
    /// (cùng luật, chỉ khác chỗ đứng): khung giờ một chuyến là [khởi hành, đến], chưa có giờ đến thì
    /// coi là một mốc — nên phải so thêm vế "cùng giờ khởi hành", vì hai khoảng rỗng không tự chồng
    /// lên nhau. Chuyến nối đuôi không tính là trùng.
    ///
    /// Lô tối đa 200 chuyến nên vòng lặp đôi ở đây là ~20k phép so — không đáng kể.
    /// </summary>
    private static void AddConflictsInsideBatch(
        List<Trip> trips,
        Guid driverId,
        string routeCode,
        string routeName,
        Dictionary<Guid, Dictionary<Guid, ConflictingTripResponse>> conflicts)
    {
        for (var i = 0; i < trips.Count; i++)
        {
            for (var j = i + 1; j < trips.Count; j++)
            {
                if (!Overlap(trips[i], trips[j]))
                {
                    continue;
                }

                conflicts[trips[i].Id][trips[j].Id] = ToConflictingTrip(trips[j], driverId, routeCode, routeName);
                conflicts[trips[j].Id][trips[i].Id] = ToConflictingTrip(trips[i], driverId, routeCode, routeName);
            }
        }
    }

    /// <summary>Hai chuyến có chồng khung giờ không — xem <see cref="AddConflictsInsideBatch"/>.</summary>
    private static bool Overlap(Trip a, Trip b)
    {
        var aEnd = a.ArrivalTime ?? a.DepartureTime;
        var bEnd = b.ArrivalTime ?? b.DepartureTime;

        return a.DepartureTime == b.DepartureTime
            || (b.DepartureTime < aEnd && bEnd > a.DepartureTime);
    }

    private static ConflictingTripResponse ToConflictingTrip(
        Trip trip,
        Guid driverId,
        string routeCode,
        string routeName) => new()
    {
        Id = trip.Id,
        RouteId = trip.RouteId,
        RouteCode = routeCode,
        RouteName = routeName,
        BusId = trip.BusId,
        // Cả lô cùng được gán cho một tài xế nên sau thao tác này chuyến trùng cũng mang tài xế đó
        // — ghi đúng giá trị mà người đọc cảnh báo cần thấy, không phải giá trị cũ trước khi gán.
        DriverId = driverId,
        DepartureTime = trip.DepartureTime,
        ArrivalTime = trip.ArrivalTime,
        Status = trip.Status.ToString(),
    };

    private static DriverAssignmentItemResponse ToItem(
        Trip trip,
        Guid driverId,
        string driverName,
        IEnumerable<ConflictingTripResponse> driverConflicts) => new()
    {
        Id = trip.Id,
        RouteId = trip.RouteId,
        BusId = trip.BusId,
        BusLicensePlate = trip.Bus?.LicensePlate ?? string.Empty,
        DriverId = driverId,
        DriverName = driverName,
        DepartureTime = trip.DepartureTime,
        ArrivalTime = trip.ArrivalTime,
        // Tên chuỗi của enum — đúng giá trị đang nằm trong cột Status
        // (HasConversion<string> ở AppDbContext.Trip.cs).
        Status = trip.Status.ToString(),
        UpdatedAt = trip.UpdatedAt,
        Conflicts = new TripConflictCheckResponse
        {
            // Endpoint này chỉ đổi tài xế nên vế xe luôn rỗng — xem chú thích ở chỗ gọi CheckAsync.
            BusConflicts = [],
            // Xếp theo giờ khởi hành rồi tới Id — cùng lối TripConflictService, để kết quả ổn định
            // giữa các lần gọi.
            DriverConflicts = driverConflicts
                .OrderBy(conflicting => conflicting.DepartureTime)
                .ThenBy(conflicting => conflicting.Id)
                .ToList(),
        },
    };

    /// <summary>
    /// Ép một mốc thời gian đọc từ cột <c>timestamptz</c> về <see cref="DateTimeOffset"/> offset 0.
    ///
    /// Không dùng <c>new DateTimeOffset(value)</c> trực tiếp: giá trị đọc lên có thể mang
    /// <see cref="DateTimeKind.Unspecified"/> (tuỳ provider), và lúc đó constructor sẽ hiểu nó là
    /// giờ MÁY CHỦ rồi cộng offset địa phương — lệch giờ và làm phép so trùng sai. Cột là
    /// <c>timestamptz</c> (quy ước A3) nên khẳng định thẳng Kind = Utc mới đúng nghĩa dữ liệu.
    /// </summary>
    private static DateTimeOffset AsUtcOffset(DateTime value)
        => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static ServiceResult<T> InvalidField<T>(string field, string message)
        => ServiceResult<T>.Invalid(
            message,
            new Dictionary<string, string[]> { [field] = [message] });
}
