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
            Items = buses.Select(ToResponse).ToList(),
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

        return bus is null
            ? ServiceResult<BusResponse>.NotFound(BusNotFoundMessage)
            : ServiceResult<BusResponse>.Ok(ToResponse(bus));
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

        return ServiceResult<BusResponse>.Ok(ToResponse(bus));
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

        return ServiceResult<BusResponse>.Ok(ToResponse(bus));
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

        return ServiceResult<BusResponse>.Ok(ToResponse(bus));
    }

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
