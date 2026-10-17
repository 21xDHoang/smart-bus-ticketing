using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Reports;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IFillRateReportService"/> — thống kê tỷ lệ lấp đầy theo chuyến, tuyến, khung
/// giờ (US 20 "Tỷ lệ lấp đầy", Sprint 4, task *"API thống kê tỷ lệ lấp đầy theo chuyến, tuyến,
/// khung giờ"* — Nguyễn Duy Kiên). Hợp đồng đầy đủ ở mục "Tỷ lệ lấp đầy — /reports/fill-rate"
/// của docs/api-contract.md.
///
/// Sáu chốt nghiệp vụ, đọc trước khi sửa:
///
/// 1. **Nền là bảng Trips, mỗi chuyến trong kỳ luôn có mặt — kể cả 0 khách.** Bảng thống kê phải
///    chỉ ra được chuyến nào bán ế; bỏ chuyến 0 khách đi thì đúng nhóm cần điều chỉnh quy mô xe
///    biến mất khỏi báo cáo. Nửa khoảng [00:00 VN from, 00:00 VN ngày sau to), giờ Việt Nam
///    UTC+7 cố định (không dùng <c>TimeZoneInfo</c> — tên vùng khác nhau giữa Windows và Linux).
/// 2. **Hành khách = vé <c>Paid</c> + <c>Used</c>, bỏ <c>Cancelled</c>.** Cùng định nghĩa với
///    <see cref="PeakHoursReportService"/>: người đã trả tiền và người đã soát vé đều là khách
///    trên xe; người huỷ/hoàn tiền thì không. Nhờ vậy `totalPassengers` của hai báo cáo bằng nhau
///    trên cùng khoảng ngày (có test đối chiếu).
/// 3. **Mẫu số là sức chứa của xe chạy chuyến (<c>Buses.Capacity</c>), không phải số ghế đã bán
///    hay số chuyến.** Tỷ lệ lấp đầy = passengers / capacity × 100, làm tròn 1 chữ số thập phân
///    (AwayFromZero). KHÔNG chặn ở 100 — bán vượt sức chứa là dữ liệu phải lộ ra, không che.
///    Xe không có sức chứa (≤ 0, dữ liệu lỗi) trả 0 thay vì chia cho 0.
/// 4. **Trung bình là CÓ TRỌNG SỐ theo sức chứa** (<c>tổng khách / tổng ghế × 100</c>), không
///    phải trung bình cộng tỷ lệ từng chuyến. Một chuyến 4/40 và một chuyến 36/60 không được
///    nặng như nhau khi nhìn cả đội xe — đúng dòng ghi chú trên thẻ "Trung bình khách trên tổng
///    sức chứa ghế" của màn hình.
/// 5. **Hai ngưỡng đếm chuyến đầy/trống, so trên giá trị ĐÃ LÀM TRÒN** — đúng con số hiển thị:
///    đầy ≥ 80%, trống &lt; 50%. Chuyến 19/40 = 47.5% là trống; 20/40 = 50.0% KHÔNG trống (và chưa
///    đầy). Hai ngưỡng cố ý không đối xứng — "đầy" là mốc cần tăng xe, "trống" là mốc cần giảm.
/// 6. **Ba cách nhìn cùng một tập chuyến nên phải cộng khớp.** `routes[]` và `timeSlots[]` đều
///    gộp từ đúng danh sách đã dựng cho `trips[]`; năm khung giờ phủ trọn 24 giờ (night vắt qua
///    nửa đêm) nên tổng của mỗi dải luôn bằng `summary`. Bộ test ghim đẳng thức này — một chuyến
///    rơi khỏi bản gộp là test đỏ ngay thay vì tự che mất.
///
/// Service KHÔNG đệm — cùng lý do ba báo cáo doanh thu phân rã: đây là lượt xem có chủ đích, dữ
/// liệu phải tươi ngay sau khi có vé mới.
/// </summary>
public sealed class FillRateReportService : IFillRateReportService
{
    /// <summary>Bỏ trống cả from lẫn to thì lấy 30 ngày gần nhất, TÍNH CẢ hôm nay (giờ Việt Nam).</summary>
    private const int DefaultWindowDays = 30;

