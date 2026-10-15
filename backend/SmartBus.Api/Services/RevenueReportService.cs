using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Reports;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IRevenueReportService"/> — tổng hợp doanh thu theo ngày / tuần / tháng
/// (US 19 "Doanh thu", Sprint 4, task *"API báo cáo doanh thu theo ngày / tuần / tháng"* — Trần
/// Trung Hiếu). Hợp đồng đầy đủ ở mục "Báo cáo doanh thu — /reports/revenue" của api-contract.md.
///
/// Năm chốt nghiệp vụ, đọc trước khi sửa:
///
/// 1. **Nền là bảng Payments, chỉ đếm Success.** Doanh thu là tiền ĐÃ THU THỰC — kiểm thử chéo
///    của story này đối chiếu "số liệu doanh thu khớp với giao dịch thực tế", mà giao dịch thực
///    tế nằm ở Payments. Vé Paid/Used không phải nền: tiền mới là thứ được đối soát, vé chỉ là
///    hàng hoá phát hành kèm. Mốc thời gian của giao dịch là <c>PaidAt</c> (cổng ghi nhận thu
///    tiền); dữ liệu cũ/lỗi thiếu <c>PaidAt</c> thì dùng <c>CreatedAt</c> — giao dịch đã thu
///    tiền không được biến mất khỏi báo cáo chỉ vì thiếu một cột thời gian.
/// 2. **Trục thời gian là giờ Việt Nam (UTC+7 cố định).** Giao dịch 00:30 giờ Việt Nam phải thuộc
///    ngày HÔM ĐÓ, không phải ngày hôm trước theo UTC — báo cáo đọc bởi người Việt. Cố ý không
///    dùng <c>TimeZoneInfo</c> (tên vùng khác nhau giữa Windows và Linux — cùng chốt của
///    TicketPdfModel). Tuần bắt đầu THỨ HAI (ISO), tháng theo lịch dương.
/// 3. **Tổng và dãy bucket lấy từ HAI nguồn truy vấn khác nhau.** Tổng là Sum/Count gộp trong
///    CSDL; bucket gộp trong C# từ danh sách dòng thô (nhóm theo ngày VN không dịch được bằng
///    LINQ chung cho cả Npgsql lẫn InMemory). Nhờ vậy đẳng thức "tổng == cộng các bucket" là
///    phép kiểm THẬT — phép gộp lệch (ví dụ lệch múi giờ làm rơi dòng) là test đỏ ngay, không
///    bao giờ xanh theo định nghĩa (cùng lý do chốt 1 của VoucherStatisticsService).
/// 4. **Dãy bucket LIÊN TỤC, không bỏ ngày rỗng.** Biểu đồ cần trục thời gian đầy đủ: ngày không
///    có giao dịch thì cột 0. Bucket đầu có thể bắt đầu TRƯỚC from (tuần từ thứ Hai, tháng từ
///    ngày 1) để khối thời gian vẽ trọn vẹn.
/// 5. **Lọc tuyến bằng hai truy vấn (danh sách tripId), không JOIN navigation.** Phép JOIN qua
///    <c>p.Trip.RouteId</c> hành xử khác nhau giữa Npgsql và InMemory của test (navigation null
///    ở dòng seed mồ côi) — cùng lối LoadActorsAsync của AuditLogExportService.
/// </summary>
public sealed class RevenueReportService : IRevenueReportService
{
    /// <summary>Bỏ trống cả from lẫn to thì lấy 30 ngày gần nhất, TÍNH CẢ hôm nay (giờ Việt Nam).</summary>
    private const int DefaultWindowDays = 30;

    /// <summary>Trần khoảng báo cáo — chặn biểu đồ phình vô nghĩa (366 ngày = trọn một năm).</summary>
    private const int MaxSpanDays = 366;

    /// <summary>Múi giờ Việt Nam — cố định +7, không DST từ 1975. Lý do không dùng TimeZoneInfo ở doc lớp.</summary>
    private const int VietnamUtcOffsetHours = 7;

    private readonly AppDbContext _db;

    public RevenueReportService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<RevenueReportResponse>> GetAsync(
        RevenueReportRequest request,
        CancellationToken cancellationToken)
    {
        if (!RevenueGranularityCodes.TryParse(request.Granularity, out var granularity))
        {
            const string message = "Granularity phải là day, week hoặc month.";

            return ServiceResult<RevenueReportResponse>.Invalid(
                message,
                new Dictionary<string, string[]> { ["granularity"] = [message] });
        }

        var todayVietnam = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(VietnamUtcOffsetHours));
        var fromDate = request.From ?? todayVietnam.AddDays(-(DefaultWindowDays - 1));
        var toDate = request.To ?? todayVietnam;

        if (toDate < fromDate)
        {
            return InvalidRange("Ngày kết thúc phải bằng hoặc sau ngày bắt đầu");
        }

        if (toDate.DayNumber - fromDate.DayNumber + 1 > MaxSpanDays)
        {
            return InvalidRange($"Khoảng thời gian tối đa {MaxSpanDays} ngày.");
        }

        if (request.RouteId is { } routeId)
        {
            var routeExists = await _db.Routes.AnyAsync(r => r.Id == routeId, cancellationToken);

            if (!routeExists)
            {
                return ServiceResult<RevenueReportResponse>.NotFound("Không tìm thấy tuyến đường.");
            }
        }

        // Nửa khoảng [00:00 VN từ from, 00:00 VN ngày SAU to) — tính trọn cả hai đầu mút mà không
        // phải sinh mốc 23:59:59.9999999 (cùng lối AuditLogExportService.BuildQuery).
        var fromUtc = fromDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(-VietnamUtcOffsetHours);
        var toExclusiveUtc = toDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
            .AddHours(-VietnamUtcOffsetHours);

