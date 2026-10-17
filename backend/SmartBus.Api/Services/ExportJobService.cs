using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Exports;
using SmartBus.Api.Dtos.Reports;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Đặt và đọc yêu cầu xuất báo cáo bất đồng bộ — task *"Xử lý xuất file lớn bất đồng bộ + thông báo
/// khi xong"* (Sprint 5 dòng 64, Vàng Thị Dăm). Xem <see cref="IExportJobService"/> cho hợp đồng.
///
/// <para>
/// Service này chỉ GHI YÊU CẦU và ĐỌC TRẠNG THÁI. Nó cố ý không dựng file: dựng file là việc của
/// <see cref="IExportJobProcessor"/> trong job nền. Nhờ vậy thời gian phản hồi của
/// <c>POST /api/exports</c> không phụ thuộc kích thước báo cáo — đúng lý do tính năng này tồn tại.
/// </para>
///
/// <para>
/// <b>Kiểm tham số ở đây hay ở processor?</b> Ở đây, với những lỗi phát hiện được mà KHÔNG cần dựng
/// file: sai định dạng, khoảng ngày ngược, quá 366 ngày, khung giờ/độ gộp lạ, tuyến không tồn tại.
/// Đẩy một job chắc chắn hỏng vào hàng đợi nghĩa là bắt người dùng chờ vài phút để nhận một thông
/// báo lỗi mà lẽ ra họ thấy ngay. Ngược lại, lỗi chỉ biết khi đọc dữ liệu (dữ liệu mồ côi, CSDL
/// đứt) vẫn phải rơi vào nhánh <c>Failed</c> của processor — vì lúc đặt job chưa có gì để biết.
/// </para>
/// </summary>
public sealed class ExportJobService : IExportJobService
{
    /// <summary>
    /// Số job đang chờ/đang chạy tối đa của MỘT người. Ba job đủ cho thao tác thật (bấm nhầm định
    /// dạng rồi đặt lại, xuất hai báo cáo khác nhau), còn mười job là một người chiếm hết CPU/RAM
    /// của job nền — vốn chỉ chạy một lượt tại một thời điểm.
    /// </summary>
    public const int MaxActiveJobsPerUser = 3;

    /// <summary>
    /// Cùng trần 366 ngày với hai báo cáo gốc. Nhật ký kiểm toán không có trần này (bản đồng bộ
    /// cũng không có) — nó được chặn bằng số dòng thay vì số ngày.
    /// </summary>
    private const int MaxSpanDays = 366;

    /// <summary>Giữ đúng cách hiển thị của ba bản xuất đồng bộ khi claim tên bị thiếu.</summary>
    private const string UnknownActor = "(không xác định)";

    /// <summary>
    /// Bỏ trường null khi ghi ảnh chụp: <see cref="ExportJobParameters"/> có đủ trường của cả ba
    /// loại báo cáo, nên để mặc định thì mỗi job mang theo vài khoá <c>null</c> không thuộc loại của
    /// nó. Bỏ null làm dòng JSON đọc lên đúng là "job này dùng bộ lọc nào", không phải "và các
    /// trường này không dùng".
    /// </summary>
    private static readonly JsonSerializerOptions ParametersJsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

    private readonly AppDbContext _db;

