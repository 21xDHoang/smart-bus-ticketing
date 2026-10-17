namespace SmartBus.Api.Dtos.Reports;

/// <summary>
/// Tham số của <c>GET /api/reports/fill-rate/export</c> — US 20 "Tỷ lệ lấp đầy", Sprint 4
/// dòng 78 — Phùng Duy Hoàng. Hợp đồng ở mục "Xuất báo cáo tỷ lệ lấp đầy" của
/// docs/api-contract.md.
///
/// Khoảng ngày tính theo LỊCH VIỆT NAM và tính cả hai đầu; khung giờ cũng theo giờ Việt Nam.
/// </summary>
public class FillRateExportRequest
{
    /// <summary>Ngày bắt đầu (lịch Việt Nam), tính cả. Bỏ trống = 29 ngày trước <see cref="To"/>.</summary>
    public DateOnly? From { get; set; }

    /// <summary>Ngày kết thúc (lịch Việt Nam), tính cả. Bỏ trống = hôm nay theo giờ Việt Nam.</summary>
    public DateOnly? To { get; set; }

    /// <summary>Lọc theo tuyến; bỏ trống = toàn công ty. Không tồn tại → 404.</summary>
    public Guid? RouteId { get; set; }

    /// <summary>
    /// Khung giờ khởi hành (giờ Việt Nam): <c>early</c> (05:00–09:00), <c>morning</c>
    /// (09:00–13:00), <c>afternoon</c> (13:00–17:00), <c>evening</c> (17:00–22:00).
    /// Biên dưới tính, biên trên không tính. Bỏ trống = mọi giờ; giá trị khác → 400
    /// gắn vào <c>errors.timeSlot</c>.
    /// </summary>
    public string? TimeSlot { get; set; }

    /// <summary><c>xlsx</c> (mặc định) hoặc <c>pdf</c>, không phân biệt hoa/thường. Giá trị khác → 400.</summary>
    public string? Format { get; set; }
}