    /// <summary>Trần khoảng báo cáo — 366 ngày = trọn một năm, cùng trần báo cáo khung giờ cao điểm.</summary>
    private const int MaxSpanDays = 366;

    /// <summary>Múi giờ Việt Nam — cố định +7, không DST từ 1975. Cùng con số với báo cáo khung giờ.</summary>
    private const int VietnamUtcOffsetHours = 7;

    /// <summary>Chuyến "đầy" khi tỷ lệ lấp đầy (đã làm tròn) đạt mốc này.</summary>
    private const decimal FullThresholdPct = 80m;

    /// <summary>Chuyến "trống" khi tỷ lệ lấp đầy (đã làm tròn) dưới mốc này.</summary>
    private const decimal EmptyThresholdPct = 50m;

    private readonly AppDbContext _db;

    public FillRateReportService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<FillRateReportResponse>> GetAsync(
        FillRateReportRequest request,
        CancellationToken cancellationToken)
    {
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

        // Service tự đọc timeSlot chứ không để model binding trả 400: giá trị lạ phải nằm trong
        // errors.timeSlot theo đúng định dạng lỗi chung, và trống = không lọc (không phải lỗi).
        if (!FillRateTimeSlotCodes.TryParse(request.TimeSlot, out var slotFilter))
        {
            const string message =
                "Khung giờ không hợp lệ. Chỉ nhận early, morning, afternoon, evening, night.";

            return ServiceResult<FillRateReportResponse>.Invalid(message, new Dictionary<string, string[]>
            {
                ["timeSlot"] = [message],
            });
        }

        if (request.RouteId is { } routeId)
        {
            var routeExists = await _db.Routes.AnyAsync(r => r.Id == routeId, cancellationToken);

            if (!routeExists)
            {
                return ServiceResult<FillRateReportResponse>.NotFound("Không tìm thấy tuyến đường.");
            }
        }

        // Nửa khoảng [00:00 VN from, 00:00 VN ngày SAU to) — cùng lối PeakHoursReportService.
        var fromUtc = fromDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(-VietnamUtcOffsetHours);
        var toExclusiveUtc = toDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
            .AddHours(-VietnamUtcOffsetHours);

        var tripsQuery = _db.Trips
            .AsNoTracking()
            .Where(t => t.DepartureTime >= fromUtc && t.DepartureTime < toExclusiveUtc);

        if (request.RouteId is { } rid)
        {
            tripsQuery = tripsQuery.Where(t => t.RouteId == rid);
        }

        var trips = await tripsQuery
            .Select(t => new { t.Id, t.RouteId, t.BusId, t.DepartureTime })
            .ToListAsync(cancellationToken);

        // Lọc khung giờ trong C#: phép đổi UTC → giờ Việt Nam không dịch được bằng LINQ chung cho
        // cả Npgsql lẫn InMemory (cùng lý do GroupIntoHours của báo cáo khung giờ).
        if (slotFilter is { } slot)
        {
            trips = trips
                .Where(t => slot.Contains(ToVietnamHour(t.DepartureTime)))
                .ToList();
        }

        var tripIds = trips.Select(t => t.Id).ToList();

        var ticketCounts = await _db.Tickets
            .AsNoTracking()
            .Where(tk => tk.Status != TicketStatus.Cancelled && tripIds.Contains(tk.TripId))
            .GroupBy(tk => tk.TripId)
            .Select(g => new { TripId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var passengersByTrip = ticketCounts.ToDictionary(r => r.TripId, r => r.Count);

        var routeById = await LoadRoutesAsync(
            trips.Select(t => t.RouteId).Distinct().ToList(),
            cancellationToken);

        var busById = await LoadBusesAsync(
            trips.Select(t => t.BusId).Distinct().ToList(),
            cancellationToken);

        var rows = trips
            .Select(t => BuildTripRow(t.Id, t.RouteId, t.BusId, t.DepartureTime,
                passengersByTrip.GetValueOrDefault(t.Id), routeById, busById))
            .OrderBy(r => r.DepartureTime)
            .ThenBy(r => r.TripId)
            .ToList();

        var totalPassengers = rows.Sum(r => r.Passengers);
        var totalSeats = rows.Sum(r => r.Capacity);

        return ServiceResult<FillRateReportResponse>.Ok(new FillRateReportResponse
        {
            From = fromDate,
            To = toDate,
            Summary = new FillRateSummaryResponse
            {
                TotalTrips = rows.Count,
                TotalPassengers = totalPassengers,
                TotalSeats = totalSeats,
                AverageFillRatePct = AverageFillRate(totalPassengers, totalSeats),
                FullTrips = rows.Count(r => r.FillRatePct >= FullThresholdPct),
                EmptyTrips = rows.Count(r => r.FillRatePct < EmptyThresholdPct),
            },
            Trips = rows,
            Routes = AggregateByRoute(rows),
            TimeSlots = AggregateByTimeSlot(rows),
        });
    }

    /// <summary>
    /// Dựng một dòng chuyến. Tuyến/xe không tìm thấy chỉ xảy ra với dữ liệu mồ côi (FK Restrict
    /// đã chặn ở CSDL thật); khi đó trả chuỗi rỗng và sức chứa 0 — dòng vẫn hiện để không âm thầm
    /// rơi khỏi tổng, đúng chốt 1.
    /// </summary>
    private static TripFillRateResponse BuildTripRow(
        Guid tripId,
        Guid routeId,
        Guid busId,
        DateTime departureUtc,
        int passengers,
        IReadOnlyDictionary<Guid, RouteLookup> routeById,
        IReadOnlyDictionary<Guid, BusLookup> busById)
    {
        var route = routeById.TryGetValue(routeId, out var r) ? r : default;
        var bus = busById.TryGetValue(busId, out var b) ? b : default;

        var departure = new DateTimeOffset(
                DateTime.SpecifyKind(departureUtc, DateTimeKind.Utc))
            .ToOffset(TimeSpan.FromHours(VietnamUtcOffsetHours));

        return new TripFillRateResponse
        {
            TripId = tripId,
            TripCode = $"C{route.Code}-{departure.ToString("HHmm", CultureInfo.InvariantCulture)}",
            RouteId = routeId,
            RouteCode = route.Code ?? string.Empty,
            RouteName = route.Name ?? string.Empty,
            BusId = busId,
            BusPlate = bus.LicensePlate ?? string.Empty,
            Capacity = bus.Capacity,
            Passengers = passengers,
            DepartureTime = departure,
            FillRatePct = AverageFillRate(passengers, bus.Capacity),
        };
    }

    /// <summary>
    /// Gộp theo tuyến — mỗi dòng cùng bộ số với tóm tắt. Sắp theo mã tuyến tăng dần (Ordinal) để
    /// hai lượt gọi cùng dữ liệu trả cùng thứ tự; hoà mã thì theo khoá tuyến.
    /// </summary>
    private static List<FillRateRouteRowResponse> AggregateByRoute(IReadOnlyList<TripFillRateResponse> rows)
        => rows
            .GroupBy(r => r.RouteId)
            .Select(g =>
            {
                var passengers = g.Sum(r => r.Passengers);
                var seats = g.Sum(r => r.Capacity);
                var first = g.First();

                return new FillRateRouteRowResponse
                {
                    RouteId = g.Key,
                    RouteCode = first.RouteCode,
                    RouteName = first.RouteName,
                    TotalTrips = g.Count(),
                    TotalPassengers = passengers,
                    TotalSeats = seats,
                    AverageFillRatePct = AverageFillRate(passengers, seats),
                    FullTrips = g.Count(r => r.FillRatePct >= FullThresholdPct),
                    EmptyTrips = g.Count(r => r.FillRatePct < EmptyThresholdPct),
                };
            })
            .OrderBy(r => r.RouteCode, StringComparer.Ordinal)
            .ThenBy(r => r.RouteId)
            .ToList();

    /// <summary>
    /// Gộp theo khung giờ — LUÔN đủ năm dòng theo thứ tự cố định early → night, khung rỗng vẫn
    /// hiện với số 0 (biểu đồ cột cần đủ cột, cùng lối 24 giờ của báo cáo khung giờ cao điểm).
    /// <see cref="DateTimeOffset.Hour"/> của dòng chuyến đã là giờ Việt Nam vì dòng được dựng với
    /// offset +07:00.
    /// </summary>
    private static List<FillRateTimeSlotRowResponse> AggregateByTimeSlot(
        IReadOnlyList<TripFillRateResponse> rows)
        => Enum.GetValues<FillRateTimeSlot>()
            .Select(slot =>
            {
                var inSlot = rows.Where(r => slot.Contains(r.DepartureTime.Hour)).ToList();
                var passengers = inSlot.Sum(r => r.Passengers);
                var seats = inSlot.Sum(r => r.Capacity);

                return new FillRateTimeSlotRowResponse
                {
                    TimeSlot = slot.ToCode(),
                    FromHour = slot.FromHour(),
                    ToHour = slot.ToHour(),
                    TotalTrips = inSlot.Count,
                    TotalPassengers = passengers,
                    TotalSeats = seats,
                    AverageFillRatePct = AverageFillRate(passengers, seats),
                    FullTrips = inSlot.Count(r => r.FillRatePct >= FullThresholdPct),
                    EmptyTrips = inSlot.Count(r => r.FillRatePct < EmptyThresholdPct),
                };
            })
            .ToList();

    /// <summary>
    /// Tỷ lệ lấp đầy 1 chữ số thập phân; 0 khi không có ghế (không chia cho 0). Không chặn ở
    /// 100 — xem chốt 3 ở doc lớp.
    /// </summary>
    private static decimal AverageFillRate(int passengers, int seats)
        => seats <= 0
            ? 0m
            : Math.Round(passengers * 100m / seats, 1, MidpointRounding.AwayFromZero);

    private async Task<Dictionary<Guid, RouteLookup>> LoadRoutesAsync(
        List<Guid> routeIds,
        CancellationToken cancellationToken)
    {
        if (routeIds.Count == 0)
        {
            return new Dictionary<Guid, RouteLookup>();
        }

        return await _db.Routes
            .AsNoTracking()
            .Where(r => routeIds.Contains(r.Id))
            .Select(r => new { r.Id, r.Code, r.Name })
            .ToDictionaryAsync(r => r.Id, r => new RouteLookup(r.Code, r.Name), cancellationToken);
    }

    private async Task<Dictionary<Guid, BusLookup>> LoadBusesAsync(
        List<Guid> busIds,
        CancellationToken cancellationToken)
    {
        if (busIds.Count == 0)
        {
            return new Dictionary<Guid, BusLookup>();
        }

        return await _db.Buses
            .AsNoTracking()
            .Where(b => busIds.Contains(b.Id))
            .Select(b => new { b.Id, b.LicensePlate, b.Capacity })
            .ToDictionaryAsync(
                b => b.Id,
                b => new BusLookup(b.LicensePlate, b.Capacity),
                cancellationToken);
    }

    /// <summary>Giờ trong ngày (giờ Việt Nam) của một mốc UTC — cùng công thức PeakHoursReportService.ToVietnamHour.</summary>
    private static int ToVietnamHour(DateTime utc)
    {
        var asUtc = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);

        return asUtc.AddHours(VietnamUtcOffsetHours).Hour;
    }

    private static ServiceResult<FillRateReportResponse> InvalidRange(string message)
    {
        // Gắn lỗi vào CẢ HAI ô ngày vì cả hai đều phải đổi mới hợp lệ được — cùng lối
        // RevenueReportService.InvalidRange.
        return ServiceResult<FillRateReportResponse>.Invalid(message, new Dictionary<string, string[]>
        {
            ["from"] = [message],
            ["to"] = [message],
        });
    }

    private readonly record struct RouteLookup(string? Code, string? Name);

    private readonly record struct BusLookup(string? LicensePlate, int Capacity);
}