        var query = await BuildQuery(request.RouteId, fromUtc, toExclusiveUtc, cancellationToken);

        // Tổng bằng truy vấn gộp riêng — KHÔNG cộng từ bucket (xem chốt 3 ở doc lớp).
        var totalRevenue = await query.SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;
        var totalPayments = await query.CountAsync(cancellationToken);

        // Bucket gộp trong C# từ dòng thô — chỉ kéo ba cột, không nạp cả bản ghi.
        var rows = await query
            .Select(p => new { p.PaidAt, p.CreatedAt, p.Amount })
            .ToListAsync(cancellationToken);

        var buckets = GroupIntoBuckets(
            rows.Select(r => (RevenueAt: r.PaidAt ?? r.CreatedAt, r.Amount)),
            granularity,
            fromDate,
            toDate);

        return ServiceResult<RevenueReportResponse>.Ok(new RevenueReportResponse
        {
            // Trả tên CHỮ THƯỜNG (day/week/month) — vọng lại đúng giá trị chính tắc của tham số
            // cho FE so khớp trực tiếp, không phải enum name "Day" kiểu trạng thái lưu CSDL.
            Granularity = granularity.ToString().ToLowerInvariant(),
            From = fromDate,
            To = toDate,
            TotalRevenue = totalRevenue,
            TotalPayments = totalPayments,
            Buckets = buckets,
        });
    }

    /// <summary>
    /// Truy vấn nền: giao dịch Success trong khoảng thời gian, lọc theo tuyến nếu có. Lọc tuyến
    /// đi hai bước (danh sách tripId của tuyến rồi Contains) — không JOIN navigation, xem chốt 5.
    /// </summary>
    private async Task<IQueryable<Payment>> BuildQuery(
        Guid? routeId,
        DateTime fromUtc,
        DateTime toExclusiveUtc,
        CancellationToken cancellationToken)
    {
        var query = _db.Payments
            .AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Success
                && (p.PaidAt ?? p.CreatedAt) >= fromUtc
                && (p.PaidAt ?? p.CreatedAt) < toExclusiveUtc);

        if (routeId is { } rid)
        {
            var tripIds = await _db.Trips
                .Where(t => t.RouteId == rid)
                .Select(t => t.Id)
                .ToListAsync(cancellationToken);

            query = query.Where(p => tripIds.Contains(p.TripId));
        }

        return query;
    }

    /// <summary>
    /// Gộp dòng thô thành dãy bucket liên tục — hàm THUẦN, public để test gọi thẳng mà ghim
    /// từng biên (lệch múi giờ, đầu tuần, đầu tháng) như TicketPdfService.BuildQrSvg.
    ///
    /// <paramref name="revenueAt"/> là UTC (Kind Unspecified coi như UTC — cùng chốt của
    /// TicketPdfModel). Bucket đầu tiên có thể bắt đầu TRƯỚC <paramref name="fromDate"/>.
    /// </summary>
    public static IReadOnlyList<RevenueBucketResponse> GroupIntoBuckets(
        IEnumerable<(DateTime RevenueAt, decimal Amount)> payments,
        RevenueGranularity granularity,
        DateOnly fromDate,
        DateOnly toDate)
    {
        var totals = new Dictionary<DateOnly, (decimal Revenue, int Count)>();

        foreach (var (revenueAt, amount) in payments)
        {
            var vietnamDate = ToVietnamDate(revenueAt);
            var key = BucketStart(vietnamDate, granularity);

            totals.TryGetValue(key, out var current);
            totals[key] = (current.Revenue + amount, current.Count + 1);
        }

        var buckets = new List<RevenueBucketResponse>();
        var cursor = BucketStart(fromDate, granularity);

        while (cursor <= toDate)
        {
            totals.TryGetValue(cursor, out var current);

            buckets.Add(new RevenueBucketResponse
            {
                PeriodStart = RevenueBucketFormat.Format(cursor),
                Revenue = current.Revenue,
                Payments = current.Count,
            });

            cursor = granularity switch
            {
                RevenueGranularity.Day => cursor.AddDays(1),
                RevenueGranularity.Week => cursor.AddDays(7),
                _ => cursor.AddMonths(1),
            };
        }

        return buckets;
    }

    /// <summary>Ngày (lịch Việt Nam) của một mốc UTC.</summary>
    private static DateOnly ToVietnamDate(DateTime utc)
    {
        var asUtc = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);

        return DateOnly.FromDateTime(asUtc.AddHours(VietnamUtcOffsetHours));
    }

    /// <summary>Điểm bắt đầu bucket chứa ngày <paramref name="date"/> — chính ngày / thứ Hai / ngày 1.</summary>
    private static DateOnly BucketStart(DateOnly date, RevenueGranularity granularity) => granularity switch
    {
        RevenueGranularity.Week => date.AddDays(-(((int)date.DayOfWeek + 6) % 7)),   // thứ Hai = 0
        RevenueGranularity.Month => new DateOnly(date.Year, date.Month, 1),
        _ => date,
    };

    private static ServiceResult<RevenueReportResponse> InvalidRange(string message)
    {
        // Gắn lỗi vào CẢ HAI ô ngày vì cả hai đều phải đổi mới hợp lệ được — cùng lối
        // AuditLogExportService.TooManyRows.
        return ServiceResult<RevenueReportResponse>.Invalid(message, new Dictionary<string, string[]>
        {
            ["from"] = [message],
            ["to"] = [message],
        });
    }
}
