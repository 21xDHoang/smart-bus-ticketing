namespace SmartBus.Api.Dtos.Reports;

/// <summary>
/// Tham số query của GET /api/reports/fill-rate (US 20 "Tỷ lệ lấp đầy", Sprint 4 — Nguyễn Duy
/// Kiên). Mọi tham số bỏ trống được: <c>from</c>/<c>to</c> mặc định 30 ngày gần nhất TÍNH CẢ hôm
/// nay (giờ Việt Nam), <c>routeId</c> trống = toàn công ty, <c>timeSlot</c> trống = mọi khung giờ.
/// Hợp đồng: docs/api-contract.md mục "Tỷ lệ lấp đầy — /reports/fill-rate".
/// </summary>
public class FillRateReportRequest
{
    /// <summary>
    /// Ngày bắt đầu (theo lịch Việt Nam, dạng <c>yyyy-MM-dd</c>), TÍNH CẢ. Trống = 29 ngày trước
    /// <c>to</c>. Phải ≤ <c>to</c> và khoảng không quá 366 ngày — nếu không → 400 theo trường.
    /// </summary>
    public DateOnly? From { get; set; }

    /// <summary>Ngày kết thúc (theo lịch Việt Nam), TÍNH CẢ. Trống = hôm nay (giờ Việt Nam).</summary>
    public DateOnly? To { get; set; }

    /// <summary>
    /// Chỉ tính các chuyến thuộc tuyến này — bỏ trống = toàn công ty. Tuyến không tồn tại → 404.
    /// </summary>
    public Guid? RouteId { get; set; }

    /// <summary>
    /// Khung giờ khởi hành: <c>early</c> / <c>morning</c> / <c>afternoon</c> / <c>evening</c> /
    /// <c>night</c> (không phân biệt hoa/thường). Trống = mọi khung. Giá trị lạ → 400 theo trường
    /// <c>timeSlot</c> (kiểm ở service bằng <see cref="FillRateTimeSlotCodes.TryParse"/>).
    /// </summary>
    public string? TimeSlot { get; set; }
}
