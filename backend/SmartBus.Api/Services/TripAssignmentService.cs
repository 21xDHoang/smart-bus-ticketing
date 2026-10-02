using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Trips;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="ITripAssignmentService"/> — hợp đồng đầy đủ ở mục
/// "PATCH /trips/{id}/assignment" của docs/api-contract.md.
///
/// Bám khuôn <see cref="RouteTripsService"/>: cùng câu thông báo cho xe (404 không tồn tại /
/// 409 không khai thác), cùng lối kiểm tra trạng thái trước khi sửa, cùng cách đọc enum ra
/// chuỗi ở bộ nhớ. Phép kiểm tra trùng lịch gọi thẳng <see cref="ITripConflictService"/> —
/// "cùng một hàm kiểm tra, chỉ khác điểm gọi" như ghi chú cuối mục "Hai kiểm tra khi tạo
/// lịch trình".
/// </summary>
public class TripAssignmentService : ITripAssignmentService
{
    private const string TripNotFoundMessage = "Không tìm thấy chuyến xe";

    /// <summary>Ba câu dưới trùng nguyên văn <see cref="RouteTripsService"/> — cùng tình huống,
    /// cùng thông báo, dù đi từ màn hình lập lịch trình hay màn hình sự cố.</summary>
    private const string BusNotFoundMessage = "Không tìm thấy xe buýt";
    private const string BusNotActiveMessage = "Xe không trong trạng thái khai thác nên không thể gán vào chuyến";
    private const string DriverNotFoundMessage = "Không tìm thấy tài xế";

    /// <summary>Song song với <see cref="BusNotActiveMessage"/>: cả hai là "tài nguyên không
    /// dùng được nữa", chỉ khác mã lỗi của xe là 409/404 còn tài xế luôn 409.</summary>
    private const string DriverLockedMessage = "Tài khoản tài xế đang bị khóa nên không thể gán vào chuyến";

    private const string TripCancelledMessage = "Chuyến đã hủy, không thể đổi xe hoặc tài xế";
    private const string TripCompletedMessage = "Chuyến đã hoàn thành, không thể đổi xe hoặc tài xế";
    private const string NoResourceMessage = "Cần cung cấp xe mới hoặc tài xế mới";

    private readonly AppDbContext _db;
    private readonly ITripConflictService _conflictService;

    public TripAssignmentService(AppDbContext db, ITripConflictService conflictService)
    {
        _db = db;
        _conflictService = conflictService;
    }

    public async Task<ServiceResult<TripAssignmentResponse>> ReassignAsync(
        Guid tripId,
        ReassignTripRequest request,
        CancellationToken cancellationToken = default)
    {
        // Không truyền tài nguyên nào thì không có gì để đổi — chặn trước cả bước tra CSDL:
        // request sai hình dạng thì không phụ thuộc chuyến có tồn tại hay không.
        if (request.BusId is null && request.DriverId is null)
        {
            return ServiceResult<TripAssignmentResponse>.Invalid(
                NoResourceMessage,
                new Dictionary<string, string[]>
                {
                    // Gắn lỗi vào cả hai trường: form gửi rỗng thì cả hai ô đều đang thiếu.
                    ["busId"] = [NoResourceMessage],
                    ["driverId"] = [NoResourceMessage],
                });
        }

        var trip = await _db.Trips
            .Include(t => t.Bus)
            .Include(t => t.Driver)
            .FirstOrDefaultAsync(t => t.Id == tripId, cancellationToken);

        if (trip is null)
        {
            return ServiceResult<TripAssignmentResponse>.NotFound(TripNotFoundMessage);
        }

        // Chỉ chuyến còn chiếm chỗ trên thời gian biểu mới đổi phân công được: chuyến đã hủy
        // hoặc đã chạy xong mà thay xe/tài xế là viết lại lịch sử — cùng lối chặn hủy chuyến
        // đã Completed của RouteTripsService.
        if (trip.Status == TripStatus.Cancelled)
        {
            return ServiceResult<TripAssignmentResponse>.Conflict(TripCancelledMessage);
        }

        if (trip.Status == TripStatus.Completed)
        {
            return ServiceResult<TripAssignmentResponse>.Conflict(TripCompletedMessage);
        }

        var newBusId = request.BusId;
        var newDriverId = request.DriverId;

        // Chỉ tính là "đổi" khi giá trị MỚI khác giá trị hiện tại. Truyền đúng giá trị hiện tại
        // = không có gì đổi (idempotent).
        var busChanged = newBusId is not null && newBusId != trip.BusId;
        var driverChanged = newDriverId is not null && newDriverId != trip.DriverId;

        if (!busChanged && !driverChanged)
        {
            // Không thay đổi gì: trả nguyên trạng, không đụng UpdatedAt, không tốn một vòng
            // kiểm tra trùng lịch — thao tác không tạo ra thay đổi nào để mà cảnh báo.
            return ServiceResult<TripAssignmentResponse>.Ok(
                ToResponse(trip, new TripConflictCheckResponse()));
        }

        Bus? bus = trip.Bus;

        if (busChanged)
        {
            bus = await _db.Buses.FirstOrDefaultAsync(b => b.Id == newBusId!.Value, cancellationToken);

            if (bus is null || bus.Status != BusStatus.Active)
            {
                return BusMissingOrInactive(bus);
            }
        }

        User? driver = trip.Driver;

        if (driverChanged)
        {
            driver = await DriverUsers()
                .FirstOrDefaultAsync(u => u.Id == newDriverId!.Value, cancellationToken);

            if (driver is null)
            {
                return ServiceResult<TripAssignmentResponse>.NotFound(DriverNotFoundMessage);
            }

            // Tài khoản bị khóa không đăng nhập được (JwtMiddleware đọc lại IsActive ở mọi
            // request) nên gán vào chuyến cũng vô nghĩa — cùng lối chặn xe không Active.
            if (!driver.IsActive)
            {
                return ServiceResult<TripAssignmentResponse>.Conflict(DriverLockedMessage);
            }
        }

        // ── Kiểm tra trùng lịch điều xe — CẢNH BÁO, KHÔNG CHẶN (khác luồng tạo lịch trình):
        //    chuyến đang chạy cần thay xe/tài xế ngay, người điều hành được phép cố ý chấp nhận
        //    trùng (ví dụ đổi xe giữa chừng khi chuyến cũ sắp về bến). ──
        var conflicts = await _conflictService.CheckAsync(
            new TripConflictCheckRequest
            {
                // Chỉ soi tài nguyên ĐƯỢC ĐỔI: đổi mỗi tài xế thì vế xe hiện tại không đem ra
                // soi — dữ liệu cũ vướng lịch xe (PUT cố ý không kiểm trùng) không được chặn
                // việc đổi tài xế của chính chuyến này. Phép kiểm tra trả lời đúng một câu:
                // "thay đổi này có tạo xung đột MỚI không".
                BusId = busChanged ? newBusId : null,
                DriverId = driverChanged ? newDriverId : null,
                DepartureTime = ToUtcOffset(trip.DepartureTime),
                ArrivalTime = trip.ArrivalTime is { } arrival ? ToUtcOffset(arrival) : null,
                // Chuyến đang đổi không tự báo trùng chính nó.
                ExcludeTripId = trip.Id,
            },
            cancellationToken);

        if (!conflicts.Success)
        {
            // Chỉ xảy ra khi dữ liệu chuyến đã hỏng sẵn (giờ đến trước giờ khởi hành) — giữ
            // nguyên câu lỗi của phép kiểm tra thay vì bịa câu mới. Invalid luôn kèm errors.
            return ServiceResult<TripAssignmentResponse>.Invalid(conflicts.Error!, conflicts.Errors!);
        }

        if (busChanged)
        {
            trip.BusId = bus!.Id;
            // Gán thẳng navigation để response có biển số mà không phải truy vấn lại —
            // cùng lối RouteTripsService.
            trip.Bus = bus;
        }

        if (driverChanged)
        {
            trip.DriverId = driver!.Id;
            trip.Driver = driver;
        }

        trip.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return ServiceResult<TripAssignmentResponse>.Ok(ToResponse(trip, conflicts.Data!));
    }

