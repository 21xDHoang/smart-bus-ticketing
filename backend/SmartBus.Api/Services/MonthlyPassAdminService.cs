using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.MonthlyPasses.Admin;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Màn quản trị vé tháng (GET /api/monthly-passes, GET /{id}, GET /statistics,
/// PATCH /{id}/status) — bổ sung ngoài story 16, hợp đồng ở mục
/// "Vé tháng — /monthly-passes" của docs/api-contract.md.
///
/// Quyền truy cập do controller gác bằng <see cref="RbacPolicies.ManagerOrAbove"/> — tầng này
/// KHÔNG kiểm quyền sở hữu: quản trị viên xem được mọi vé, cùng lối IFeedbackAdminService.
///
/// Service không đăng ký DI: đăng ký service nằm trong Program.cs — file của Phùng Duy Hoàng,
/// ngoài phạm vi được sửa. Controller nhận AppDbContext từ DI rồi khởi tạo service trực tiếp
/// (xem MonthlyPassAdminController) — vòng đời scoped vẫn đúng vì DbContext là scoped.
/// </summary>
public class MonthlyPassAdminService
{
    private const string PassNotFoundMessage = "Không tìm thấy vé tháng";
    private const string AlreadyCancelledMessage = "Vé tháng đã ở trạng thái đã huỷ";
    private const string AlreadyActiveMessage = "Vé tháng đã ở trạng thái Active";
    private const string ExpiredStatusMessage = "Vé tháng đã hết hạn, không thể thay đổi trạng thái";
    private const string ExpiredForCancelMessage = "Vé tháng đã hết hạn, không thể huỷ";
    private const string ExpiredForRestoreMessage = "Vé tháng đã hết hạn, không thể mở lại";

    private const int DefaultPageSize = 10;

    private readonly AppDbContext _db;

    public MonthlyPassAdminService(AppDbContext db) => _db = db;

    /// <summary>
    /// Danh sách phân trang, mới nhất trước. <c>Total</c> là tổng số dòng khớp bộ lọc.
    ///
    /// <c>Search</c> so khớp mã vé / họ tên / SĐT không phân biệt hoa thường bằng ToLower()
    /// thay vì EF.Functions.ILike — ILike là hàm riêng của Npgsql, dùng nó thì test chạy trên
    /// provider InMemory sẽ đổ (cùng lối BusService và AdminUserService).
    /// </summary>
    public async Task<MonthlyPassListResponse> ListAsync(
        ListMonthlyPassesRequest request,
        CancellationToken cancellationToken = default)
    {
        var page = request.Page ?? 1;
        var pageSize = request.PageSize ?? DefaultPageSize;

        var query = _db.MonthlyPasses.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var keyword = request.Search.Trim().ToLowerInvariant();

            // Tra id người dùng khớp từ khoá trước rồi lọc vé theo UserId, thay vì truy vấn
            // thẳng qua navigation p.User: provider InMemory của bộ test không nạp navigation
            // trong câu WHERE, còn trên Postgres hai cách tương đương nhau về kết quả.
            var matchingUserIds = await _db.Users
                .AsNoTracking()
                .Where(u => u.FullName.ToLower().Contains(keyword) ||
                            u.PhoneNumber.ToLower().Contains(keyword))
                .Select(u => u.Id)
                .ToListAsync(cancellationToken);

            query = query.Where(p =>
                p.Code.ToLower().Contains(keyword) ||
                matchingUserIds.Contains(p.UserId));
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            // Chỉ nhận đúng tên thành viên enum. Không dùng Enum.TryParse vì nó nhận cả số
            // ("0" → Active), trong khi query string của hợp đồng chỉ có ba mã chữ.
            var statusText = request.Status.Trim();
            var parsedStatus = Enum.GetValues<MonthlyPassStatus>()
                .Where(status => string.Equals(status.ToString(), statusText, StringComparison.OrdinalIgnoreCase))
                .Select(status => (MonthlyPassStatus?)status)
                .FirstOrDefault();

            // Giá trị lạ = bộ lọc không khớp gì → danh sách rỗng, không báo lỗi ở query string —
            // cùng lối GET /buses.
            if (parsedStatus is null)
            {
                return new MonthlyPassListResponse { Items = [], Total = 0, Page = page, PageSize = pageSize };
            }

            var statusValue = parsedStatus.Value;
            query = query.Where(p => p.Status == statusValue);
        }

        if (request.RouteId is { } routeId)
        {
            query = query.Where(p => p.RouteId == routeId);
        }

        if (!string.IsNullOrWhiteSpace(request.PassTypeCode))
        {
            var passTypeCode = request.PassTypeCode.Trim();

            // Cùng lý do như trên: tra Id theo mã rồi lọc theo khoá ngoại.
            var passTypeId = await _db.PassTypes
                .AsNoTracking()
                .Where(pt => pt.Code == passTypeCode)
                .Select(pt => (Guid?)pt.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (passTypeId is null)
            {
                return new MonthlyPassListResponse { Items = [], Total = 0, Page = page, PageSize = pageSize };
            }

            query = query.Where(p => p.PassTypeId == passTypeId.Value);
        }

        var total = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(p => p.CreatedAt)
            .ThenBy(p => p.Code)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(p => p.User)
            .Include(p => p.Route)
            .Include(p => p.PassType)
            .ToListAsync(cancellationToken);

        return new MonthlyPassListResponse
        {
            Items = rows.Select(ToResponse).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize,
        };
    }

    /// <summary>Chi tiết một vé bất kỳ; null khi id không tồn tại.</summary>
    public async Task<MonthlyPassAdminResponse?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        // Include rồi chiếu trong bộ nhớ thay vì Select thẳng sang DTO: ToResponse dùng
        // navigation (User/Route/PassType), mà EF chỉ dịch được biểu thức LINQ sang SQL chứ
        // không gọi được hàm C# thường trong Select.
        var pass = await _db.MonthlyPasses
            .AsNoTracking()
            .Include(p => p.User)
            .Include(p => p.Route)
            .Include(p => p.PassType)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        return pass is null ? null : ToResponse(pass);
    }

    /// <summary>
    /// Huỷ (Active → Cancelled) hoặc mở lại (Cancelled → Active) một vé.
    ///
    /// Luật chuyển trạng thái đầy đủ ở docs/api-contract.md; tóm tắt: chỉ đổi được khi vé CHƯA
    /// quá ValidTo, trạng thái Expired do job quản lý nên không đổi tay, và huỷ không hoàn tiền.
    /// </summary>
    public async Task<ServiceResult<MonthlyPassAdminResponse>> UpdateStatusAsync(
        Guid id,
        UpdateMonthlyPassStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var pass = await _db.MonthlyPasses.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (pass is null)
        {
            return ServiceResult<MonthlyPassAdminResponse>.NotFound(PassNotFoundMessage);
        }

        // Expired là trạng thái của job quét hết hạn — không phải trạng thái quản trị viên đổi tay.
        if (pass.Status == MonthlyPassStatus.Expired)
        {
            return ServiceResult<MonthlyPassAdminResponse>.Conflict(ExpiredStatusMessage);
        }

        var now = DateTime.UtcNow;
        var target = request.Status!.Trim();

        if (string.Equals(target, nameof(MonthlyPassStatus.Cancelled), StringComparison.OrdinalIgnoreCase))
        {
            if (pass.Status == MonthlyPassStatus.Cancelled)
            {
                return ServiceResult<MonthlyPassAdminResponse>.Conflict(AlreadyCancelledMessage);
            }

            // Đã quá ValidTo thì huỷ không còn ý nghĩa — sống chờ job lật sang Expired. Trả 409
            // chứ không âm thầm ghi Cancelled đè lên một vé đã hết hạn từ trước.
            if (pass.ValidTo < now)
            {
                return ServiceResult<MonthlyPassAdminResponse>.Conflict(ExpiredForCancelMessage);
            }

            pass.Status = MonthlyPassStatus.Cancelled;
        }
        else
        {
            // [RegularExpression] ở DTO đã chặn mọi giá trị ngoài Active/Cancelled.
            if (pass.Status == MonthlyPassStatus.Active)
            {
                return ServiceResult<MonthlyPassAdminResponse>.Conflict(AlreadyActiveMessage);
            }

            if (pass.ValidTo < now)
            {
                return ServiceResult<MonthlyPassAdminResponse>.Conflict(ExpiredForRestoreMessage);
            }

            pass.Status = MonthlyPassStatus.Active;
        }

        // A4: vé tháng có sửa dữ liệu nên có UpdatedAt.
        pass.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);

        var response = await GetAsync(id, cancellationToken);

        return ServiceResult<MonthlyPassAdminResponse>.Ok(response!);
    }

    /// <summary>
    /// Số vé theo nhóm hiệu lực + doanh thu. Bốn nhóm đếm phân hoạch đủ tập vé.
    ///
    /// Active/Upcoming/Expired suy từ cặp mốc chứ KHÔNG lọc cột Status (Active/Expired có độ trễ
    /// — xem MonthlyPassStatus). Cancelled đọc cột Status vì huỷ là chặn cứng do người đặt.
    /// </summary>
    public async Task<MonthlyPassStatisticsResponse> GetStatisticsAsync(
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var live = _db.MonthlyPasses.AsNoTracking()
            .Where(p => p.Status != MonthlyPassStatus.Cancelled);

        var totalCount = await _db.MonthlyPasses.CountAsync(cancellationToken);
        var cancelledCount = await _db.MonthlyPasses
            .CountAsync(p => p.Status == MonthlyPassStatus.Cancelled, cancellationToken);
        var activeCount = await live
            .CountAsync(p => p.ValidFrom <= now && p.ValidTo >= now, cancellationToken);
        var upcomingCount = await live
            .CountAsync(p => p.ValidFrom > now, cancellationToken);
        var expiredCount = await live
            .CountAsync(p => p.ValidTo < now, cancellationToken);

        // Doanh thu cộng MỌI vé, kể cả vé đã huỷ: huỷ không hoàn tiền nên tiền đã thu vẫn là
        // doanh thu. Select sang decimal? để Sum trả 0 khi bảng rỗng, không phụ thuộc cách
        // provider xử lý Sum trên tập rỗng.
        var totalRevenue = await _db.MonthlyPasses
            .Select(p => (decimal?)p.Price)
            .SumAsync(cancellationToken) ?? 0m;

        var revenueThisMonth = await _db.MonthlyPasses
            .Where(p => p.CreatedAt >= monthStart)
            .Select(p => (decimal?)p.Price)
            .SumAsync(cancellationToken) ?? 0m;

        return new MonthlyPassStatisticsResponse
        {
            TotalCount = totalCount,
            ActiveCount = activeCount,
            UpcomingCount = upcomingCount,
            ExpiredCount = expiredCount,
            CancelledCount = cancelledCount,
            TotalRevenue = totalRevenue,
            RevenueThisMonth = revenueThisMonth,
        };
    }

    private static MonthlyPassAdminResponse ToResponse(MonthlyPass pass) => new()
    {
        Id = pass.Id,
        Code = pass.Code,
        UserId = pass.UserId,
        UserFullName = pass.User?.FullName ?? string.Empty,
        UserPhoneNumber = pass.User?.PhoneNumber ?? string.Empty,
        RouteId = pass.RouteId,
        RouteCode = pass.Route?.Code ?? string.Empty,
        RouteName = pass.Route?.Name ?? string.Empty,
        PassTypeCode = pass.PassType?.Code ?? string.Empty,
        PassTypeName = pass.PassType?.Name ?? string.Empty,
        Price = pass.Price,
        ValidFrom = pass.ValidFrom,
        ValidTo = pass.ValidTo,
        Status = pass.Status.ToString(),
        CreatedAt = pass.CreatedAt,
        UpdatedAt = pass.UpdatedAt,
    };
}
