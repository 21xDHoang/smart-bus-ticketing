namespace SmartBus.Api.Dtos.Reports;

/// <summary>
/// Độ mịn của báo cáo doanh thu theo thời gian (US 19 "Doanh thu", Sprint 4 — Trần Trung Hiếu):
/// gộp theo ngày, theo tuần hay theo tháng.
/// </summary>
public enum RevenueGranularity
{
    /// <summary>Mỗi bucket là một ngày — <c>periodStart</c> là chính ngày đó.</summary>
    Day,

    /// <summary>Mỗi bucket là một tuần bắt đầu THỨ HAI — <c>periodStart</c> là thứ Hai của tuần.</summary>
    Week,

    /// <summary>Mỗi bucket là một tháng lịch — <c>periodStart</c> là ngày 1 của tháng.</summary>
    Month,
}

/// <summary>So khớp chuỗi tham số <c>granularity</c> — xem <see cref="RevenueReportService"/>.</summary>
public static class RevenueGranularityCodes
{
    /// <summary>
    /// So khớp với danh sách tên của enum thay vì dùng <c>Enum.TryParse</c> — cùng lý do
    /// <c>RouteService.TryParseRouteStatus</c> và <c>AuditLogExportService.TryParseAuditAction</c>:
    /// TryParse chấp nhận cả chuỗi số (<c>"3"</c> ra <c>Month</c>), trái quy ước A3 là trạng thái
    /// lưu dạng chuỗi đọc được. Bỏ qua hoa/thường khi đọc. Trống/whitespace coi như chọn mặc định
    /// <see cref="RevenueGranularity.Day"/> (tham số bị bỏ trống = không chọn).
    /// </summary>
    public static bool TryParse(string? value, out RevenueGranularity granularity)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            granularity = RevenueGranularity.Day;
            return true;
        }

        foreach (var name in Enum.GetNames<RevenueGranularity>())
        {
            if (string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                granularity = Enum.Parse<RevenueGranularity>(name);
                return true;
            }
        }

        granularity = default;
        return false;
    }
}
