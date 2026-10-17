using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Reports;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Xuất báo cáo tỷ lệ lấp đầy ra Excel/PDF — <c>GET /api/reports/fill-rate/export</c>. US 20
/// "Tỷ lệ lấp đầy", Sprint 4 dòng 78 — Phùng Duy Hoàng.
///
/// Định nghĩa đã chốt trong docs/api-contract.md và được cài đặt DUY NHẤT ở đây:
///
/// <code>
/// tỷ lệ (%) = số vé Paid + Used của chuyến / Buses.Capacity × 100   (làm tròn 1 chữ số)
/// </code>
///
/// Bốn chốt, đọc trước khi sửa:
///
/// 1. **Mẫu số là <c>Buses.Capacity</c>**, không phải số ghế trong sơ đồ ghế. Chuyến của xe có
///    sức chứa ≤ 0 bị loại khỏi cả bảng lẫn phần tóm tắt (chia cho 0 vô nghĩa).
/// 2. **Không kẹp về 100%.** Bán vượt sức chứa là bất thường cần lộ ra, không phải làm tròn cho
///    đẹp báo cáo.
/// 3. **Hai ngưỡng 80/50 tính trên tỷ lệ đã làm tròn 1 chữ số** — cùng con số in trên dòng.
/// 4. **Không bao giờ cắt bớt file.** Vượt trần thì 400 kèm gợi ý thu hẹp; một chứng từ thiếu dòng
///    mà không ai biết còn tệ hơn không có chứng từ.
///
/// Khi Kiên làm JSON <c>GET /reports/fill-rate</c> (dòng 66), rút phép tính ở đây ra service dùng
/// chung thay vì chép công thức.
/// </summary>
public sealed class FillRateExportService : IFillRateExportService
{
    /// <summary>Trần dòng Excel — cùng lý do bộ nhớ với <see cref="AuditLogExportService.MaxRows"/>.</summary>
    public const int MaxExcelRows = 10_000;

    /// <summary>Trần dòng PDF — QuestPDF dựng cả tài liệu trong RAM, thấp hơn Excel cho an toàn.</summary>
    public const int MaxPdfRows = 2_000;

    private const int DefaultWindowDays = 30;
    private const int MaxSpanDays = 366;
    private const int VietnamUtcOffsetHours = 7;

    private const string SheetName = "Tỷ lệ lấp đầy";
    private const string DocumentTitle = "BÁO CÁO TỶ LỆ LẤP ĐẦY";
    private const string UnknownActor = "(không xác định)";
    private const int HeaderRow = 4;

    private static readonly string[] Headers =
    [
        "Mã chuyến",
        "Tuyến",
        "Giờ khởi hành (VN)",
        "Xe",
        "Sức chứa",
        "Khách",
        "Tỷ lệ lấp đầy (%)",
    ];

    /// <summary>Độ rộng cột đặt cứng — không gọi <c>AdjustToContent()</c> vì nó đo font và vỡ trên Linux.</summary>
    private static readonly double[] ColumnWidths = [14, 38, 20, 14, 12, 10, 18];

    /// <summary>
    /// Culture hiển thị: dấu phẩy thập phân, dấu chấm hàng nghìn — cách người Việt đọc báo cáo.
    /// Ô số của Excel vẫn là số thật, không đi qua culture này.
    /// </summary>
    private static readonly CultureInfo VietnameseCulture = CultureInfo.GetCultureInfo("vi-VN");

    private readonly AppDbContext _db;
    private readonly IReportPdfService _reportPdfService;
    private readonly IReportExcelService _reportExcelService;

    public FillRateExportService(
        AppDbContext db,
        IReportPdfService reportPdfService,
        IReportExcelService reportExcelService)
    {
        _db = db;
        _reportPdfService = reportPdfService;
        _reportExcelService = reportExcelService;
    }

