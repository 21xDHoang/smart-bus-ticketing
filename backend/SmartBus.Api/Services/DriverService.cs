using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Drivers;
using SmartBus.Api.Dtos.Trips;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IDriverService"/>. Story 14 — Trần Trung Hiếu.
/// Bám sát khuôn <see cref="BusService"/> và <see cref="AdminUserService"/>: cùng cách lọc,
/// cùng cách chặn trùng khoá nghiệp vụ, cùng cách xoá mềm. Phần ca làm việc bám sát
/// <see cref="RouteTripsService.ListAsync"/>.
///
/// Tài xế là User mang vai trò Driver (quy ước A8.4): mọi truy vấn đều lọc vai trò Driver
/// ngay từ đầu nên không endpoint nào ở đây có thể chạm vào tài khoản không phải tài xế.
/// </summary>
public class DriverService : IDriverService
{
    private const string DriverNotFoundMessage = "Không tìm thấy tài xế";

    /// <summary>Dùng chung câu với <see cref="AuthService"/> và <see cref="AdminUserService"/> để
    /// người dùng nhận một thông báo duy nhất dù tạo tài khoản từ màn hình nào.</summary>
    private const string PhoneExists = "Số điện thoại đã được đăng ký";

    private const string EmailExists = "Email đã được sử dụng";

    private const string CannotLockSelf = "Không thể tự khóa tài khoản của chính mình";

    private const string DateRangeMessage = "Thời điểm kết thúc phải sau thời điểm bắt đầu";

    /// <summary>Trùng con số của BusService và AdminUserService — các màn hình danh sách cùng cỡ trang.</summary>
    private const int DefaultPageSize = 10;

    private readonly AppDbContext _db;

    public DriverService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<DriverListResponse>> ListAsync(
        ListDriversRequest request,
        CancellationToken cancellationToken = default)
    {
        var page = request.Page ?? 1;
        var pageSize = request.PageSize ?? DefaultPageSize;

        var query = DriverUsers(tracking: false);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            // So khớp không phân biệt hoa thường bằng ToLower() thay vì EF.Functions.ILike:
            // ILike là hàm riêng của Npgsql, dùng nó thì test chạy trên provider InMemory sẽ đổ.
            var keyword = request.Search.Trim().ToLowerInvariant();

            query = query.Where(u =>
                u.FullName.ToLower().Contains(keyword) ||
                u.PhoneNumber.Contains(keyword) ||
                (u.Email != null && u.Email.ToLower().Contains(keyword)));
        }

        if (request.IsActive is not null)
        {
            var isActive = request.IsActive.Value;
            query = query.Where(u => u.IsActive == isActive);
        }

        var total = await query.CountAsync(cancellationToken);

        // Nhân bằng long rồi mới ép về int — cùng chiêu RouteTripsService: (page - 1) * pageSize
        // tính bằng int sẽ tràn thành số âm khi page tiến gần int.MaxValue.
        var skip = (long)(page - 1) * pageSize;

