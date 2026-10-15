using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Reports;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IPeakHoursReportService"/> — phân tích khung giờ cao điểm theo mật độ hành
/// khách (US 20 "Tỷ lệ lấp đầy", Sprint 4, task *"API phân tích khung giờ cao điểm theo mật độ
/// hành khách"* — Trần Trung Hiếu). Hợp đồng đầy đủ ở mục "Khung giờ cao điểm —
/// /reports/peak-hours" của docs/api-contract.md.
///
/// Sáu chốt nghiệp vụ, đọc trước khi sửa:
///
/// 1. **Nền là bảng Tickets, đếm vé Paid/Used của các chuyến khởi hành trong kỳ.** Mật độ hành
///    khách = khách thực sự đã trả tiền cho chuyến (vé đã phát hành), không phải số ghế hay số
///    chuyến. Vé Cancelled bỏ — khách đã huỷ/hoàn tiền không lên xe. Còn vé đã soát (Used) VẪN
///    tính: người đó đã đi thật, mật độ vận tải phải đếm họ.
/// 2. **Khung giờ = GIỜ KHỞI HÀNH của chuyến, theo giờ Việt Nam (UTC+7 cố định).** Báo cáo trả
///    lời "giờ nào đông khách" để nhà xe điều tần suất — trục phải là giờ chạy, không phải giờ
///    bán vé. Cố ý không dùng <c>TimeZoneInfo</c> (tên vùng khác nhau giữa Windows và Linux —
///    cùng chốt của RevenueReportService). Chuyến 00:30 giờ Việt Nam thuộc giờ 0 của NGÀY ĐÓ.
/// 3. **Mật độ = khách trung bình mỗi chuyến.** Hai giờ cùng 100 khách nhưng một giờ có 20
///    chuyến, giờ kia có 4 — chỗ đông là giờ 4 chuyến. Chia cho số chuyến khiến "cao điểm" phản
///    ánh độ chật của xe chứ không chỉ độ lớn của thời khoá biểu.
/// 4. **Cao điểm = mật độ giờ ≥ mật độ trung bình cả kỳ**, so trên giá trị ĐÃ LÀM TRÒN 2 chữ số
///    — con số hiển thị chính là con số được so sánh, người đọc phản hồi tự kiểm lại được.
///    Giờ không có chuyến (hoặc cả kỳ không có khách nào) KHÔNG BAO GIỜ là cao điểm: ngưỡng
///    trung bình bằng 0 sẽ đánh dấu nhầm toàn bộ.
/// 5. **Tổng và dãy giờ lấy từ HAI phép tính khác nhau.** Tổng là Count trong CSDL; bucket gộp
///    trong C# từ danh sách dòng thô (phép chia giờ Việt Nam không dịch được bằng LINQ chung cho
///    cả Npgsql lẫn InMemory). Đẳng thức "tổng == cộng các bucket" là phép kiểm THẬT — gộp lệch
///    (ví dụ lệch múi giờ làm rơi dòng) là test đỏ ngay (cùng lối RevenueReportService chốt 3).
/// 6. **Trục 24 giờ CỐ ĐỊNH, không bỏ giờ rỗng.** Biểu đồ cột cần đủ 24 cột — giờ không có
///    chuyến thì passengers/trips/mật độ bằng 0.
/// </summary>
public sealed class PeakHoursReportService : IPeakHoursReportService
{
    /// <summary>Bỏ trống cả from lẫn to thì lấy 30 ngày gần nhất, TÍNH CẢ hôm nay (giờ Việt Nam).</summary>
    private const int DefaultWindowDays = 30;

    /// <summary>Trần khoảng báo cáo — chặn biểu đồ phình vô nghĩa (366 ngày = trọn một năm).</summary>
    private const int MaxSpanDays = 366;

    /// <summary>Múi giờ Việt Nam — cố định +7, không DST từ 1975. Lý do không dùng TimeZoneInfo ở doc lớp.</summary>
    private const int VietnamUtcOffsetHours = 7;

    private readonly AppDbContext _db;