    public ExportJobService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<ExportJobResponse>> EnqueueAsync(
        CreateExportJobRequest request,
        Guid userId,
        string? userName,
        CancellationToken cancellationToken)
    {
        if (!ExportReportTypes.TryParse(request.ReportType, out var reportType))
        {
            return FieldError("reportType", ExportReportTypes.InvalidMessage);
        }

        if (!ReportExportFormats.TryParse(request.Format, out var format))
        {
            return FieldError("format", ReportExportFormats.InvalidMessage);
        }

        // Kiểm ngày/khoảng ngày TRƯỚC khi chạm CSDL. Chỉ kiểm khi client đã gửi đủ hai đầu: thiếu
        // một đầu thì mặc định của báo cáo gốc quyết định, và chép lại mặc định ở đây là tạo nguồn
        // sự thật thứ hai dễ lệch.
        if (request.From is { } from && request.To is { } to)
        {
            if (to < from)
            {
                return RangeError("Ngày kết thúc phải bằng hoặc sau ngày bắt đầu");
            }

            if (reportType != ExportReportType.AuditLogs
                && to.DayNumber - from.DayNumber + 1 > MaxSpanDays)
            {
                return RangeError($"Khoảng thời gian tối đa {MaxSpanDays} ngày.");
            }
        }

        switch (reportType)
        {
            case ExportReportType.Revenue:
                if (!RevenueGranularityCodes.TryParse(request.Granularity, out _))
                {
                    return FieldError(
                        "granularity",
                        "Độ gộp phải là day, week hoặc month.");
                }

                break;

            case ExportReportType.FillRate:
                if (!FillRateTimeSlotCodes.TryParse(request.TimeSlot, out _))
                {
                    return FieldError(
                        "timeSlot",
                        "Khung giờ không hợp lệ. Chỉ nhận early, morning, afternoon, evening, night.");
                }

                break;
        }

        // Tuyến không tồn tại là 404 ngay, không phải một job chạy xong với file rỗng — cùng lối
        // hai bản xuất đồng bộ.
        if (reportType is ExportReportType.Revenue or ExportReportType.FillRate
            && request.RouteId is { } routeId)
        {
            var routeExists = await _db.Routes
                .AsNoTracking()
                .AnyAsync(r => r.Id == routeId, cancellationToken);

            if (!routeExists)
            {
                return ServiceResult<ExportJobResponse>.NotFound("Không tìm thấy tuyến đường.");
            }
        }

        // Chặn người dùng bấm nút nhiều lần / mở nhiều tab. Đếm Pending + Processing, KHÔNG đếm
        // Succeeded/Failed: hai trạng thái sau không còn chiếm job nền nữa.
        var activeJobs = await _db.ExportJobs
            .AsNoTracking()
            .CountAsync(
                j => j.UserId == userId
                    && (j.Status == ExportJobStatus.Pending || j.Status == ExportJobStatus.Processing),
                cancellationToken);

        if (activeJobs >= MaxActiveJobsPerUser)
        {
            return ServiceResult<ExportJobResponse>.Conflict(
                $"Bạn đang có {activeJobs} yêu cầu xuất file chưa xong. "
                + "Hãy chờ một yêu cầu hoàn tất rồi đặt tiếp.");
        }

        var now = DateTime.UtcNow;

        var job = new ExportJob
        {
            UserId = userId,
            RequestedByName = string.IsNullOrWhiteSpace(userName) ? UnknownActor : userName.Trim(),
            ReportType = reportType,
            Format = format == ReportExportFormat.Pdf ? "pdf" : "xlsx",
            Status = ExportJobStatus.Pending,
            ParametersJson = JsonSerializer.Serialize(SnapshotParameters(request, reportType), ParametersJsonOptions),
            CreatedAt = now,
        };

        _db.ExportJobs.Add(job);
        await _db.SaveChangesAsync(cancellationToken);

        return ServiceResult<ExportJobResponse>.Ok(ToResponse(job, now));
    }

    public async Task<ServiceResult<ExportJobResponse>> GetAsync(
        Guid jobId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var job = await _db.ExportJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == jobId && j.UserId == userId, cancellationToken);

        // Job của người khác và job không tồn tại trả cùng một câu: phân biệt hai ca là tiết lộ
        // rằng một id nào đó có thật trong hệ thống.
        if (job is null)
        {
            return ServiceResult<ExportJobResponse>.NotFound("Không tìm thấy yêu cầu xuất file.");
        }

        var now = DateTime.UtcNow;

        // Job đã quá hạn nhưng lượt dọn chưa chạy (nhịp dọn không phải tức thời) — coi như đã mất.
        // Trả về một job mà endpoint tải chắc chắn 404 là hứa một thứ không lấy được.
        if (IsExpired(job, now))
        {
            return ServiceResult<ExportJobResponse>.NotFound("Không tìm thấy yêu cầu xuất file.");
        }

