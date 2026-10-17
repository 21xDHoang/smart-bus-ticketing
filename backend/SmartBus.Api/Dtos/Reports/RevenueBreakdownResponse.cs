namespace SmartBus.Api.Dtos.Reports;

/// <summary>
/// Phần chung của ba phản hồi doanh thu phân rã (US 19 "Doanh thu", Sprint 4 — Nguyễn Duy Kiên):
/// khoảng ngày đã áp mặc định + hai con số tổng của cả kỳ.
///
/// <see cref="TotalRevenue"/> / <see cref="TotalPayments"/> được tính bằng một truy vấn gộp riêng
/// trên bảng <c>Payments</c> — cùng cách với <see cref="RevenueReportResponse"/>, không cộng từ
/// các dòng trả về. Nhờ vậy đẳng thức "tổng == cộng các dòng" là phép kiểm thật (xem
/// <c>RevenueBreakdownService</c>).
/// </summary>
public abstract class RevenueBreakdownResponse
{
    /// <summary>Ngày bắt đầu báo cáo (lịch Việt Nam) — lặp lại tham số sau khi áp dụng mặc định.</summary>
    public DateOnly From { get; set; }

    /// <summary>Ngày kết thúc báo cáo (lịch Việt Nam), TÍNH CẢ.</summary>
    public DateOnly To { get; set; }

    /// <summary>Tổng doanh thu cả kỳ (VND) — chỉ giao dịch Success.</summary>
    public decimal TotalRevenue { get; set; }

    /// <summary>Số giao dịch Success cả kỳ.</summary>
    public int TotalPayments { get; set; }
}