    public async Task<ServiceResult<ReportExportFile>> ExportAsync(
        FillRateExportRequest request,
        string? exportedBy,
        CancellationToken cancellationToken)
    {
        // Kiểm hai tham số định dạng/khung giờ TRƯỚC khi chạm CSDL — tham số hỏng thì không tốn
        // truy vấn, và lỗi gắn đúng ô input.
        if (!ReportExportFormats.TryParse(request.Format, out var format))
        {
            return FormatError();
        }

        TimeSlotFilter? slot = null;

        if (!string.IsNullOrWhiteSpace(request.TimeSlot))
        {
            if (!TryParseTimeSlot(request.TimeSlot, out var parsed))
            {
                return TimeSlotError();
            }

            slot = parsed;
        }

        var todayVietnam = DateOnly.FromDateTime(
            DateTime.UtcNow.AddHours(VietnamUtcOffsetHours));

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

        // Kiểm tuyến trước khi truy vấn chuyến: tuyến không tồn tại là 404, không phải file rỗng.
        RouteInfo? selectedRoute = null;

        if (request.RouteId is { } routeId)
        {
            selectedRoute = await _db.Routes
                .AsNoTracking()
                .Where(r => r.Id == routeId)
                .Select(r => new RouteInfo(r.Code, r.Name))
                .FirstOrDefaultAsync(cancellationToken);

            if (selectedRoute is null)
            {
                return ServiceResult<ReportExportFile>.NotFound("Không tìm thấy tuyến đường.");
            }
        }

        // Nửa khoảng theo lịch Việt Nam: [from 00:00 VN, to+1 ngày 00:00 VN) quy về UTC.
        var fromUtc = fromDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
            .AddHours(-VietnamUtcOffsetHours);
        var toExclusiveUtc = toDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
            .AddHours(-VietnamUtcOffsetHours);

        var buses = await _db.Buses
            .AsNoTracking()
            .Where(b => b.Capacity > 0)
            .Select(b => new { b.Id, b.LicensePlate, b.Capacity })
            .ToDictionaryAsync(
                b => b.Id,
                b => new BusInfo(b.LicensePlate, b.Capacity),
                cancellationToken);

        var candidates = new List<CandidateTrip>();

        if (buses.Count > 0)
        {
            var busIds = buses.Keys.ToList();

            var query = _db.Trips
                .AsNoTracking()
                .Where(t => t.DepartureTime >= fromUtc
                    && t.DepartureTime < toExclusiveUtc
                    && t.Status != TripStatus.Cancelled
                    && busIds.Contains(t.BusId));

            if (request.RouteId is { } filterRouteId)
            {
                query = query.Where(t => t.RouteId == filterRouteId);
            }

            var trips = await query
                .Select(t => new ScanTrip(t.Id, t.RouteId, t.BusId, t.DepartureTime))
                .ToListAsync(cancellationToken);

            // Khung giờ lọc trong bộ nhớ: giờ Việt Nam của từng chuyến, biên dưới tính, biên trên
            // không. Lọc cả ngày ở DB chỉ đúng khi quy được về UTC cố định, còn khung 05–22h cắt
            // qua nhiều ngày — lọc sau khi nạp ít rủi ro hơn một biểu thức EF khó kiểm.
            foreach (var trip in trips)
            {
                var departureVietnam = ToVietnam(trip.DepartureTime);

                if (slot is { } wanted && !MatchesSlot(departureVietnam.Hour, wanted))
                {
                    continue;
                }

                candidates.Add(new CandidateTrip(trip, departureVietnam));
            }
        }

        var routes = await LoadRoutesAsync(
            request.RouteId, selectedRoute, candidates, cancellationToken);

        // Chuyến trỏ tới tuyến không đọc được (dữ liệu mồ côi) bị loại thay vì ghi dòng thiếu tên
        // tuyến. FK Restrict của CSDL lẽ ra không để xảy ra, nhưng file xuất không được vỡ vì dữ
        // liệu bẩn.
        candidates.RemoveAll(c => !routes.ContainsKey(c.Trip.RouteId));

        var cap = format == ReportExportFormat.Pdf ? MaxPdfRows : MaxExcelRows;

        if (candidates.Count > cap)
        {
            return TooManyRows(candidates.Count, fromDate, toDate, cap, format);
        }

        // Giờ khởi hành tăng dần; trùng giờ thì theo Id để hai lần xuất ra cùng thứ tự.
        candidates.Sort((left, right) =>
        {
            var byDeparture = left.Trip.DepartureTime.CompareTo(right.Trip.DepartureTime);

            return byDeparture != 0
                ? byDeparture
                : left.Trip.Id.CompareTo(right.Trip.Id);
        });

        var ticketCounts = await LoadTicketCountsAsync(candidates, cancellationToken);

        var rows = new List<ExportRow>(candidates.Count);

        foreach (var candidate in candidates)
        {
            var trip = candidate.Trip;
            var bus = buses[trip.BusId];
            var route = routes[trip.RouteId];
            var passengers = ticketCounts.TryGetValue(trip.Id, out var count) ? count : 0;

            // Làm tròn 1 chữ số, AwayFromZero — cùng quy tắc báo cáo doanh thu. Tỷ lệ có thể vượt
            // 100% và KHÔNG bị kẹp: giữ con số thật để bất thường lộ ra.
            var fillRate = Math.Round(
                passengers * 100m / bus.Capacity,
                1,
                MidpointRounding.AwayFromZero);

            rows.Add(new ExportRow(
                $"C{route.Code}-{candidate.DepartureVietnam:HHmm}",
                $"{route.Code} · {route.Name}",
                candidate.DepartureVietnam,
                bus.LicensePlate,
                bus.Capacity,
                passengers,
                fillRate));
        }

        var generatedAtUtc = DateTime.UtcNow;
        var routeLabel = selectedRoute is null
            ? "Tất cả"
            : $"{selectedRoute.Code} · {selectedRoute.Name}";

        var content = format == ReportExportFormat.Pdf
            ? BuildPdf(rows, fromDate, toDate, routeLabel, slot, generatedAtUtc, exportedBy)
            : BuildExcel(rows, fromDate, toDate, routeLabel, slot, generatedAtUtc, exportedBy);

        return ServiceResult<ReportExportFile>.Ok(new ReportExportFile
        {
            Content = content,
            ContentType = format.ContentType(),
            FileName = $"bao-cao-ty-le-lap-day_{generatedAtUtc:yyyyMMdd-HHmmss}Z{format.Extension()}",
        });
    }

