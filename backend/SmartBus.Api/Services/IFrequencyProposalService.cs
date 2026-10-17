using SmartBus.Api.Dtos.Reports;

namespace SmartBus.Api.Services;

/// <summary>
/// Đề xuất tăng/giảm tần suất chạy xe theo dữ liệu — GET /api/reports/frequency-proposals (US 20
/// "Tỷ lệ lấp đầy", Sprint 4 — Phùng Duy Hoàng). Đọc thẳng báo cáo khung giờ cao điểm rồi quy đổi
/// mật độ từng giờ thành số chuyến nên chạy; không ghi dữ liệu.
/// </summary>
public interface IFrequencyProposalService
{
    /// <summary>
    /// Trả đề xuất cho đủ 24 giờ. Lỗi đầu vào (from sau to, quá 366 ngày, tuyến không tồn tại)
    /// được chuyển nguyên từ <see cref="IPeakHoursReportService"/> — controller chỉ thấy một hình
    /// dạng kết quả.
    /// </summary>
    Task<ServiceResult<FrequencyProposalResponse>> GetAsync(
        FrequencyProposalRequest request,
        CancellationToken cancellationToken);
}
