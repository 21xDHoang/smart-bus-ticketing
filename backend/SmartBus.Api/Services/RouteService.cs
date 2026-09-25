using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Routes;
using SmartBus.Api.Entities;

// Web SDK tự thêm implicit using Microsoft.AspNetCore.Routing, trong đó có lớp Route —
// trùng tên với entity Route của mình. Alias để mọi chỗ trong file này trỏ về entity
// (giống AppDbContext.Route.cs đã làm).
using Route = SmartBus.Api.Entities.Route;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IRouteService"/>. Story 12 — Trần Trung Hiếu.
/// </summary>
public class RouteService : IRouteService
{
    private const string RouteNotFoundMessage = "Không tìm thấy tuyến đường";
    private const string CodeExistsMessage = "Mã tuyến đã tồn tại";
    private const string StatusInvalidMessage = "Trạng thái không hợp lệ";
    private const string DistanceNotNegativeMessage = "Chiều dài tuyến không được âm";
    private const string DistanceTooLargeMessage = "Chiều dài tuyến tối đa 9 999.99 km";

    /// <summary>Trần của cột numeric(6,2): 6 chữ số tổng, trong đó 2 chữ số thập phân.</summary>
    private static readonly decimal MaxDistanceKm = 9999.99m;

    private const int DefaultPageSize = 10;

    private readonly AppDbContext _db;

    public RouteService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<RouteListResponse>> ListAsync(
        ListRoutesRequest request,
        CancellationToken cancellationToken = default)
    {
        var page = request.Page ?? 1;
        var pageSize = request.PageSize ?? DefaultPageSize;

        var query = _db.Routes.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            // So khớp không phân biệt hoa thường bằng ToLower() thay vì EF.Functions.ILike:
            // ILike là hàm riêng của Npgsql, dùng nó thì test chạy trên provider InMemory sẽ đổ.
            var keyword = request.Search.Trim().ToLowerInvariant();

            query = query.Where(r =>
                r.Code.ToLower().Contains(keyword) ||
                r.Name.ToLower().Contains(keyword) ||
                r.Origin.ToLower().Contains(keyword) ||
                r.Destination.ToLower().Contains(keyword));
        }

        if (!string.IsNullOrWhiteSpace(request.Status) &&
            TryParseRouteStatus(request.Status, out var status))
        {
            query = query.Where(r => r.Status == status);
        }
        else if (!string.IsNullOrWhiteSpace(request.Status))
        {
            // Mã trạng thái không khớp "Active"/"Inactive": không báo lỗi mà trả danh sách
            // rỗng — cùng lối AdminUserService lọc vai trò bằng chuỗi so khớp trực tiếp.
            return ServiceResult<RouteListResponse>.Ok(new RouteListResponse
            {
                Items = [],
                Total = 0,
                Page = page,
                PageSize = pageSize,
            });
        }

        var total = await query.CountAsync(cancellationToken);

        var routes = await query
            .OrderByDescending(r => r.CreatedAt)
            // Chốt thêm theo Id: nhiều tuyến tạo cùng lúc có thể trùng CreatedAt, thiếu khoá
            // phụ thì thứ tự giữa các trang không ổn định và phân trang bị trùng/thiếu dòng.
            .ThenBy(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return ServiceResult<RouteListResponse>.Ok(new RouteListResponse
        {
            Items = routes.Select(ToResponse).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize,
        });
    }