        return ServiceResult<ExportJobResponse>.Ok(ToResponse(job, now));
    }

    public async Task<ServiceResult<ReportExportFile>> GetFileAsync(
        Guid jobId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        // Chiếu đủ cột cần cho file, TRỪ Content khi chưa chắc tải được: kiểm trạng thái trước rồi
        // mới nạp vài chục MB của cột bytea.
        var job = await _db.ExportJobs
            .AsNoTracking()
            .Where(j => j.Id == jobId && j.UserId == userId)
            .Select(j => new
            {
                j.Status,
                j.FileName,
                j.ContentType,
                j.Content,
                j.ExpiresAt,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (job is null)
        {
            return ServiceResult<ReportExportFile>.NotFound("Không tìm thấy yêu cầu xuất file.");
        }

        var now = DateTime.UtcNow;

        if (job.ExpiresAt is { } expiresAt && expiresAt <= now)
        {
            return ServiceResult<ReportExportFile>.NotFound("Không tìm thấy yêu cầu xuất file.");
        }

        if (job.Status != ExportJobStatus.Succeeded)
        {
            // 409 chứ không 404: job có thật, chỉ là chưa có file. Câu tách hai trạng thái để
            // frontend biết nên tiếp tục hỏi lại hay dừng và hiển thị lỗi.
            return job.Status == ExportJobStatus.Failed
                ? ServiceResult<ReportExportFile>.Conflict(
                    "Yêu cầu xuất file đã thất bại. Xem lý do ở trạng thái yêu cầu.")
                : ServiceResult<ReportExportFile>.Conflict(
                    "File chưa sẵn sàng. Hãy chờ thông báo hoàn tất rồi tải lại.");
        }

        if (job.Content is null || job.FileName is null || job.ContentType is null)
        {
            // Không thể xảy ra nếu processor làm đúng, nhưng dòng Succeeded thiếu file sẽ làm
            // controller ném NullReferenceException ở một chỗ rất khó lần. Trả 404 rõ ràng.
            return ServiceResult<ReportExportFile>.NotFound("Không tìm thấy file của yêu cầu xuất.");
        }

        return ServiceResult<ReportExportFile>.Ok(new ReportExportFile
        {
            Content = job.Content,
            FileName = job.FileName,
            ContentType = job.ContentType,
        });
    }

    public async Task<int> RecoverStaleAsync(
        DateTime utcNow,
        TimeSpan staleAfter,
        CancellationToken cancellationToken)
    {
        var threshold = utcNow - staleAfter;

        var stale = await _db.ExportJobs
            .Where(j => j.Status == ExportJobStatus.Processing
                && j.StartedAt != null
                && j.StartedAt < threshold)
            .ToListAsync(cancellationToken);

        if (stale.Count == 0)
        {
            return 0;
        }

        foreach (var job in stale)
        {
            // KHÔNG tăng AttemptCount ở đây — lần nhận job tiếp theo mới tăng. Tăng ở đây sẽ đếm
            // hai lần cho cùng một lần chạy hỏng và làm job chết sớm hơn dự kiến.
            job.Status = ExportJobStatus.Pending;
            job.StartedAt = null;
            job.UpdatedAt = utcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return stale.Count;
    }

    public async Task<int> CleanupExpiredAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        // ExecuteDeleteAsync: xoá thẳng ở CSDL, không nạp hàng trăm MB Content vào bộ nhớ chỉ để
        // đánh dấu Deleted. Job hết hạn không còn ai đọc (GetAsync/GetFileAsync đều trả 404 từ
        // ExpiresAt), nên không cần đi qua change tracker.
        return await _db.ExportJobs
            .Where(j => j.ExpiresAt != null && j.ExpiresAt <= utcNow)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// Chỉ chép các trường thuộc loại báo cáo đã chọn. Trường thừa của form dùng chung bị bỏ qua
    /// ngay từ lúc đặt job, nên JSON trong CSDL đọc lên là biết job thực sự dùng bộ lọc nào.
    /// </summary>
    private static ExportJobParameters SnapshotParameters(
        CreateExportJobRequest request,
        ExportReportType reportType) => reportType switch
        {
            ExportReportType.Revenue => new ExportJobParameters
            {
                Granularity = string.IsNullOrWhiteSpace(request.Granularity)
                    ? null
                    : request.Granularity.Trim(),
                From = request.From,
                To = request.To,
                RouteId = request.RouteId,
            },
            ExportReportType.FillRate => new ExportJobParameters
            {
                From = request.From,
                To = request.To,
                RouteId = request.RouteId,
                TimeSlot = string.IsNullOrWhiteSpace(request.TimeSlot)
                    ? null
                    : request.TimeSlot.Trim(),
            },
            _ => new ExportJobParameters
            {
                From = request.From,
                To = request.To,
                AuditUserId = request.UserId,
                AuditAction = string.IsNullOrWhiteSpace(request.Action)
                    ? null
                    : request.Action.Trim(),
            },
        };

    private static ExportJobResponse ToResponse(ExportJob job, DateTime utcNow) => new()
    {
        Id = job.Id,
        ReportType = job.ReportType.ToCode(),
        Format = job.Format,
        Status = job.Status.ToString(),
        CreatedAt = job.CreatedAt,
        StartedAt = job.StartedAt,
        CompletedAt = job.CompletedAt,
        ExpiresAt = job.ExpiresAt,
        FileName = job.FileName,
        FileSizeBytes = job.FileSizeBytes,
        RowCount = job.RowCount,
        ErrorMessage = job.ErrorMessage,

        // Server là nơi duy nhất biết đường dẫn tải. Hôm nay là một chuỗi cố định, nhưng để frontend
        // tự ghép là ghim hình dạng route vào một file khác — đổi route sẽ quên cập nhật nó.
        DownloadUrl = job.Status == ExportJobStatus.Succeeded && !IsExpired(job, utcNow)
            ? $"/api/exports/{job.Id}/download"
            : null,
    };

    private static bool IsExpired(ExportJob job, DateTime utcNow)
        => job.ExpiresAt is { } expiresAt && expiresAt <= utcNow;

    private static ServiceResult<ExportJobResponse> FieldError(string field, string message)
        => ServiceResult<ExportJobResponse>.Invalid(
            message,
            new Dictionary<string, string[]> { [field] = [message] });

    private static ServiceResult<ExportJobResponse> RangeError(string message)
        => ServiceResult<ExportJobResponse>.Invalid(message, new Dictionary<string, string[]>
        {
            ["from"] = [message],
            ["to"] = [message],
        });
}