    /// <summary>
    /// Truy vấn tài khoản MANG vai trò Driver: vai trò chính hoặc một dòng trong bảng nối
    /// UserRoles — cùng phép lọc của <see cref="DriverService"/> (chỉ xét RoleId là bỏ sót tài
    /// khoản nhiều vai trò). Nhờ vậy "tài xế không tồn tại" và "tài khoản không phải tài xế"
    /// là CÙNG một câu 404, đúng hành vi các endpoint /drivers.
    /// </summary>
    private IQueryable<User> DriverUsers()
        => _db.Users.Where(u =>
            u.RoleId == RoleIds.Driver || u.UserRoles.Any(ur => ur.RoleId == RoleIds.Driver));

    /// <summary>
    /// Cột thời gian của chuyến là DateTime UTC (<c>timestamptz</c>) còn request kiểm tra trùng
    /// lịch dùng DateTimeOffset — ép Kind=Utc trước khi bọc: để nguyên Kind Unspecified thì
    /// DateTimeOffset hiểu theo múi giờ máy chủ và lệch giờ (InMemory đọc lại trả Kind
    /// Unspecified).
    /// </summary>
    private static DateTimeOffset ToUtcOffset(DateTime value)
        => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    /// <summary>
    /// Hai nhánh "xe không tồn tại" (404) và "xe không Active" (409) khác nhau ở mã lỗi — nhận
    /// chính kết quả tra cứu để chọn nhánh, không tra cứu lần thứ hai. Cùng khuôn
    /// <see cref="RouteTripsService"/>.
    /// </summary>
    private static ServiceResult<TripAssignmentResponse> BusMissingOrInactive(Bus? bus)
        => bus is null
            ? ServiceResult<TripAssignmentResponse>.NotFound(BusNotFoundMessage)
            : ServiceResult<TripAssignmentResponse>.Conflict(BusNotActiveMessage);

    private static TripAssignmentResponse ToResponse(Trip trip, TripConflictCheckResponse conflicts) => new()
    {
        Id = trip.Id,
        RouteId = trip.RouteId,
        BusId = trip.BusId,
        // A5 đặt Restrict cho khoá ngoại nên xe của chuyến không thể bị xoá cứng — nhánh null
        // chỉ là lưới an toàn, cùng lối RouteTripsService.
        BusLicensePlate = trip.Bus?.LicensePlate ?? string.Empty,
        DriverId = trip.DriverId,
        DriverName = trip.Driver?.FullName,
        DepartureTime = trip.DepartureTime,
        ArrivalTime = trip.ArrivalTime,
        // Tên chuỗi của enum — đúng giá trị đang nằm trong cột Status
        // (HasConversion<string> ở AppDbContext.Trip.cs).
        Status = trip.Status.ToString(),
        UpdatedAt = trip.UpdatedAt,
        Conflicts = conflicts,
    };
}
