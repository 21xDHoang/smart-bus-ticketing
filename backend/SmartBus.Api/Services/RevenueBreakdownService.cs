using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Reports;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IRevenueBreakdownService"/> — doanh thu theo tuyến / xe / phương thức
/// thanh toán (US 19 "Doanh thu", Sprint 4, task *"API doanh thu theo tuyến, theo xe, theo phương
/// thức thanh toán"* — Nguyễn Duy Kiên). Hợp đồng đầy đủ ở mục "Báo cáo doanh thu —
/// /reports/revenue" của api-contract.md.
///
/// Năm chốt nghiệp vụ, đọc trước khi sửa:
///
/// 1. **Cùng nền với báo cáo thời gian.** Bảng <c>Payments</c>, chỉ <c>Success</c>, mốc
///    <c>PaidAt ?? CreatedAt</c>, nửa khoảng [00:00 VN from, 00:00 VN ngày sau to), ngày theo giờ
///    Việt Nam UTC+7 cố định. Ba endpoint không dựng nền tính riêng — định nghĩa "doanh thu" nằm
///    ở đúng một chỗ về mặt nghiệp vụ; các hằng số dưới đây chép từ
///    <see cref="RevenueReportService"/> (file của Hiếu, quy ước E1 không sửa) và có test đối
///    chiếu tổng với chính báo cáo thời gian để hai bên không trôi khỏi nhau.
/// 2. **Tổng bằng truy vấn gộp RIÊNG, không cộng từ dòng trả về.** Cùng chốt 3 của
///    <see cref="RevenueReportService"/>: nhờ vậy đẳng thức "tổng == cộng các dòng" là phép kiểm
///    thật — một dòng bị rơi (ví dụ chuyến mồ côi) làm test đỏ ngay thay vì tự che mất.
/// 3. **Phân nhóm và lấy tên ở HAI bước, không JOIN navigation.** Nhóm theo <c>TripId</c> (hoặc
///    <c>MethodCode</c>) ngay trong CSDL, rồi nạp bảng <c>Trips</c>/<c>Routes</c>/<c>Buses</c> bằng
///    danh sách khoá — cùng lối <c>BuildQuery</c> của báo cáo gốc và
///    <c>AuditLogExportService.LoadActorsAsync</c>. JOIN navigation hành xử khác nhau giữa Npgsql
///    và InMemory ở dòng seed mồ côi.
/// 4. **Chỉ trả nhóm CÓ phát sinh giao dịch Success.** Tuyến/xe/phương thức không bán được vé
///    không hiện thành dòng 0 — phản hồi không phình theo danh mục toàn công ty. Màn hình cần đủ
///    danh sách thì đối chiếu với API danh mục ở FE.
/// 5. **Thứ tự tất định**: doanh thu giảm dần, hoà thì mã tuyến / biển số / mã phương thức tăng
///    dần theo Ordinal. FE không phải tự sắp, và hai lượt gọi cùng dữ liệu trả cùng thứ tự.
///
/// Service KHÔNG đệm (khác báo cáo thời gian — task cache của Dăm): ba báo cáo này là lượt xem
/// có chủ đích, và việc đệm riêng sẽ khiến tổng của chúng lệch bản thời gian trong tối đa 60
/// giây mà không có lợi ích rõ ràng.
/// </summary>
public sealed class RevenueBreakdownService : IRevenueBreakdownService
{
    /// <summary>Bỏ trống cả from lẫn to thì lấy 30 ngày gần nhất, TÍNH CẢ hôm nay (giờ Việt Nam).</summary>
    private const int DefaultWindowDays = 30;

    /// <summary>Trần khoảng báo cáo — 366 ngày = trọn một năm, cùng trần báo cáo thời gian.</summary>
    private const int MaxSpanDays = 366;

    /// <summary>Múi giờ Việt Nam — cố định +7, không DST từ 1975. Cùng con số với báo cáo gốc.</summary>
    private const int VietnamUtcOffsetHours = 7;

