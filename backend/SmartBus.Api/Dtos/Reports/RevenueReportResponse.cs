using System.Globalization;

namespace SmartBus.Api.Dtos.Reports;

/// <summary>
/// Hình dạng trả về của GET /api/reports/revenue (US 19 "Doanh thu", Sprint 4 — Trần Trung Hiếu).
///
/// Hai lớp dữ liệu: (1) hai con số TỔNG cho thẻ số liệu của dashboard, (2) dãy bucket liên tục
/// cho biểu đồ theo thời gian. Bucket KHÔNG bỏ ngày rỗng — trục thời gian cố định, ngày không
/// có giao dịch thì <c>revenue</c>/<c>payments</c> bằng 0 (cùng lối "trục cố định, thiếu dữ liệu
/// thì vẽ cột 0" của VoucherStatisticsService).
///
/// Hợp đồng: docs/api-contract.md mục "Báo cáo doanh thu — /reports/revenue".
/// </summary>
public class RevenueReportResponse
{
    /// <summary>Độ mịn đã áp dụng — lặp lại tham số (hoặc giá trị mặc định) để phản hồi tự đủ nghĩa.</summary>
    public string Granularity { get; set; } = string.Empty;

    /// <summary>Ngày bắt đầu báo cáo (lịch Việt Nam) — lặp lại tham số sau khi áp dụng mặc định.</summary>
    public DateOnly From { get; set; }

    /// <summary>Ngày kết thúc báo cáo (lịch Việt Nam), TÍNH CẢ.</summary>
    public DateOnly To { get; set; }

    /// <summary>Tổng doanh thu cả kỳ (VND) — chỉ giao dịch Success, tính tại thời điểm thu tiền.</summary>
    public decimal TotalRevenue { get; set; }

    /// <summary>Số giao dịch Success cả kỳ.</summary>
    public int TotalPayments { get; set; }

    /// <summary>
    /// Dãy bucket LIÊN TỤC phủ trọn khoảng from → to. Bucket đầu tiên có thể bắt đầu TRƯỚC
    /// <c>from</c> (tuần lấy từ thứ Hai, tháng lấy từ ngày 1) để biểu đồ vẽ trọn khối thời gian.
    /// </summary>
    public required IReadOnlyList<RevenueBucketResponse> Buckets { get; set; }
}

/// <summary>Một mốc thời gian trong dãy bucket — một cột của biểu đồ doanh thu.</summary>
public class RevenueBucketResponse
{
    /// <summary>
    /// Điểm bắt đầu bucket, dạng <c>yyyy-MM-dd</c>: ngày đầu tiên của bucket theo độ mịn —
    /// chính ngày đó (day), thứ Hai của tuần (week), ngày 1 của tháng (month).
    /// </summary>
    public string PeriodStart { get; set; } = string.Empty;

    /// <summary>Doanh thu (VND) của bucket.</summary>
    public decimal Revenue { get; set; }

    /// <summary>Số giao dịch Success của bucket.</summary>
    public int Payments { get; set; }
}

/// <summary>Khuôn <c>yyyy-MM-dd</c> dùng chung cho periodStart — một chỗ, không trôi định dạng.</summary>
internal static class RevenueBucketFormat
{
    public static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
