using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Trips;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Tra cứu danh sách chuyến theo ngày + lọc theo tuyến — story 13, Phùng Duy Hoàng.
/// Hợp đồng đầy đủ ở mục "Chuyến xe — /trips" của docs/api-contract.md.
///
/// Chỉ đọc: không có thao tác ghi nào ở đây, nên cũng không có <c>SaveChangesAsync</c> và không có
/// <c>try/catch DbUpdateException</c> như các service CRUD khác — không có gì để xung đột.
/// </summary>
public class TripLookupService : ITripLookupService
{
    private const string RouteNotFoundMessage = "Không tìm thấy tuyến đường";
    private const string DateRangeMessage = "Thời điểm kết thúc phải sau thời điểm bắt đầu";

    /// <summary>Trùng con số của RouteService, RouteTripsService và AuditLogQueryService — các màn hình danh sách cùng cỡ trang.</summary>
    private const int DefaultPageSize = 10;

    private readonly AppDbContext _db;

    public TripLookupService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<TripLookupListResponse>> ListAsync(
        Guid? routeId,
        ListTripsRequest request,
        CancellationToken cancellationToken = default)
    {
        // routeId là tham chiếu cứng: GUID trỏ tới tuyến không tồn tại là lỗi gọi, không phải
        // "không có chuyến nào" — cùng câu hỏi "chuyến của tuyến X" hỏi ở /routes/{routeId}/trips
        // cũng trả 404. (status thì ngược lại: mã lạ trả danh sách rỗng — xem nhánh bên dưới.)
        if (routeId is { } route && !await _db.Routes.AnyAsync(r => r.Id == route, cancellationToken))
        {
            return ServiceResult<TripLookupListResponse>.NotFound(RouteNotFoundMessage);
        }

        // Cùng lối RouteTripsService: chỉ báo lỗi khi CẢ HAI mốc cùng có mặt — thiếu một mốc là
        // khoảng mở, không phải khoảng sai.
        if (request.From is { } from && request.To is { } to && to < from)
        {
            return InvalidField<TripLookupListResponse>("to", DateRangeMessage);
        }

        var page = request.Page ?? 1;
        var pageSize = request.PageSize ?? DefaultPageSize;

        var fromUtc = request.From?.UtcDateTime;
        var toUtc = request.To?.UtcDateTime;

        var query = _db.Trips.AsNoTracking();

        if (routeId is { } routeFilter)
        {
            query = query.Where(t => t.RouteId == routeFilter);
        }

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
                // RouteTripsService: mã lạ có thể là trạng thái hợp lệ trong tương lai.
                return ServiceResult<TripLookupListResponse>.Ok(new TripLookupListResponse
                {
                    Items = [],
                    Total = 0,
                    Page = page,
                    PageSize = pageSize,
                });
            }

            query = query.Where(t => t.Status == status);
        }

        // Ghép tường minh bằng join thay vì Include: máy chủ CSDL chỉ trả đúng những cột được
        // chọn, và join trong cũng là thứ đưa chuyến mồ côi (tuyến hoặc xe không còn) ra khỏi kết
        // quả một cách tự nhiên — cùng lối TripService.GetByIdAsync. Đếm bằng chính truy vấn ĐÃ
        // join để `total` khớp đúng số dòng liệt kê được; đếm trước khi join thì dòng mồ côi (nếu
        // có) vẫn vào `total` mà không bao giờ hiện ra.
        var joined = from trip in query
                     join r in _db.Routes.AsNoTracking() on trip.RouteId equals r.Id
                     join b in _db.Buses.AsNoTracking() on trip.BusId equals b.Id
                     select new
                     {
                         trip.Id,
                         RouteId = r.Id,
                         r.Code,
                         RouteName = r.Name,
                         trip.BusId,
                         BusLicensePlate = b.LicensePlate,
                         trip.DepartureTime,
                         trip.ArrivalTime,
                         trip.Status,
                         trip.CreatedAt,
                         trip.UpdatedAt,
                     };

        var total = await joined.CountAsync(cancellationToken);

        // Nhân bằng long rồi mới ép về int — cùng chiêu RouteTripsService và AuditLogQueryService:
        // (page - 1) * pageSize tính bằng int sẽ tràn thành số âm khi page tiến gần int.MaxValue.
        var skip = (long)(page - 1) * pageSize;

        var rows = await joined
            // Thời gian biểu đọc theo thứ tự chạy; trùng giờ khởi hành (chuyến sinh hàng loạt cùng
            // giây) xếp tiếp theo Id để phân trang không trùng hay thiếu dòng.
            .OrderBy(t => t.DepartureTime)
            .ThenBy(t => t.Id)
            .Skip((int)Math.Min(skip, int.MaxValue))
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        // Status đổi sang chuỗi ở bộ nhớ chứ không trong câu truy vấn: cột trong CSDL đã là chuỗi
        // (HasConversion<string> ở AppDbContext.Trip.cs), nhưng dịch ToString() của enum ra SQL là
        // việc provider phải đoán — đọc về rồi mới đổi thì không phụ thuộc vào bản dịch đó.
        return ServiceResult<TripLookupListResponse>.Ok(new TripLookupListResponse
        {
            Items = rows.Select(row => new TripLookupResponse
            {
                Id = row.Id,
                RouteId = row.RouteId,
                RouteCode = row.Code,
                RouteName = row.RouteName,
                BusId = row.BusId,
                BusLicensePlate = row.BusLicensePlate,
                DepartureTime = row.DepartureTime,
                ArrivalTime = row.ArrivalTime,
                Status = row.Status.ToString(),
                CreatedAt = row.CreatedAt,
                UpdatedAt = row.UpdatedAt,
            }).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize,
        });
    }

    /// <summary>
    /// So khớp với danh sách tên của enum thay vì dùng <c>Enum.TryParse</c> — cùng lý do
    /// RouteTripsService làm với TripStatus: TryParse chấp nhận cả chuỗi số, trái quy ước A3.
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

    private static ServiceResult<T> InvalidField<T>(string field, string message)
        => ServiceResult<T>.Invalid(
            message,
            new Dictionary<string, string[]> { [field] = [message] });
}