    private readonly AppDbContext _db;

    public RevenueBreakdownService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<RevenueByRouteResponse>> GetByRouteAsync(
        RevenueBreakdownRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryResolveWindow(request, out var window, out var error))
        {
            return ServiceResult<RevenueByRouteResponse>.Invalid(error!.Message, error.Errors);
        }

        var query = BuildQuery(window);
        var (totalRevenue, totalPayments) = await TotalizeAsync(query, cancellationToken);

        var tripRevenue = await GroupByTripAsync(query, cancellationToken);

        List<RevenueRouteRowResponse> routes = tripRevenue.Count == 0
            ? []
            : await AttributeToRoutesAsync(tripRevenue, cancellationToken);

        return ServiceResult<RevenueByRouteResponse>.Ok(new RevenueByRouteResponse
        {
            From = window.From,
            To = window.To,
            TotalRevenue = totalRevenue,
            TotalPayments = totalPayments,
            Routes = routes,
        });
    }

    public async Task<ServiceResult<RevenueByBusResponse>> GetByBusAsync(
        RevenueBreakdownRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryResolveWindow(request, out var window, out var error))
        {
            return ServiceResult<RevenueByBusResponse>.Invalid(error!.Message, error.Errors);
        }

        var query = BuildQuery(window);
        var (totalRevenue, totalPayments) = await TotalizeAsync(query, cancellationToken);

        var tripRevenue = await GroupByTripAsync(query, cancellationToken);

        List<RevenueBusRowResponse> buses = tripRevenue.Count == 0
            ? []
            : await AttributeToBusesAsync(tripRevenue, cancellationToken);

        return ServiceResult<RevenueByBusResponse>.Ok(new RevenueByBusResponse
        {
            From = window.From,
            To = window.To,
            TotalRevenue = totalRevenue,
            TotalPayments = totalPayments,
            Buses = buses,
        });
    }

    public async Task<ServiceResult<RevenueByPaymentMethodResponse>> GetByPaymentMethodAsync(
        RevenueBreakdownRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryResolveWindow(request, out var window, out var error))
        {
            return ServiceResult<RevenueByPaymentMethodResponse>.Invalid(error!.Message, error.Errors);
        }

        var query = BuildQuery(window);
        var (totalRevenue, totalPayments) = await TotalizeAsync(query, cancellationToken);

        // Nhóm ngay theo cột của Payments — không cần bước nạp bảng phụ như tuyến/xe.
        var groups = await query
            .GroupBy(p => p.MethodCode)
            .Select(g => new
            {
                MethodCode = g.Key,
                Revenue = g.Sum(p => p.Amount),
                Payments = g.Count(),
            })
            .ToListAsync(cancellationToken);

        var rows = groups
            .OrderByDescending(g => g.Revenue)
            .ThenBy(g => g.MethodCode, StringComparer.Ordinal)
            .Select(g => new RevenuePaymentMethodRowResponse
            {
                MethodCode = g.MethodCode,
                Revenue = g.Revenue,
                Payments = g.Payments,
            })
            .ToList();

        return ServiceResult<RevenueByPaymentMethodResponse>.Ok(new RevenueByPaymentMethodResponse
        {
            From = window.From,
            To = window.To,
            TotalRevenue = totalRevenue,
            TotalPayments = totalPayments,
            PaymentMethods = rows,
        });
    }

    /// <summary>
    /// Truy vấn nền: giao dịch Success trong nửa khoảng thời gian của báo cáo. Không JOIN
    /// navigation — xem chốt 3 ở doc lớp.
    /// </summary>
    private IQueryable<Payment> BuildQuery(ResolvedWindow window)
        => _db.Payments
            .AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Success
                && (p.PaidAt ?? p.CreatedAt) >= window.FromUtc
                && (p.PaidAt ?? p.CreatedAt) < window.ToExclusiveUtc);

    /// <summary>Tổng cả kỳ bằng truy vấn gộp riêng — không cộng từ dòng trả về (chốt 2).</summary>
    private static async Task<(decimal Revenue, int Payments)> TotalizeAsync(
        IQueryable<Payment> query,
        CancellationToken cancellationToken)
    {
        var revenue = await query.SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;
        var payments = await query.CountAsync(cancellationToken);

        return (revenue, payments);
    }

    /// <summary>Doanh thu theo từng chuyến — bước trung gian chung của báo cáo tuyến và báo cáo xe.</summary>
    private static async Task<List<TripRevenue>> GroupByTripAsync(
        IQueryable<Payment> query,
        CancellationToken cancellationToken)
    {
        var rows = await query
            .GroupBy(p => p.TripId)
            .Select(g => new
            {
                TripId = g.Key,
                Revenue = g.Sum(p => p.Amount),
                Payments = g.Count(),
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new TripRevenue(r.TripId, r.Revenue, r.Payments)).ToList();
    }

    /// <summary>
    /// Gộp doanh thu theo chuyến sang theo tuyến (nạp Trips → Routes bằng danh sách khoá), rồi
    /// sắp xếp cố định: doanh thu giảm dần, mã tuyến tăng dần.
    /// </summary>
    private async Task<List<RevenueRouteRowResponse>> AttributeToRoutesAsync(
        List<TripRevenue> tripRevenue,
        CancellationToken cancellationToken)
    {
        var tripIds = tripRevenue.Select(r => r.TripId).Distinct().ToList();
        var routeIdByTrip = await _db.Trips
            .AsNoTracking()
            .Where(t => tripIds.Contains(t.Id))
            .Select(t => new { t.Id, t.RouteId })
            .ToDictionaryAsync(t => t.Id, t => t.RouteId, cancellationToken);

        var totalsByRoute = tripRevenue
            .Where(r => routeIdByTrip.ContainsKey(r.TripId))
            .GroupBy(r => routeIdByTrip[r.TripId])
            .Select(g => new
            {
                RouteId = g.Key,
                Revenue = g.Sum(r => r.Revenue),
                Payments = g.Sum(r => r.Payments),
            })
            .ToList();

        if (totalsByRoute.Count == 0)
        {
            return [];
        }

        var routeIds = totalsByRoute.Select(r => r.RouteId).ToList();
        var routeById = await _db.Routes
            .AsNoTracking()
            .Where(r => routeIds.Contains(r.Id))
            .Select(r => new { r.Id, r.Code, r.Name })
            .ToDictionaryAsync(r => r.Id, r => new RouteLookup(r.Code, r.Name), cancellationToken);

        return totalsByRoute
            .Where(t => routeById.ContainsKey(t.RouteId))
            .OrderByDescending(t => t.Revenue)
            .ThenBy(t => routeById[t.RouteId].Code, StringComparer.Ordinal)
            .Select(t => new RevenueRouteRowResponse
            {
                RouteId = t.RouteId,
                RouteCode = routeById[t.RouteId].Code,
                RouteName = routeById[t.RouteId].Name,
                Revenue = t.Revenue,
                Payments = t.Payments,
            })
            .ToList();
    }

    /// <summary>
    /// Gộp doanh thu theo chuyến sang theo xe (nạp Trips → Buses bằng danh sách khoá), rồi sắp
    /// xếp cố định: doanh thu giảm dần, biển số tăng dần.
    /// </summary>
    private async Task<List<RevenueBusRowResponse>> AttributeToBusesAsync(
        List<TripRevenue> tripRevenue,
        CancellationToken cancellationToken)
    {
        var tripIds = tripRevenue.Select(r => r.TripId).Distinct().ToList();
        var busIdByTrip = await _db.Trips
            .AsNoTracking()
            .Where(t => tripIds.Contains(t.Id))
            .Select(t => new { t.Id, t.BusId })
            .ToDictionaryAsync(t => t.Id, t => t.BusId, cancellationToken);

        var totalsByBus = tripRevenue
            .Where(r => busIdByTrip.ContainsKey(r.TripId))
            .GroupBy(r => busIdByTrip[r.TripId])
            .Select(g => new
            {
                BusId = g.Key,
                Revenue = g.Sum(r => r.Revenue),
                Payments = g.Sum(r => r.Payments),
            })
            .ToList();

        if (totalsByBus.Count == 0)
        {
            return [];
        }

        var busIds = totalsByBus.Select(b => b.BusId).ToList();
        var busById = await _db.Buses
            .AsNoTracking()
            .Where(b => busIds.Contains(b.Id))
            .Select(b => new { b.Id, b.LicensePlate, b.BusType })
            .ToDictionaryAsync(
                b => b.Id,
                b => new BusLookup(b.LicensePlate, b.BusType),
                cancellationToken);

        return totalsByBus
            .Where(t => busById.ContainsKey(t.BusId))
            .OrderByDescending(t => t.Revenue)
            .ThenBy(t => busById[t.BusId].LicensePlate, StringComparer.Ordinal)
            .Select(t => new RevenueBusRowResponse
            {
                BusId = t.BusId,
                LicensePlate = busById[t.BusId].LicensePlate,
                BusType = busById[t.BusId].BusType,
                Revenue = t.Revenue,
                Payments = t.Payments,
            })
            .ToList();
    }

    /// <summary>
    /// Áp mặc định 30 ngày, kiểm tra khoảng ngày rồi đổi sang nửa khoảng UTC của trục giờ Việt
    /// Nam. Cùng luật với <see cref="RevenueReportService"/> (chép lại vì file đó của Hiếu — quy
    /// ước E1); lỗi gắn vào CẢ HAI ô ngày vì cả hai đều phải đổi mới hợp lệ được.
    /// </summary>
    private static bool TryResolveWindow(
        RevenueBreakdownRequest request,
        out ResolvedWindow window,
        out WindowError? error)
    {
        var todayVietnam = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(VietnamUtcOffsetHours));
        var fromDate = request.From ?? todayVietnam.AddDays(-(DefaultWindowDays - 1));
        var toDate = request.To ?? todayVietnam;

        if (toDate < fromDate)
        {
            return Invalid("Ngày kết thúc phải bằng hoặc sau ngày bắt đầu", out window, out error);
        }

        if (toDate.DayNumber - fromDate.DayNumber + 1 > MaxSpanDays)
        {
            return Invalid($"Khoảng thời gian tối đa {MaxSpanDays} ngày.", out window, out error);
        }

        // Nửa khoảng [00:00 VN from, 00:00 VN ngày SAU to) — tính trọn cả hai đầu mút mà không
        // phải sinh mốc 23:59:59.9999999 (cùng lối báo cáo gốc).
        var fromUtc = fromDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
            .AddHours(-VietnamUtcOffsetHours);
        var toExclusiveUtc = toDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
            .AddHours(-VietnamUtcOffsetHours);

        window = new ResolvedWindow(fromDate, toDate, fromUtc, toExclusiveUtc);
        error = null;

        return true;
    }

    private static bool Invalid(string message, out ResolvedWindow window, out WindowError? error)
    {
        window = default;
        error = new WindowError(message, new Dictionary<string, string[]>
        {
            ["from"] = [message],
            ["to"] = [message],
        });

        return false;
    }

    /// <summary>Khoảng ngày đã chốt: ngày lịch Việt Nam + nửa khoảng UTC để truy vấn.</summary>
    private readonly record struct ResolvedWindow(
        DateOnly From,
        DateOnly To,
        DateTime FromUtc,
        DateTime ToExclusiveUtc);

    /// <summary>Doanh thu của một chuyến — bước trung gian trước khi quy về tuyến/xe.</summary>
    private readonly record struct TripRevenue(Guid TripId, decimal Revenue, int Payments);

    private readonly record struct RouteLookup(string Code, string Name);

    private readonly record struct BusLookup(string LicensePlate, string BusType);

    private sealed record WindowError(string Message, Dictionary<string, string[]> Errors);
}