        var drivers = await query
            .OrderByDescending(u => u.CreatedAt)
            // Chốt thêm theo Id: nhiều tài khoản tạo cùng lúc có thể trùng CreatedAt, thiếu khoá
            // phụ thì thứ tự giữa các trang không ổn định và phân trang bị trùng/thiếu dòng.
            .ThenBy(u => u.Id)
            .Skip((int)Math.Min(skip, int.MaxValue))
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return ServiceResult<DriverListResponse>.Ok(new DriverListResponse
        {
            Items = drivers.Select(ToResponse).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize,
        });
    }

    public async Task<ServiceResult<DriverResponse>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var user = await DriverUsers(tracking: false)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

        return user is null
            ? ServiceResult<DriverResponse>.NotFound(DriverNotFoundMessage)
            : ServiceResult<DriverResponse>.Ok(ToResponse(user));
    }

    public async Task<ServiceResult<DriverResponse>> CreateAsync(
        CreateDriverRequest request,
        CancellationToken cancellationToken = default)
    {
        var phoneNumber = request.PhoneNumber.Trim();
        var email = NormalizeEmail(request.Email);

        // Kiểm tra trùng TRƯỚC khi băm mật khẩu (BCrypt tốn CPU) — request chắc chắn hỏng
        // thì không nên tiêu tốn tài nguyên. Cùng lối AdminUserService.
        if (await _db.Users.AnyAsync(u => u.PhoneNumber == phoneNumber, cancellationToken))
        {
            return InvalidField<DriverResponse>("phoneNumber", PhoneExists);
        }

        if (email is not null && await EmailTakenAsync(email, exceptUserId: null, cancellationToken))
        {
            return InvalidField<DriverResponse>("email", EmailExists);
        }

        // RoleIds.Driver là GUID cố định đã seed từ migration Sprint 1 — xem RoleIds.cs.
        // Không cần tra bảng Roles như AdminUserService: bên kia nhận mã vai trò từ client
        // nên phải kiểm tra, ở đây vai trò là hằng số không thể sai.
        var user = new User
        {
            FullName = request.FullName.Trim(),
            PhoneNumber = phoneNumber,
            Email = email,
            PasswordHash = PasswordService.HashPassword(request.Password),
            IsActive = true,
            RoleId = RoleIds.Driver,
            // Ghi luôn một dòng vào bảng nối, giống AuthService khi đăng ký: vai trò chính
            // phải luôn nằm trong UserRoles, nếu không màn hình sửa vai trò sẽ hiện thiếu.
            UserRoles = [new UserRole { RoleId = RoleIds.Driver }],
        };

        _db.Users.Add(user);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Hai request cùng SĐT chạy song song lọt qua bước kiểm tra ở trên —
            // ràng buộc unique trong CSDL là lớp bảo vệ cuối cùng.
            return InvalidField<DriverResponse>("phoneNumber", PhoneExists);
        }

        return ServiceResult<DriverResponse>.Ok(ToResponse(user));
    }

    public async Task<ServiceResult<DriverResponse>> UpdateAsync(
        Guid id,
        UpdateDriverRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await DriverUsers(tracking: true)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return ServiceResult<DriverResponse>.NotFound(DriverNotFoundMessage);
        }

        var phoneNumber = request.PhoneNumber.Trim();
        var email = NormalizeEmail(request.Email);

        if (phoneNumber != user.PhoneNumber &&
            await _db.Users.AnyAsync(u => u.Id != id && u.PhoneNumber == phoneNumber, cancellationToken))
        {
            return InvalidField<DriverResponse>("phoneNumber", PhoneExists);
        }

        if (email is not null && await EmailTakenAsync(email, exceptUserId: id, cancellationToken))
        {
            return InvalidField<DriverResponse>("email", EmailExists);
        }

        user.FullName = request.FullName.Trim();
        user.PhoneNumber = phoneNumber;
        user.Email = email;
        user.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return InvalidField<DriverResponse>("phoneNumber", PhoneExists);
        }

        return ServiceResult<DriverResponse>.Ok(ToResponse(user));
    }

    public async Task<ServiceResult<DriverResponse>> DeleteAsync(
        Guid id,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var user = await DriverUsers(tracking: true)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return ServiceResult<DriverResponse>.NotFound(DriverNotFoundMessage);
        }

        // Cùng chốt an toàn của AdminUserService: JwtMiddleware đọc lại IsActive ở MỌI request
        // nên tự khóa xong là không còn đường nào mở lại trong hệ thống.
        if (id == currentUserId)
        {
            return ServiceResult<DriverResponse>.Conflict(CannotLockSelf);
        }

        // Xoá mềm = khóa tài khoản (quy ước A4: cấm thêm cột IsDeleted). Tài xế đã khóa gọi lại
        // thì trả về luôn — idempotent, cùng lối AdminUserService.SetStatusAsync.
        if (user.IsActive)
        {
            user.IsActive = false;
            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return ServiceResult<DriverResponse>.Ok(ToResponse(user));
    }

    public async Task<ServiceResult<DriverTripListResponse>> ListTripsAsync(
        Guid driverId,
        ListTripsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await DriverUsers(tracking: false).AnyAsync(u => u.Id == driverId, cancellationToken))
        {
            return ServiceResult<DriverTripListResponse>.NotFound(DriverNotFoundMessage);
        }

        if (request.From is { } from && request.To is { } to && to < from)
        {
            return InvalidField<DriverTripListResponse>("to", DateRangeMessage);
        }

        var page = request.Page ?? 1;
        var pageSize = request.PageSize ?? DefaultPageSize;

        var fromUtc = request.From?.UtcDateTime;
        var toUtc = request.To?.UtcDateTime;

        var query = _db.Trips
            .AsNoTracking()
            .Where(t => t.DriverId == driverId);

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
                // RouteTripsService lọc trạng thái chuyến.
                return ServiceResult<DriverTripListResponse>.Ok(new DriverTripListResponse
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

        var skip = (long)(page - 1) * pageSize;

        var trips = await query
            .Include(t => t.Route)
            .Include(t => t.Bus)
            // Ca làm việc đọc theo thứ tự chạy chứ không theo thời điểm tạo — màn hình đọc
            // như thời gian biểu của tài xế, cùng lối RouteTripsService.
            .OrderBy(t => t.DepartureTime)
            // Chốt thêm theo Id: hai chuyến trùng giờ khởi hành thì phân trang mới ổn định.
            .ThenBy(t => t.Id)
            .Skip((int)Math.Min(skip, int.MaxValue))
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return ServiceResult<DriverTripListResponse>.Ok(new DriverTripListResponse
        {
            Items = trips.Select(ToTripResponse).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize,
        });
    }

    /// <summary>
    /// Truy vấn tài khoản MANG vai trò Driver: vai trò chính hoặc một dòng trong bảng nối
    /// UserRoles. Chỉ xét RoleId là bỏ sót tài khoản có nhiều vai trò (ví dụ [Driver, Passenger])
    /// mà PUT /admin/users/{id}/roles vẫn có thể tạo ra.
    /// </summary>
    private IQueryable<User> DriverUsers(bool tracking)
    {
        var query = _db.Users.AsQueryable();

        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return query.Where(u =>
            u.RoleId == RoleIds.Driver || u.UserRoles.Any(ur => ur.RoleId == RoleIds.Driver));
    }

    /// <summary>Email là khoá nghiệp vụ duy nhất (khi có) — có ràng buộc unique ở CSDL.</summary>
    private Task<bool> EmailTakenAsync(string email, Guid? exceptUserId, CancellationToken cancellationToken)
    {
        var query = _db.Users.Where(u => u.Email == email);

        if (exceptUserId is not null)
        {
            var exceptId = exceptUserId.Value;
            query = query.Where(u => u.Id != exceptId);
        }

        return query.AnyAsync(cancellationToken);
    }

    private static string? NormalizeEmail(string? email)
    {
        var trimmed = email?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// So khớp với danh sách tên của enum thay vì dùng <c>Enum.TryParse</c> — cùng lý do
    /// RouteTripsService làm: TryParse chấp nhận cả chuỗi số, trái quy ước A3.
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

    private static ServiceResult<T> InvalidField<T>(string field, string message)
        => ServiceResult<T>.Invalid(
            message,
            new Dictionary<string, string[]> { [field] = [message] });

    private static DriverResponse ToResponse(User user) => new()
    {
        Id = user.Id,
        FullName = user.FullName,
        PhoneNumber = user.PhoneNumber,
        Email = user.Email,
        IsActive = user.IsActive,
        CreatedAt = user.CreatedAt,
    };

    private static DriverTripResponse ToTripResponse(Trip trip) => new()
    {
        Id = trip.Id,
        RouteId = trip.RouteId,
        // Route/Bus có thể null trên lý thuyết nhưng A5 đặt Restrict cho cả hai khoá ngoại
        // nên không xoá được tuyến/xe còn chuyến tham chiếu — đường này thực tế không xảy ra.
        RouteCode = trip.Route?.Code ?? string.Empty,
        RouteName = trip.Route?.Name ?? string.Empty,
        BusId = trip.BusId,
        BusLicensePlate = trip.Bus?.LicensePlate ?? string.Empty,
        DepartureTime = trip.DepartureTime,
        ArrivalTime = trip.ArrivalTime,
        // Tên chuỗi của enum — đúng giá trị đang nằm trong cột Status
        // (HasConversion<string> ở AppDbContext.Trip.cs).
        Status = trip.Status.ToString(),
        CreatedAt = trip.CreatedAt,
        UpdatedAt = trip.UpdatedAt,
    };
}