    public PeakHoursReportService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<PeakHoursReportResponse>> GetAsync(
        PeakHoursReportRequest request,
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

        if (request.RouteId is { } routeId)
        {
            var routeExists = await _db.Routes.AnyAsync(r => r.Id == routeId, cancellationToken);

            if (!routeExists)
            {
                return ServiceResult<PeakHoursReportResponse>.NotFound("Không tìm thấy tuyến đường.");
            }
        }

        // Nửa khoảng [00:00 VN từ from, 00:00 VN ngày SAU to) — tính trọn cả hai đầu mút mà không
        // phải sinh mốc 23:59:59.9999999 (cùng lối RevenueReportService).
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

        // Lọc tuyến thẳng trên Trips — nền của báo cáo này CHÍNH LÀ bảng chuyến, không cần hai
        // bước như RevenueReportService (bên đó nền là Payments mới phải đi vòng qua tripIds).
        var trips = await tripsQuery
            .Select(t => new { t.Id, t.DepartureTime })
            .ToListAsync(cancellationToken);

        var tripIds = trips.Select(t => t.Id).ToList();
        var totalTrips = tripIds.Count;

        var ticketsQuery = _db.Tickets
            .AsNoTracking()
            .Where(tk => tk.Status != TicketStatus.Cancelled && tripIds.Contains(tk.TripId));

        // Tổng bằng truy vấn gộp riêng — KHÔNG cộng từ bucket (xem chốt 5 ở doc lớp).
        var totalPassengers = await ticketsQuery.CountAsync(cancellationToken);

        // Bucket gộp trong C# từ dòng thô — chỉ kéo cột TripId, không nạp cả bản ghi.
        var ticketTripIds = await ticketsQuery
            .Select(tk => tk.TripId)
            .ToListAsync(cancellationToken);

        var hours = GroupIntoHours(
            trips.Select(t => (t.Id, t.DepartureTime)),
            ticketTripIds);

        var overallDensity = totalTrips == 0
            ? 0d
            : Math.Round(totalPassengers / (double)totalTrips, 2);

        return ServiceResult<PeakHoursReportResponse>.Ok(new PeakHoursReportResponse
        {
            From = fromDate,
            To = toDate,
            TotalPassengers = totalPassengers,
            TotalTrips = totalTrips,
            AveragePassengersPerTrip = overallDensity,
            PeakHours = hours.Where(h => h.IsPeak).Select(h => h.Hour).ToList(),
            Hours = hours,
        });
    }

    /// <summary>
    /// Gộp dòng thô thành đủ 24 bucket giờ (0–23) — hàm THUẦN, public để test gọi thẳng mà ghim
    /// từng biên (lệch múi giờ, giờ rỗng, ngưỡng cao điểm) như TicketPdfService.BuildQrSvg.
    ///
    /// <paramref name="trips"/> là danh sách chuyến trong kỳ; <paramref name="ticketTripIds"/>
    /// là chuyến của từng vé hợp lệ. Vé trỏ tới chuyến ngoài danh sách bị bỏ qua (không thể xảy
    /// ra khi truy vấn đã lọc theo tripIds — nhưng hàm thuần vẫn an toàn với dữ liệu lạ).
    /// <see cref="Trip.DepartureTime"/> là UTC (Kind Unspecified coi như UTC — cùng chốt của
    /// TicketPdfModel).
    /// </summary>
    public static IReadOnlyList<PeakHourBucketResponse> GroupIntoHours(
        IEnumerable<(Guid TripId, DateTime DepartureTime)> trips,
        IEnumerable<Guid> ticketTripIds)
    {
        var tripsList = trips.ToList();
        var hourByTrip = tripsList.ToDictionary(t => t.TripId, t => ToVietnamHour(t.DepartureTime));

        var tripCounts = new int[24];
        var passengers = new int[24];

        foreach (var trip in tripsList)
        {
            tripCounts[hourByTrip[trip.TripId]]++;
        }

        var passengerCount = 0;

        foreach (var ticketTripId in ticketTripIds)
        {
            if (!hourByTrip.TryGetValue(ticketTripId, out var hour))
            {
                continue;
            }

            passengers[hour]++;
            passengerCount++;
        }

        // Ngưỡng so trên giá trị ĐÃ LÀM TRÒN 2 chữ số — đúng con số hiển thị trong phản hồi (chốt 4).
        var overallDensity = tripsList.Count == 0
            ? 0d
            : Math.Round(passengerCount / (double)tripsList.Count, 2);

        var buckets = new List<PeakHourBucketResponse>(24);

        for (var hour = 0; hour < 24; hour++)
        {
            var density = tripCounts[hour] == 0
                ? 0d
                : Math.Round(passengers[hour] / (double)tripCounts[hour], 2);

            buckets.Add(new PeakHourBucketResponse
            {
                Hour = hour,
                Passengers = passengers[hour],
                Trips = tripCounts[hour],
                AveragePassengersPerTrip = density,
                IsPeak = tripCounts[hour] > 0 && passengerCount > 0 && density >= overallDensity,
            });
        }

        return buckets;
    }

    /// <summary>Giờ trong ngày (giờ Việt Nam) của một mốc UTC.</summary>
    public static int ToVietnamHour(DateTime utc)
    {
        var asUtc = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);

        return asUtc.AddHours(VietnamUtcOffsetHours).Hour;
    }

    private static ServiceResult<PeakHoursReportResponse> InvalidRange(string message)
    {
        // Gắn lỗi vào CẢ HAI ô ngày vì cả hai đều phải đổi mới hợp lệ được — cùng lối
        // RevenueReportService.InvalidRange.
        return ServiceResult<PeakHoursReportResponse>.Invalid(message, new Dictionary<string, string[]>
        {
            ["from"] = [message],
            ["to"] = [message],
        });
    }
}
