namespace SmartBus.Api.Entities;

/// <summary>
/// Loại báo cáo mà một <see cref="ExportJob"/> sẽ dựng. Lưu dạng chuỗi trong CSDL (quy ước A3).
///
/// Ba giá trị tương ứng đúng ba bản xuất đồng bộ đã có:
/// <c>GET /api/reports/revenue/export</c>, <c>GET /api/reports/fill-rate/export</c> và
/// <c>GET /api/audit-logs/export</c>. Bản bất đồng bộ không thêm loại báo cáo mới — nó chỉ chạy
/// cùng phép dựng file ở nền, nên danh sách này cố ý đóng.
///
/// ⚠️ Giá trị trao đổi với frontend là <c>revenue</c> / <c>fill-rate</c> / <c>audit-logs</c>
/// (kebab-case, cùng lối route API), KHÔNG phải tên enum. Đổi một chuỗi là đổi hợp đồng — sửa
/// <c>docs/api-contract.md</c> trước.
/// </summary>
public enum ExportReportType
{
    /// <summary>Báo cáo doanh thu — tối đa 366 bucket nên không bao giờ chạm trần dòng.</summary>
    Revenue,

    /// <summary>Báo cáo tỷ lệ lấp đầy — một dòng mỗi chuyến, đây là loại phình to nhất.</summary>
    FillRate,

    /// <summary>Nhật ký kiểm toán — một dòng mỗi bản ghi, chỉ Admin được yêu cầu.</summary>
    AuditLogs,
}

/// <summary>
/// Bộ đọc/ghi mã chuỗi của <see cref="ExportReportType"/>.
///
/// Một chỗ duy nhất biết ba chuỗi hợp lệ: controller, service và test dùng chung, nên không có
/// cách nào một bên nhận <c>fillRate</c> còn bên kia ghi <c>fill-rate</c>.
/// </summary>
public static class ExportReportTypes
{
    /// <summary>Mã của <see cref="ExportReportType.Revenue"/>.</summary>
    public const string Revenue = "revenue";

    /// <summary>Mã của <see cref="ExportReportType.FillRate"/>.</summary>
    public const string FillRate = "fill-rate";

    /// <summary>Mã của <see cref="ExportReportType.AuditLogs"/>.</summary>
    public const string AuditLogs = "audit-logs";

    /// <summary>Thông báo lỗi dùng chung cho trường <c>reportType</c>.</summary>
    public const string InvalidMessage =
        "Loại báo cáo phải là revenue, fill-rate hoặc audit-logs.";

    /// <summary>
    /// Đọc mã báo cáo, bỏ qua hoa/thường và khoảng trắng thừa. Cố ý KHÔNG dùng
    /// <c>Enum.TryParse</c> — nó nhận cả chuỗi số và tên enum (<c>"FillRate"</c>), trái quy ước
    /// A3 "giá trị trao đổi dạng chuỗi đọc được".
    /// </summary>
    public static bool TryParse(string? value, out ExportReportType reportType)
    {
        var trimmed = value?.Trim();

        if (string.Equals(trimmed, Revenue, StringComparison.OrdinalIgnoreCase))
        {
            reportType = ExportReportType.Revenue;
            return true;
        }

        if (string.Equals(trimmed, FillRate, StringComparison.OrdinalIgnoreCase))
        {
            reportType = ExportReportType.FillRate;
            return true;
        }

        if (string.Equals(trimmed, AuditLogs, StringComparison.OrdinalIgnoreCase))
        {
            reportType = ExportReportType.AuditLogs;
            return true;
        }

        reportType = default;
        return false;
    }

    /// <summary>Mã chuỗi của một loại — dùng trong response và trong thông báo.</summary>
    public static string ToCode(this ExportReportType reportType) => reportType switch
    {
        ExportReportType.Revenue => Revenue,
        ExportReportType.FillRate => FillRate,
        ExportReportType.AuditLogs => AuditLogs,
        _ => throw new ArgumentOutOfRangeException(nameof(reportType), reportType, null),
    };

    /// <summary>Nhãn tiếng Việt để ghép vào tiêu đề/nội dung thông báo và tên file.</summary>
    public static string ToLabel(this ExportReportType reportType) => reportType switch
    {
        ExportReportType.Revenue => "báo cáo doanh thu",
        ExportReportType.FillRate => "báo cáo tỷ lệ lấp đầy",
        ExportReportType.AuditLogs => "nhật ký kiểm toán",
        _ => throw new ArgumentOutOfRangeException(nameof(reportType), reportType, null),
    };

    /// <summary>Phần đầu tên file ASCII không dấu, giống hệt ba bản xuất đồng bộ.</summary>
    public static string FileStem(this ExportReportType reportType) => reportType switch
    {
        ExportReportType.Revenue => "bao-cao-doanh-thu",
        ExportReportType.FillRate => "bao-cao-ty-le-lap-day",
        ExportReportType.AuditLogs => "nhat-ky-kiem-toan",
        _ => throw new ArgumentOutOfRangeException(nameof(reportType), reportType, null),
    };
}