    private async Task<Dictionary<Guid, RouteInfo>> LoadRoutesAsync(
        Guid? selectedRouteId,
        RouteInfo? selectedRoute,
        IReadOnlyList<CandidateTrip> candidates,
        CancellationToken cancellationToken)
    {
        if (selectedRouteId is { } filterRouteId && selectedRoute is not null)
        {
            return new Dictionary<Guid, RouteInfo> { [filterRouteId] = selectedRoute };
        }

        var routeIds = candidates
            .Select(c => c.Trip.RouteId)
            .Distinct()
            .ToList();

        if (routeIds.Count == 0)
        {
            return [];
        }

        return await _db.Routes
            .AsNoTracking()
            .Where(r => routeIds.Contains(r.Id))
            .Select(r => new { r.Id, r.Code, r.Name })
            .ToDictionaryAsync(
                r => r.Id,
                r => new RouteInfo(r.Code, r.Name),
                cancellationToken);
    }

    /// <summary>
    /// Đếm vé Paid + Used theo chuyến bằng MỘT truy vấn nhóm — không kéo từng vé về. Vé
    /// <c>Cancelled</c> không phải khách có chỗ trên xe nên không tính.
    /// </summary>
    private async Task<Dictionary<Guid, int>> LoadTicketCountsAsync(
        IReadOnlyList<CandidateTrip> candidates,
        CancellationToken cancellationToken)
    {
        var tripIds = candidates.Select(c => c.Trip.Id).ToList();

        if (tripIds.Count == 0)
        {
            return [];
        }

        return await _db.Tickets
            .AsNoTracking()
            .Where(t => tripIds.Contains(t.TripId)
                && (t.Status == TicketStatus.Paid || t.Status == TicketStatus.Used))
            .GroupBy(t => t.TripId)
            .Select(group => new { TripId = group.Key, Passengers = group.Count() })
            .ToDictionaryAsync(
                item => item.TripId,
                item => item.Passengers,
                cancellationToken);
    }

