using System.Globalization;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Exports;
using SmartBus.Api.Dtos.Reports;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Dựng file cho <see cref="ExportJob"/> — task *"Xử lý xuất file lớn bất đồng bộ + thông báo khi
/// xong"* (Sprint 5 dòng 64, Vàng Thị Dăm).
///
/// <para>
/// <b>Vì sao không gọi thẳng ba service xuất đồng bộ:</b> hai trong ba service đó có trần dòng
/// (10.000 Excel / 2.000 PDF) cài bằng hằng số trong file của người khác, và mục đích của job nền
/// chính là vượt trần đó. Processor dùng lại những nguồn KHÔNG có trần:
/// </para>
///
/// <list type="bullet">
/// <item><c>IRevenueExportService</c> — bản thân nó không có trần (báo cáo tối đa 366 bucket).</item>
/// <item><c>IFillRateReportService</c> — trả MỌI chuyến trong kỳ; processor chỉ dàn ra file.</item>
/// <item>Bảng <c>AuditLogs</c> đọc trực tiếp — cùng bộ lọc với bản đồng bộ, nhưng trần riêng.</item>
/// </list>
///
/// <para>
/// <b>Trần của bản bất đồng bộ: 50.000 dòng Excel / 10.000 dòng PDF.</b> Đây không phải trần "đủ
/// lớn để không ai chạm": nó là giới hạn bộ nhớ thật của ClosedXML/QuestPDF (xem
/// <c>AuditLogExportService</c> — 10.000 dòng Excel tốn 40–60 MB). Job chạy nền không giữ request
/// HTTP nên chịu được nhiều hơn, nhưng vẫn phải có trần: không có trần thì một khoảng lọc 5 năm
/// nhật ký là một tiến trình bị OOM, kéo theo cả API.
/// </para>
///
/// <para>
/// <b>Không cắt bớt file.</b> Vượt trần thì job <c>Failed</c> kèm câu gợi ý thu hẹp bộ lọc — cùng
/// chốt đã ghi ở hai bản xuất đồng bộ: một chứng từ thiếu dòng mà không ai biết còn tệ hơn không có
/// chứng từ nào.
/// </para>
/// </summary>
public sealed class ExportJobProcessor : IExportJobProcessor
{
    /// <summary>Trần dòng Excel của bản bất đồng bộ — xem doc-comment đầu file.</summary>
    public const int MaxExcelRows = 50_000;

    /// <summary>Trần dòng PDF của bản bất đồng bộ — QuestPDF dựng cả tài liệu trong RAM.</summary>
    public const int MaxPdfRows = 10_000;

    /// <summary>
    /// Số lần tối đa job nền nhận một job. App chết giữa lúc dựng file ba lần (deploy, OOM, mất
    /// điện) thì lần thứ tư chốt <c>Failed</c> thay vì thử mãi — một job độc không được phép quay
    /// vòng vô hạn trong hàng đợi.
    /// </summary>
    public const int MaxAttempts = 3;

    /// <summary>
    /// Thời gian giữ file sau khi chốt kết quả. 24 giờ đủ để người dùng qua một đêm rồi tải; lâu
    /// hơn thì bảng phình theo số người dùng × số báo cáo, trong khi giá trị của file cũ giảm nhanh
    /// (báo cáo luôn dựng lại được từ dữ liệu nghiệp vụ).
    /// </summary>
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    private const int DefaultAuditWindowDays = 30;

    private const string UnknownActor = "(không xác định)";

    private static readonly JsonSerializerOptions ParametersJsonOptions =
        new(JsonSerializerDefaults.Web);

    private static readonly CultureInfo VietnameseCulture = CultureInfo.GetCultureInfo("vi-VN");

    private readonly AppDbContext _db;
    private readonly IRevenueExportService _revenueExportService;
    private readonly IFillRateReportService _fillRateReportService;
    private readonly IReportPdfService _reportPdfService;
    private readonly IExportJobNotifier _notifier;
    private readonly ILogger<ExportJobProcessor> _logger;

