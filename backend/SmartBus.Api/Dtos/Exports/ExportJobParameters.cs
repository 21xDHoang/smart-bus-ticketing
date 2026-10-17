namespace SmartBus.Api.Dtos.Exports;

/// <summary>
/// Ảnh chụp bộ lọc được ghi vào <c>ExportJobs.ParametersJson</c> lúc đặt job.
///
/// Vì sao chép ra một lớp riêng thay vì serialize thẳng <see cref="CreateExportJobRequest"/>:
/// request có <c>reportType</c>/<c>format</c> — hai thứ đã nằm ở cột riêng của bảng. Ghi lặp lại
/// chúng trong JSON là hai nguồn sự thật cho cùng một giá trị, và sớm muộn sẽ có một chỗ sửa cột
/// mà quên JSON. Lớp này chỉ chứa phần BỘ LỌC.
///
/// <c>System.Text.Json</c> serialize <see cref="DateOnly"/> thành <c>yyyy-MM-dd</c> và đọc lại
/// được; <see cref="Guid"/> thành chuỗi. Không cần converter riêng.
/// </summary>
public class ExportJobParameters
{
    public string? Granularity { get; set; }

    public DateOnly? From { get; set; }

    public DateOnly? To { get; set; }

    public Guid? RouteId { get; set; }

    public string? TimeSlot { get; set; }

    public Guid? AuditUserId { get; set; }

    public string? AuditAction { get; set; }
}