    /// <summary>
    /// Dàn các dòng ra khuôn Excel chung. Phép vẽ workbook nằm ở <see cref="IReportExcelService"/>;
    /// ở đây chỉ chọn kiểu từng ô để giữ đúng ngữ nghĩa của báo cáo.
    /// </summary>
    private byte[] BuildExcel(
        IReadOnlyList<ExportRow> rows,
        DateOnly fromDate,
        DateOnly toDate,
        string routeLabel,
        TimeSlotFilter? slot,
        DateTime generatedAtUtc,
        string? exportedBy)
    {
        var model = new ReportExcelModel
        {
            SheetName = SheetName,
            Title = DocumentTitle,
            Subtitle = MetadataLine(
                rows, fromDate, toDate, routeLabel, slot, generatedAtUtc, exportedBy),
            Headers = Headers,
            Rows = rows
                .Select(row => new ReportExcelRow
                {
                    Cells =
                    [
                        ReportExcelCell.Text(row.TripCode),
                        ReportExcelCell.Text(row.RouteText),

                        // Giờ khởi hành để CHUỖI theo giờ Việt Nam: gán DateTime thì Excel diễn
                        // giải lại theo múi giờ máy người mở, có thể lệch 7 tiếng so với giờ in
                        // trên vé.
                        ReportExcelCell.Text(
                            row.DepartureVietnam.ToString(
                                "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),

                        ReportExcelCell.Text(row.LicensePlate),
                        ReportExcelCell.Number(row.Capacity),
                        ReportExcelCell.Number(row.Passengers),
                        ReportExcelCell.Number(row.FillRatePct, "0.0"),
                    ],
                })
                .ToList(),
            ColumnWidths = ColumnWidths,
        };

        return _reportExcelService.GenerateExcel(model);
    }

    private byte[] BuildPdf(
        IReadOnlyList<ExportRow> rows,
        DateOnly fromDate,
        DateOnly toDate,
        string routeLabel,
        TimeSlotFilter? slot,
        DateTime generatedAtUtc,
        string? exportedBy)
    {
        var tableRows = rows
            .Select(row => (IReadOnlyList<string>)
            [
                row.TripCode,
                row.RouteText,
                row.DepartureVietnam.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                row.LicensePlate,
                row.Capacity.ToString("N0", VietnameseCulture),
                row.Passengers.ToString("N0", VietnameseCulture),
                row.FillRatePct.ToString("0.0", VietnameseCulture),
            ])
            .ToList();

        var model = new ReportPdfModel
        {
            Title = DocumentTitle,
            Subtitle = MetadataLine(
                rows, fromDate, toDate, routeLabel, slot, generatedAtUtc, exportedBy),
            ExportedAt = generatedAtUtc,
            GeneratedBy = exportedBy,

            // Kỳ không có chuyến nào vẫn là bảng rỗng hợp lệ — file phải có đầu cột để người đọc
            // thấy "không có dòng nào".
            Table = new ReportTable
            {
                Headers = Headers,
                Rows = tableRows,
            },

            FooterNote =
                "Tỷ lệ (%) = (vé Paid + Used) / sức chứa ghế của xe × 100, làm tròn 1 chữ số.",
        };

        return _reportPdfService.GeneratePdf(model);
    }

    /// <summary>
    /// Khối tóm tắt dùng chung cho hàng 2 của Excel và dòng phụ của PDF. Tỷ lệ trung bình là trung
    /// bình KHÔNG trọng số của tỷ lệ từng chuyến (đã làm tròn), 1 chữ số; không có chuyến thì 0.
    /// </summary>
    private static string MetadataLine(
        IReadOnlyList<ExportRow> rows,
        DateOnly fromDate,
        DateOnly toDate,
        string routeLabel,
        TimeSlotFilter? slot,
        DateTime generatedAtUtc,
        string? exportedBy)
    {
        var totalTrips = rows.Count;

        var averageFillRate = totalTrips == 0
            ? 0m
            : Math.Round(
                rows.Average(r => r.FillRatePct),
                1,
                MidpointRounding.AwayFromZero);

        var fullTrips = rows.Count(r => r.FillRatePct >= 80m);
        var emptyTrips = rows.Count(r => r.FillRatePct < 50m);

        return
            $"Kỳ báo cáo: {fromDate:yyyy-MM-dd} → {toDate:yyyy-MM-dd} (lịch Việt Nam) · " +
            $"Tuyến: {routeLabel} · Khung giờ: {TimeSlotLabel(slot)} · " +
            $"Tổng số chuyến: {totalTrips.ToString("N0", VietnameseCulture)} · " +
            $"Tỷ lệ trung bình: {averageFillRate.ToString("0.0", VietnameseCulture)}% · " +
            $"Chuyến đầy (≥ 80%): {fullTrips.ToString("N0", VietnameseCulture)} · " +
            $"Chuyến trống (< 50%): {emptyTrips.ToString("N0", VietnameseCulture)} · " +
            $"Xuất lúc: {generatedAtUtc:yyyy-MM-dd HH:mm:ss} UTC · " +
            $"Người xuất: {exportedBy ?? UnknownActor}";
    }

    private static ServiceResult<ReportExportFile> FormatError()
        => ServiceResult<ReportExportFile>.Invalid(
            ReportExportFormats.InvalidMessage,
            new Dictionary<string, string[]>
            {
                ["format"] = [ReportExportFormats.InvalidMessage],
            });

    private static ServiceResult<ReportExportFile> TimeSlotError()
    {
        const string message =
            "Khung giờ phải là early, morning, afternoon hoặc evening.";

        return ServiceResult<ReportExportFile>.Invalid(
            message,
            new Dictionary<string, string[]> { ["timeSlot"] = [message] });
    }

    private static ServiceResult<ReportExportFile> InvalidRange(string message)
    {
        // Gắn lỗi vào CẢ HAI ô ngày vì cả hai đều phải đổi mới hợp lệ được — cùng lối báo cáo
        // doanh thu và bản xuất nhật ký.
        return ServiceResult<ReportExportFile>.Invalid(
            message,
            new Dictionary<string, string[]> { ["from"] = [message], ["to"] = [message] });
    }

    private static ServiceResult<ReportExportFile> TooManyRows(
        int count,
        DateOnly fromDate,
        DateOnly toDate,
        int cap,
        ReportExportFormat format)
    {
        var formatLabel = format == ReportExportFormat.Pdf ? "PDF" : "Excel";

        var message =
            $"Khoảng {fromDate:yyyy-MM-dd} → {toDate:yyyy-MM-dd} có " +
            $"{count.ToString("N0", VietnameseCulture)} chuyến, vượt trần " +
            $"{cap.ToString("N0", VietnameseCulture)} dòng mỗi lần xuất file {formatLabel}. " +
            "Hãy thu hẹp khoảng thời gian, hoặc lọc thêm theo tuyến / khung giờ.";

        // Không bao giờ cắt bớt rồi xuất — file thiếu dòng mà không báo là báo cáo sai.
        return ServiceResult<ReportExportFile>.Invalid(message, new Dictionary<string, string[]>
        {
            ["from"] = [message],
            ["to"] = [message],
        });
    }

    private static DateTime ToVietnam(DateTime departureTime)
    {
        var asUtc = departureTime.Kind == DateTimeKind.Utc
            ? departureTime
            : DateTime.SpecifyKind(departureTime, DateTimeKind.Utc);

        return asUtc.AddHours(VietnamUtcOffsetHours);
    }

    /// <summary>Khung giờ theo giờ Việt Nam: biên dưới tính, biên trên KHÔNG tính.</summary>
    private static bool MatchesSlot(int vietnamHour, TimeSlotFilter slot) => slot switch
    {
        TimeSlotFilter.Early => vietnamHour is >= 5 and < 9,
        TimeSlotFilter.Morning => vietnamHour is >= 9 and < 13,
        TimeSlotFilter.Afternoon => vietnamHour is >= 13 and < 17,
        TimeSlotFilter.Evening => vietnamHour is >= 17 and < 22,
        _ => true,
    };

    private static string TimeSlotLabel(TimeSlotFilter? slot) => slot switch
    {
        TimeSlotFilter.Early => "early (05:00–09:00)",
        TimeSlotFilter.Morning => "morning (09:00–13:00)",
        TimeSlotFilter.Afternoon => "afternoon (13:00–17:00)",
        TimeSlotFilter.Evening => "evening (17:00–22:00)",
        _ => "Tất cả",
    };

    /// <summary>
    /// So khớp tên khung giờ, bỏ qua hoa/thường — cùng lý do
    /// <see cref="AuditLogExportService"/> không dùng <c>Enum.TryParse</c>: nó chấp nhận cả chuỗi
    /// số, trái quy ước A3.
    /// </summary>
    private static bool TryParseTimeSlot(string? value, out TimeSlotFilter slot)
    {
        var trimmed = value?.Trim();

        foreach (var name in Enum.GetNames<TimeSlotFilter>())
        {
            if (string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                slot = Enum.Parse<TimeSlotFilter>(name);
                return true;
            }
        }

        slot = default;
        return false;
    }

    /// <summary>Khung giờ khởi hành — tên chuỗi đọc được là giá trị trao đổi của tham số.</summary>
    private enum TimeSlotFilter
    {
        Early,
        Morning,
        Afternoon,
        Evening,
    }

    /// <summary>Chiếu tối thiểu từ bảng Trips — chỉ bốn cột cần cho báo cáo.</summary>
    private sealed record ScanTrip(Guid Id, Guid RouteId, Guid BusId, DateTime DepartureTime);

    /// <summary>Một chuyến đã qua lọc khung giờ, kèm giờ khởi hành Việt Nam đã quy đổi.</summary>
    private sealed record CandidateTrip(ScanTrip Trip, DateTime DepartureVietnam);

    private sealed record RouteInfo(string Code, string Name);

    private sealed record BusInfo(string LicensePlate, int Capacity);

    /// <summary>Một dòng dữ liệu đã tính xong tỷ lệ, sẵn sàng ghi vào file.</summary>
    private sealed record ExportRow(
        string TripCode,
        string RouteText,
        DateTime DepartureVietnam,
        string LicensePlate,
        int Capacity,
        int Passengers,
        decimal FillRatePct);
}
