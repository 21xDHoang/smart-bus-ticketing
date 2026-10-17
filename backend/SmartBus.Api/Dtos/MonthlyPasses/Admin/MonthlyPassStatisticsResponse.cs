namespace SmartBus.Api.Dtos.MonthlyPasses.Admin;

/// <summary>
/// Kết quả GET /api/monthly-passes/statistics — số vé theo nhóm hiệu lực + doanh thu.
/// Hợp đồng đầy đủ ở mục "Vé tháng — /monthly-passes" của docs/api-contract.md.
///
/// Bốn nhóm đếm đầu PHÂN HOẠCH đủ tập vé: mỗi vé rơi đúng một nhóm. Nhóm Active/Upcoming/Expired
/// suy từ cặp ValidFrom/ValidTo (không đọc cột Status của Active/Expired — cột đó có độ trễ);
/// Cancelled đọc cột Status vì đó là chặn cứng do người đặt.
/// </summary>
public class MonthlyPassStatisticsResponse
{
    /// <summary>Tổng số dòng vé tháng.</summary>
    public int TotalCount { get; set; }

    /// <summary>Còn hiệu lực thật: status != Cancelled và ValidFrom &lt;= bây giờ &lt;= ValidTo.</summary>
    public int ActiveCount { get; set; }

    /// <summary>Vé mua trước cho kỳ sau: status != Cancelled và ValidFrom &gt; bây giờ.</summary>
    public int UpcomingCount { get; set; }

    /// <summary>Đã quá hạn: status != Cancelled và ValidTo &lt; bây giờ.</summary>
    public int ExpiredCount { get; set; }

    /// <summary>Vé bị quản trị viên huỷ.</summary>
    public int CancelledCount { get; set; }

    /// <summary>Tổng tiền đã thu của MỌI vé — tính cả vé đã huỷ, vì huỷ không hoàn tiền.</summary>
    public decimal TotalRevenue { get; set; }

    /// <summary>Tổng tiền các vé có CreatedAt trong tháng hiện tại theo UTC.</summary>
    public decimal RevenueThisMonth { get; set; }
}