    public async Task<ServiceResult<RouteResponse>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var route = await _db.Routes
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        return route is null
            ? ServiceResult<RouteResponse>.NotFound(RouteNotFoundMessage)
            : ServiceResult<RouteResponse>.Ok(ToResponse(route));
    }

    public async Task<ServiceResult<RouteResponse>> CreateAsync(
        CreateRouteRequest request,
        CancellationToken cancellationToken = default)
    {
        var code = request.Code.Trim();

        if (ValidateDistanceKm(request.DistanceKm) is { } distanceError)
        {
            return InvalidField("distanceKm", distanceError);
        }

        if (await CodeTakenAsync(code, exceptRouteId: null, cancellationToken))
        {
            return InvalidField("code", CodeExistsMessage);
        }

        var route = new Route
        {
            Code = code,
            Name = request.Name.Trim(),
            Origin = request.Origin.Trim(),
            Destination = request.Destination.Trim(),
            DistanceKm = request.DistanceKm,
        };

        _db.Routes.Add(route);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Hai request cùng mã tuyến chạy song song đều lọt qua bước kiểm tra ở trên.
            // Ràng buộc unique trên cột Code là lớp chặn cuối cùng.
            return InvalidField("code", CodeExistsMessage);
        }

        return ServiceResult<RouteResponse>.Ok(ToResponse(route));
    }

    public async Task<ServiceResult<RouteResponse>> UpdateAsync(
        Guid id,
        UpdateRouteRequest request,
        CancellationToken cancellationToken = default)
    {
        var route = await _db.Routes.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (route is null)
        {
            return ServiceResult<RouteResponse>.NotFound(RouteNotFoundMessage);
        }

        var code = request.Code.Trim();

        if (ValidateDistanceKm(request.DistanceKm) is { } distanceError)
        {
            return InvalidField("distanceKm", distanceError);
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!TryParseRouteStatus(request.Status, out var status))
            {
                return InvalidField("status", StatusMessage());
            }

            route.Status = status;
        }

        if (code != route.Code &&
            await CodeTakenAsync(code, exceptRouteId: id, cancellationToken))
        {
            return InvalidField("code", CodeExistsMessage);
        }

        route.Code = code;
        route.Name = request.Name.Trim();
        route.Origin = request.Origin.Trim();
        route.Destination = request.Destination.Trim();
        route.DistanceKm = request.DistanceKm;
        route.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return InvalidField("code", CodeExistsMessage);
        }

        return ServiceResult<RouteResponse>.Ok(ToResponse(route));
    }

    public async Task<ServiceResult<RouteResponse>> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var route = await _db.Routes.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (route is null)
        {
            return ServiceResult<RouteResponse>.NotFound(RouteNotFoundMessage);
        }

        // Xoá mềm = chuyển về Inactive (quy ước A4: cấm thêm cột IsDeleted, và Route đã có
        // sẵn cột trạng thái cho đúng việc này). Tuyến đã Inactive gọi lại thì trả về luôn —
        // idempotent, cùng lối AdminUserService.SetStatusAsync.
        if (route.Status != RouteStatus.Inactive)
        {
            route.Status = RouteStatus.Inactive;
            route.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return ServiceResult<RouteResponse>.Ok(ToResponse(route));
    }

    /// <summary>Mã tuyến là khoá nghiệp vụ duy nhất toàn hệ thống — có ràng buộc unique ở CSDL.</summary>
    private Task<bool> CodeTakenAsync(string code, Guid? exceptRouteId, CancellationToken cancellationToken)
    {
        var query = _db.Routes.Where(r => r.Code == code);

        if (exceptRouteId is not null)
        {
            var exceptId = exceptRouteId.Value;
            query = query.Where(r => r.Id != exceptId);
        }

        return query.AnyAsync(cancellationToken);
    }

    /// <summary>
    /// Kiểm tra biên chiều dài ở tầng nghiệp vụ chứ không gắn [Range] ở DTO: biên trên phải
    /// là decimal chính xác (trần của cột numeric(6,2)), mà [Range] chỉ nhận biên dưới dạng
    /// chuỗi. Khai báo một lần bằng decimal ở đây, và thông báo lỗi cũng đi qua đúng cấu
    /// trúc { message, errors } của dự án. Trả về null nếu giá trị hợp lệ.
    /// </summary>
    private static string? ValidateDistanceKm(decimal distanceKm)
        => distanceKm < 0m ? DistanceNotNegativeMessage
        : distanceKm > MaxDistanceKm ? DistanceTooLargeMessage
        : null;

    /// <summary>
    /// So khớp với danh sách tên của enum thay vì dùng <c>Enum.TryParse</c> — cùng lý do
    /// FareService làm với PassengerType: TryParse chấp nhận cả chuỗi số, trái quy ước A3.
    /// Bỏ qua hoa/thường khi đọc, nhưng giá trị lưu xuống CSDL luôn là tên chuẩn của enum.
    /// </summary>
    private static bool TryParseRouteStatus(string? value, out RouteStatus status)
    {
        var trimmed = value?.Trim();

        foreach (var name in Enum.GetNames<RouteStatus>())
        {
            if (string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                status = Enum.Parse<RouteStatus>(name);
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
        => $"{StatusInvalidMessage}. Chấp nhận: {string.Join(", ", Enum.GetNames<RouteStatus>())}";

    private static ServiceResult<RouteResponse> InvalidField(string field, string message)
        => ServiceResult<RouteResponse>.Invalid(
            message,
            new Dictionary<string, string[]> { [field] = [message] });

    private static RouteResponse ToResponse(Route route) => new()
    {
        Id = route.Id,
        Code = route.Code,
        Name = route.Name,
        Origin = route.Origin,
        Destination = route.Destination,
        DistanceKm = route.DistanceKm,
        // Tên chuỗi của enum — đúng giá trị đang nằm trong cột Status
        // (HasConversion<string> ở AppDbContext.Route.cs).
        Status = route.Status.ToString(),
        CreatedAt = route.CreatedAt,
        UpdatedAt = route.UpdatedAt,
    };
}