    public ExportJobProcessor(
        AppDbContext db,
        IRevenueExportService revenueExportService,
        IFillRateReportService fillRateReportService,
        IReportPdfService reportPdfService,
        IExportJobNotifier notifier,
        ILogger<ExportJobProcessor> logger)
    {
        _db = db;
        _revenueExportService = revenueExportService;
        _fillRateReportService = fillRateReportService;
        _reportPdfService = reportPdfService;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var job = await _db.ExportJobs
            .Where(j => j.Status == ExportJobStatus.Pending)
            .OrderBy(j => j.CreatedAt)
            .ThenBy(j => j.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (job is null)
        {
            return false;
        }

        var now = DateTime.UtcNow;

        // Đã thử đủ số lần (các lượt trước app chết giữa chừng) — chốt thất bại thay vì nhận lần
        // nữa. Kiểm TRƯỚC khi tăng AttemptCount để con số trong CSDL luôn là "đã thử bao nhiêu lần".
        if (job.AttemptCount >= MaxAttempts)
        {
            await FailAsync(
                job,
                $"Hệ thống đã thử dựng file {MaxAttempts} lần nhưng không hoàn tất được.",
                now,
                cancellationToken);
            return true;
        }

        job.Status = ExportJobStatus.Processing;
        job.StartedAt = now;
        job.AttemptCount++;
        job.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Bắt đầu dựng {ReportType} ({Format}) cho job xuất {JobId}, lần thử {Attempt}.",
            job.ReportType,
            job.Format,
            job.Id,
            job.AttemptCount);

        try
        {
            if (!ReportExportFormats.TryParse(job.Format, out var format))
            {
                // Cột Format do chính hệ thống ghi nên hiếm khi sai; nếu sai thì đây là dữ liệu
                // hỏng, không phải lỗi người dùng — câu thông báo vẫn nói được việc phải làm.
                await FailAsync(job, "Định dạng file của yêu cầu không hợp lệ.", DateTime.UtcNow, cancellationToken);
                return true;
            }

            var parameters = JsonSerializer.Deserialize<ExportJobParameters>(
                job.ParametersJson,
                ParametersJsonOptions) ?? new ExportJobParameters();

            var built = await BuildAsync(job, parameters, format, cancellationToken);

            if (!built.Success)
            {
                await FailAsync(
                    job,
                    built.Error ?? "Không dựng được file báo cáo.",
                    DateTime.UtcNow,
                    cancellationToken);
                return true;
            }

            var file = built.Data!;
            var completedAt = DateTime.UtcNow;

            job.Status = ExportJobStatus.Succeeded;
            job.FileName = file.FileName;
            job.ContentType = file.ContentType;
            job.Content = file.Content;
            job.FileSizeBytes = file.Content.LongLength;
            job.RowCount = file.RowCount;
            job.ErrorMessage = null;
            job.CompletedAt = completedAt;
            job.ExpiresAt = completedAt + Retention;
            job.UpdatedAt = completedAt;

            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Job xuất {JobId} xong: {FileName}, {Size} byte, {Rows} dòng.",
                job.Id,
                job.FileName,
                job.FileSizeBytes,
                job.RowCount);

            // Bọc riêng: hợp đồng của notifier là không ném, nhưng nếu nó vỡ thì cũng KHÔNG được
            // rơi vào catch bên dưới — lúc đó file đã lưu Succeeded, chốt lại thành Failed là nói
            // dối về một file đang tải được.
            try
            {
                await _notifier.NotifyCompletedAsync(job);
            }
            catch (Exception notifyEx)
            {
                _logger.LogError(notifyEx, "Không báo được kết quả job xuất {JobId}.", job.Id);
            }

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // App đang tắt giữa lúc dựng file. KHÔNG chốt Failed — lần khởi động sau job được đưa
            // lại Pending (IExportJobService.RecoverStaleAsync) và làm tiếp. Ném ra để vòng lặp nền
            // thoát êm.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job xuất {JobId} thất bại khi dựng file.", job.Id);

            // Câu cho người dùng KHÔNG chứa stack trace hay tên bảng. Chi tiết kỹ thuật đã ở log.
            await FailAsync(
                job,
                "Hệ thống gặp lỗi khi dựng file. Vui lòng thử lại sau ít phút.",
                DateTime.UtcNow,
                cancellationToken);
            return true;
        }
    }

    /// <summary>
    /// Chốt job <c>Failed</c>, đặt hạn dọn và báo người dùng. Job thất bại vẫn có <c>ExpiresAt</c>
    /// để thông báo lỗi không nằm lại vĩnh viễn trong CSDL.
    /// </summary>
    private async Task FailAsync(
        ExportJob job,
        string reason,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var message = Truncate(reason);

        job.Status = ExportJobStatus.Failed;
        job.ErrorMessage = message;
        job.Content = null;
        job.FileSizeBytes = null;
        job.RowCount = null;
        job.CompletedAt = now;
        job.ExpiresAt = now + Retention;
        job.UpdatedAt = now;

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogWarning("Job xuất {JobId} thất bại: {Reason}", job.Id, message);

        await _notifier.NotifyFailedAsync(job, message);
    }

    /// <summary>Chạm trần 1.000 ký tự của cột <c>ErrorMessage</c> — xem <see cref="ExportJob.ErrorMessage"/>.</summary>
    private static string Truncate(string message)
    {
        var trimmed = message.Trim();

        return trimmed.Length <= 1_000 ? trimmed : trimmed[..997] + "...";
    }

    // ───────────────────────────── Điều phối theo loại báo cáo ─────────────────────────────

    private Task<ServiceResult<BuiltFile>> BuildAsync(
        ExportJob job,
        ExportJobParameters parameters,
        ReportExportFormat format,
        CancellationToken cancellationToken) => job.ReportType switch
        {
            ExportReportType.Revenue => BuildRevenueAsync(job, parameters, format, cancellationToken),
            ExportReportType.FillRate => BuildFillRateAsync(job, parameters, format, cancellationToken),
            ExportReportType.AuditLogs => BuildAuditLogsAsync(job, parameters, format, cancellationToken),
            _ => Task.FromResult(ServiceResult<BuiltFile>.Invalid(
                "Loại báo cáo của yêu cầu không hợp lệ.",
                new Dictionary<string, string[]>())),
        };

    /// <summary>
    /// Báo cáo doanh thu: gọi lại đúng service của endpoint đồng bộ. Service đó không có trần dòng
    /// và đã có đệm 60 giây, nên file bất đồng bộ khớp số liệu endpoint JSON — không có định nghĩa
    /// doanh thu thứ hai.
    /// </summary>
    private async Task<ServiceResult<BuiltFile>> BuildRevenueAsync(
        ExportJob job,
        ExportJobParameters parameters,
        ReportExportFormat format,
        CancellationToken cancellationToken)
    {
        var result = await _revenueExportService.ExportAsync(
            new RevenueExportRequest
            {
                Format = job.Format,
                Granularity = parameters.Granularity,
                From = parameters.From,
                To = parameters.To,
                RouteId = parameters.RouteId,
            },
            job.RequestedByName,
            cancellationToken);

        if (!result.Success)
        {
            return ConvertFailure(result);
        }

        var file = result.Data!;

        return ServiceResult<BuiltFile>.Ok(new BuiltFile
        {
            Content = file.Content,
            FileName = file.FileName,
            ContentType = file.ContentType,

            // Báo cáo doanh thu tối đa 366 bucket — con số dòng không có giá trị vận hành, và
            // service gốc cũng không trả nó. Để null thay vì đoán.
            RowCount = null,
        });
    }

    /// <summary>
    /// Báo cáo lấp đầy: lấy TOÀN BỘ chuyến từ <see cref="IFillRateReportService"/> (không trần) rồi
    /// dàn ra file. Công thức tỷ lệ nằm ở service báo cáo — processor không tính lại.
    /// </summary>
    private async Task<ServiceResult<BuiltFile>> BuildFillRateAsync(
        ExportJob job,
        ExportJobParameters parameters,
        ReportExportFormat format,
        CancellationToken cancellationToken)
    {
        var result = await _fillRateReportService.GetAsync(
            new FillRateReportRequest
            {
                From = parameters.From,
                To = parameters.To,
                RouteId = parameters.RouteId,
                TimeSlot = parameters.TimeSlot,
            },
            cancellationToken);

        if (!result.Success)
        {
            return ConvertFailure(result);
        }

        var report = result.Data!;
        var cap = format == ReportExportFormat.Pdf ? MaxPdfRows : MaxExcelRows;

        if (report.Trips.Count > cap)
        {
            var formatLabel = format == ReportExportFormat.Pdf ? "PDF" : "Excel";

            return ServiceResult<BuiltFile>.Invalid(
                $"Khoảng {report.From:yyyy-MM-dd} → {report.To:yyyy-MM-dd} có "
                + $"{report.Trips.Count.ToString("N0", VietnameseCulture)} chuyến, vượt trần "
                + $"{cap.ToString("N0", VietnameseCulture)} dòng của file {formatLabel} xuất ở nền. "
                + "Hãy thu hẹp khoảng thời gian, hoặc lọc thêm theo tuyến / khung giờ.",
                new Dictionary<string, string[]>());
        }

        var generatedAtUtc = DateTime.UtcNow;
        var routeLabel = ResolveRouteLabel(parameters.RouteId, report);
        var slotLabel = TimeSlotLabel(parameters.TimeSlot);

        var content = format == ReportExportFormat.Pdf
            ? BuildFillRatePdf(report, routeLabel, slotLabel, generatedAtUtc, job.RequestedByName)
            : BuildFillRateWorkbook(report, routeLabel, slotLabel, generatedAtUtc, job.RequestedByName);

        return ServiceResult<BuiltFile>.Ok(new BuiltFile
        {
            Content = content,
            FileName = BuildFileName(job.ReportType, generatedAtUtc, format),
            ContentType = format.ContentType(),
            RowCount = report.Trips.Count,
        });
    }

    /// <summary>
    /// Nhật ký kiểm toán: đọc thẳng bảng, cùng bộ lọc với bản đồng bộ. Cố ý không gọi
    /// <c>AuditLogExportService</c> vì trần 10.000/2.000 của nó nằm trong file của người khác và
    /// chính là thứ job nền cần vượt.
    /// </summary>
    private async Task<ServiceResult<BuiltFile>> BuildAuditLogsAsync(
        ExportJob job,
        ExportJobParameters parameters,
        ReportExportFormat format,
        CancellationToken cancellationToken)
    {
        var todayUtc = DateOnly.FromDateTime(DateTime.UtcNow);
        var fromDate = parameters.From ?? todayUtc.AddDays(-(DefaultAuditWindowDays - 1));
        var toDate = parameters.To ?? todayUtc;

        if (toDate < fromDate)
        {
            return ServiceResult<BuiltFile>.Invalid(
                "Ngày kết thúc phải bằng hoặc sau ngày bắt đầu",
                new Dictionary<string, string[]>());
        }

        var query = BuildAuditQuery(parameters, fromDate, toDate);
        var cap = format == ReportExportFormat.Pdf ? MaxPdfRows : MaxExcelRows;

        var total = await query.CountAsync(cancellationToken);

        if (total > cap)
        {
            var formatLabel = format == ReportExportFormat.Pdf ? "PDF" : "Excel";

            return ServiceResult<BuiltFile>.Invalid(
                $"Khoảng {fromDate:yyyy-MM-dd} → {toDate:yyyy-MM-dd} có "
                + $"{total.ToString("N0", VietnameseCulture)} bản ghi, vượt trần "
                + $"{cap.ToString("N0", VietnameseCulture)} dòng của file {formatLabel} xuất ở nền. "
                + "Hãy thu hẹp khoảng thời gian, hoặc lọc thêm theo người thao tác / hành động.",
                new Dictionary<string, string[]>());
        }

        // count đã ≤ trần (50.000), nên nạp MỘT lần thay vì phân trang: vòng lặp phân trang chỉ
        // thêm phức tạp mà không giảm đỉnh bộ nhớ, vì workbook/PDF vẫn phải giữ đủ dòng.
        var raw = await query
            .OrderByDescending(a => a.CreatedAt)
            .ThenBy(a => a.Id)
            .Select(a => new
            {
                a.CreatedAt,
                a.Action,
                a.Target,
                a.IpAddress,
                a.UserId,
            })
            .ToListAsync(cancellationToken);

        // Chặn đua với middleware ghi nhật ký: số dòng có thể tăng giữa CountAsync và ToListAsync.
        if (raw.Count > cap)
        {
            var formatLabel = format == ReportExportFormat.Pdf ? "PDF" : "Excel";

            return ServiceResult<BuiltFile>.Invalid(
                $"Số bản ghi vừa tăng vượt trần {cap.ToString("N0", VietnameseCulture)} dòng của file "
                + $"{formatLabel} trong lúc dựng. Hãy thử lại với khoảng thời gian hẹp hơn.",
                new Dictionary<string, string[]>());
        }

        var actors = await LoadActorsAsync(raw.Select(r => r.UserId), cancellationToken);

        var rows = new List<AuditRow>(raw.Count);

        foreach (var item in raw)
        {
            Actor? actor = item.UserId is { } userId && actors.TryGetValue(userId, out var found)
                ? found
                : null;

            rows.Add(new AuditRow(
                item.CreatedAt,
                item.Action,
                item.Target,
                item.IpAddress,
                item.UserId,
                actor?.FullName,
                actor?.PhoneNumber));
        }

        var generatedAt = DateTime.UtcNow;
        var content = format == ReportExportFormat.Pdf
            ? BuildAuditPdf(rows, fromDate, toDate, generatedAt, job.RequestedByName)
            : BuildAuditWorkbook(rows, fromDate, toDate, generatedAt, job.RequestedByName);

        return ServiceResult<BuiltFile>.Ok(new BuiltFile
        {
            Content = content,
            FileName = BuildFileName(job.ReportType, generatedAt, format),
            ContentType = format.ContentType(),
            RowCount = rows.Count,
        });
    }

    /// <summary>
    /// Chuyển lỗi của báo cáo gốc sang kết quả của processor — giữ nguyên thông báo để người dùng
    /// thấy đúng câu mà endpoint đồng bộ sẽ trả (ví dụ "tuyến không tồn tại", "quá 366 ngày").
    /// </summary>
    private static ServiceResult<BuiltFile> ConvertFailure<T>(ServiceResult<T> source)
    {
        var message = source.Error ?? "Không dựng được file báo cáo.";

        return source.ErrorKind switch
        {
            ServiceErrorKind.NotFound => ServiceResult<BuiltFile>.NotFound(message),
            ServiceErrorKind.Conflict => ServiceResult<BuiltFile>.Conflict(message),
            _ => ServiceResult<BuiltFile>.Invalid(message, source.Errors ?? new Dictionary<string, string[]>()),
        };
    }

    private IQueryable<AuditLog> BuildAuditQuery(
        ExportJobParameters parameters,
        DateOnly fromDate,
        DateOnly toDate)
    {
        // Nửa khoảng [from 00:00Z, to+1 ngày 00:00Z) — cùng định nghĩa với bản xuất đồng bộ, nhờ
        // vậy hai bản xuất cùng bộ lọc không lệch nhau một giây ở đầu mút.
        var fromUtc = fromDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toExclusiveUtc = toDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var query = _db.AuditLogs
            .AsNoTracking()
            .Where(a => a.CreatedAt >= fromUtc && a.CreatedAt < toExclusiveUtc);

        if (parameters.AuditUserId is { } userId)
        {
            query = query.Where(a => a.UserId == userId);
        }

        if (!string.IsNullOrWhiteSpace(parameters.AuditAction))
        {
            // Mã lạ cho ra file rỗng, không phải lỗi — cùng lối bản đồng bộ.
            query = TryParseAuditAction(parameters.AuditAction, out var action)
                ? query.Where(a => a.Action == action)
                : query.Where(_ => false);
        }

        return query;
    }

    private async Task<Dictionary<Guid, Actor>> LoadActorsAsync(
        IEnumerable<Guid?> userIds,
        CancellationToken cancellationToken)
    {
        var ids = userIds
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
        {
            return [];
        }

        return await _db.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.PhoneNumber })
            .ToDictionaryAsync(
                u => u.Id,
                u => new Actor(u.FullName, u.PhoneNumber),
                cancellationToken);
    }

    private static bool TryParseAuditAction(string? value, out AuditAction action)
    {
        var trimmed = value?.Trim();

        foreach (var name in Enum.GetNames<AuditAction>())
        {
            if (string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                action = Enum.Parse<AuditAction>(name);
                return true;
            }
        }

        action = default;
        return false;
    }

    /// <summary>Tên file ASCII không dấu, cùng mốc thời gian với khối tự mô tả trong file.</summary>
    private static string BuildFileName(
        ExportReportType reportType,
        DateTime generatedAtUtc,
        ReportExportFormat format)
        => $"{reportType.FileStem()}_{generatedAtUtc:yyyyMMdd-HHmmss}Z{format.Extension()}";

    // ────────────────────────────────── Báo cáo lấp đầy ──────────────────────────────────

    private const string FillRateSheetName = "Tỷ lệ lấp đầy";

    private const string FillRateTitle = "BÁO CÁO TỶ LỆ LẤP ĐẦY";

    private static readonly string[] FillRateHeaders =
    [
        "Mã chuyến",
        "Tuyến",
        "Giờ khởi hành (VN)",
        "Xe",
        "Sức chứa",
        "Khách",
        "Tỷ lệ lấp đầy (%)",
    ];

    private static readonly double[] FillRateColumnWidths = [14, 38, 20, 14, 12, 10, 18];

    private byte[] BuildFillRateWorkbook(
        FillRateReportResponse report,
        string routeLabel,
        string slotLabel,
        DateTime generatedAtUtc,
        string? exportedBy)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(FillRateSheetName);

        WriteReportHeading(
            sheet,
            FillRateTitle,
            FillRateMetadata(report, routeLabel, slotLabel, generatedAtUtc, exportedBy),
            FillRateHeaders);

        var rowIndex = HeaderRow + 1;

        foreach (var trip in report.Trips)
        {
            sheet.Cell(rowIndex, 1).Value = trip.TripCode;
            sheet.Cell(rowIndex, 2).Value = $"{trip.RouteCode} · {trip.RouteName}";

            // Chuỗi theo giờ Việt Nam: gán DateTime thì Excel diễn giải lại theo múi giờ máy mở.
            sheet.Cell(rowIndex, 3).Value = trip.DepartureTime
                .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

            sheet.Cell(rowIndex, 4).Value = trip.BusPlate;

            var capacityCell = sheet.Cell(rowIndex, 5);
            capacityCell.Value = trip.Capacity;
            capacityCell.Style.NumberFormat.Format = "#,##0";

            var passengersCell = sheet.Cell(rowIndex, 6);
            passengersCell.Value = trip.Passengers;
            passengersCell.Style.NumberFormat.Format = "#,##0";

            var fillRateCell = sheet.Cell(rowIndex, 7);
            fillRateCell.Value = trip.FillRatePct;
            fillRateCell.Style.NumberFormat.Format = "0.0";

            rowIndex++;
        }

        FinishSheet(sheet, FillRateHeaders.Length, FillRateColumnWidths);

        return SaveWorkbook(workbook);
    }

    private byte[] BuildFillRatePdf(
        FillRateReportResponse report,
        string routeLabel,
        string slotLabel,
        DateTime generatedAtUtc,
        string? exportedBy)
    {
        var rows = report.Trips
            .Select(trip => (IReadOnlyList<string>)
            [
                trip.TripCode,
                $"{trip.RouteCode} · {trip.RouteName}",
                trip.DepartureTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                trip.BusPlate,
                trip.Capacity.ToString("N0", VietnameseCulture),
                trip.Passengers.ToString("N0", VietnameseCulture),
                trip.FillRatePct.ToString("0.0", VietnameseCulture),
            ])
            .ToList();

        return _reportPdfService.GeneratePdf(new ReportPdfModel
        {
            Title = FillRateTitle,
            Subtitle = FillRateMetadata(report, routeLabel, slotLabel, generatedAtUtc, exportedBy),
            ExportedAt = generatedAtUtc,
            GeneratedBy = exportedBy,
            Table = new ReportTable
            {
                Headers = FillRateHeaders,
                Rows = rows,
            },
            FooterNote =
                "Tỷ lệ (%) = (vé Paid + Used) / sức chứa ghế của xe × 100, làm tròn 1 chữ số.",
        });
    }

    private static string FillRateMetadata(
        FillRateReportResponse report,
        string routeLabel,
        string slotLabel,
        DateTime generatedAtUtc,
        string? exportedBy)
    {
        var summary = report.Summary;

        return
            $"Kỳ báo cáo: {report.From:yyyy-MM-dd} → {report.To:yyyy-MM-dd} (lịch Việt Nam) · "
            + $"Tuyến: {routeLabel} · Khung giờ: {slotLabel} · "
            + $"Tổng số chuyến: {summary.TotalTrips.ToString("N0", VietnameseCulture)} · "
            + $"Tỷ lệ trung bình: {summary.AverageFillRatePct.ToString("0.0", VietnameseCulture)}% · "
            + $"Chuyến đầy (≥ 80%): {summary.FullTrips.ToString("N0", VietnameseCulture)} · "
            + $"Chuyến trống (< 50%): {summary.EmptyTrips.ToString("N0", VietnameseCulture)} · "
            + $"Xuất lúc: {generatedAtUtc:yyyy-MM-dd HH:mm:ss} UTC · "
            + $"Người xuất: {exportedBy ?? UnknownActor}";
    }

    private static string ResolveRouteLabel(Guid? routeId, FillRateReportResponse report)
    {
        if (routeId is not { } selected)
        {
            return "Tất cả";
        }

        var route = report.Routes.FirstOrDefault(r => r.RouteId == selected);

        return route is null ? "Tất cả" : $"{route.RouteCode} · {route.RouteName}";
    }

    private static string TimeSlotLabel(string? timeSlot)
    {
        if (!FillRateTimeSlotCodes.TryParse(timeSlot, out var slot) || slot is null)
        {
            return "Tất cả";
        }

        return $"{slot.Value.ToCode()} ({slot.Value.FromHour():00}:00–{slot.Value.ToHour():00}:00)";
    }

    // ─────────────────────────────── Nhật ký kiểm toán ───────────────────────────────

    private const string AuditSheetName = "Nhật ký kiểm toán";

    private const string AuditTitle = "NHẬT KÝ TRUY CẬP VÀ THAO TÁC HỆ THỐNG";

    private static readonly string[] AuditHeaders =
    [
        "Thời gian (UTC)",
        "Người thao tác",
        "Số điện thoại",
        "Hành động",
        "Đối tượng",
        "Địa chỉ IP",
        "Mã người dùng",
    ];

    private static readonly double[] AuditColumnWidths = [22, 24, 16, 12, 30, 16, 38];

    private static byte[] BuildAuditWorkbook(
        IReadOnlyList<AuditRow> rows,
        DateOnly fromDate,
        DateOnly toDate,
        DateTime generatedAtUtc,
        string? exportedBy)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(AuditSheetName);

        WriteReportHeading(
            sheet,
            AuditTitle,
            AuditMetadata(rows.Count, fromDate, toDate, generatedAtUtc, exportedBy),
            AuditHeaders);

        var rowIndex = HeaderRow + 1;

        foreach (var row in rows)
        {
            // Ô thời gian gán DateTime thật để lọc/sắp xếp được; các cột còn lại để text để Excel
            // không diễn giải số điện thoại thành số.
            var timeCell = sheet.Cell(rowIndex, 1);
            timeCell.Value = row.CreatedAt;
            timeCell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";

            sheet.Cell(rowIndex, 2).Value = row.UserFullName ?? UnknownActor;
            sheet.Cell(rowIndex, 3).Value = row.UserPhoneNumber ?? string.Empty;
            sheet.Cell(rowIndex, 4).Value = row.Action.ToString();
            sheet.Cell(rowIndex, 5).Value = row.Target ?? string.Empty;
            sheet.Cell(rowIndex, 6).Value = row.IpAddress ?? string.Empty;
            sheet.Cell(rowIndex, 7).Value = row.UserId?.ToString() ?? string.Empty;

            rowIndex++;
        }

        FinishSheet(sheet, AuditHeaders.Length, AuditColumnWidths);

        return SaveWorkbook(workbook);
    }

    private byte[] BuildAuditPdf(
        IReadOnlyList<AuditRow> rows,
        DateOnly fromDate,
        DateOnly toDate,
        DateTime generatedAtUtc,
        string? exportedBy)
    {
        var tableRows = rows
            .Select(row => (IReadOnlyList<string>)
            [
                row.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                row.UserFullName ?? UnknownActor,
                row.UserPhoneNumber ?? string.Empty,
                row.Action.ToString(),
                row.Target ?? string.Empty,
                row.IpAddress ?? string.Empty,
                row.UserId?.ToString() ?? string.Empty,
            ])
            .ToList();

        return _reportPdfService.GeneratePdf(new ReportPdfModel
        {
            Title = AuditTitle,
            Subtitle = AuditMetadata(rows.Count, fromDate, toDate, generatedAtUtc, exportedBy),
            ExportedAt = generatedAtUtc,
            GeneratedBy = exportedBy,
            Table = new ReportTable
            {
                Headers = AuditHeaders,
                Rows = tableRows,
            },
            FooterNote = "Thời gian hiển thị theo UTC, trọn ngày; bản ghi mới nhất ở trên.",
        });
    }

    private static string AuditMetadata(
        int rowCount,
        DateOnly fromDate,
        DateOnly toDate,
        DateTime generatedAtUtc,
        string? exportedBy)
        => $"Khoảng thời gian: {fromDate:yyyy-MM-dd} → {toDate:yyyy-MM-dd} (UTC, trọn ngày) · "
            + $"Số dòng: {rowCount.ToString("N0", VietnameseCulture)} · "
            + $"Xuất lúc: {generatedAtUtc:yyyy-MM-dd HH:mm:ss} UTC · "
            + $"Người xuất: {exportedBy ?? UnknownActor}";

    // ─────────────────────────────── Tiện ích ghi Excel ───────────────────────────────

    /// <summary>Hàng 1 tiêu đề, hàng 2 khối tự mô tả, hàng 3 trống, hàng 4 tiêu đề cột.</summary>
    private const int HeaderRow = 4;

    private static void WriteReportHeading(
        IXLWorksheet sheet,
        string title,
        string metadata,
        IReadOnlyList<string> headers)
    {
        var titleCell = sheet.Cell(1, 1);
        titleCell.Value = title;
        titleCell.Style.Font.Bold = true;
        titleCell.Style.Font.FontSize = 14;

        sheet.Cell(2, 1).Value = metadata;

        for (var i = 0; i < headers.Count; i++)
        {
            var cell = sheet.Cell(HeaderRow, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
        }
    }

    /// <summary>
    /// Độ rộng đặt cứng — không gọi <c>AdjustToContent()</c>: nó đo font (đúng chỗ sinh lỗi trên
    /// Linux) và chậm trên sheet 50.000 dòng.
    /// </summary>
    private static void FinishSheet(IXLWorksheet sheet, int columnCount, IReadOnlyList<double> widths)
    {
        for (var i = 0; i < columnCount; i++)
        {
            sheet.Column(i + 1).Width = widths[i];
        }

        sheet.Range(HeaderRow, 1, HeaderRow, columnCount).SetAutoFilter();
        sheet.SheetView.FreezeRows(HeaderRow);
    }

    private static byte[] SaveWorkbook(XLWorkbook workbook)
    {
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>Người thao tác — chỉ hai trường hiển thị, không kéo theo <c>PasswordHash</c>.</summary>
    private sealed record Actor(string FullName, string PhoneNumber);

    /// <summary>Một dòng nhật ký đã ghép xong người thao tác.</summary>
    private sealed record AuditRow(
        DateTime CreatedAt,
        AuditAction Action,
        string? Target,
        string? IpAddress,
        Guid? UserId,
        string? UserFullName,
        string? UserPhoneNumber);

    /// <summary>Kết quả dựng file — đủ để điền cột của <see cref="ExportJob"/>.</summary>
    private sealed record BuiltFile
    {
        public required byte[] Content { get; init; }

        public required string FileName { get; init; }

        public required string ContentType { get; init; }

        public int? RowCount { get; init; }
    }
}
