namespace SmartBus.Api.Services;

/// <summary>
/// Hợp đồng dựng file Excel báo cáo dùng chung (task "Service dùng chung để xuất Excel
/// (ClosedXML) cho mọi báo cáo" — US 21 "Xuất dữ liệu", Sprint 5 dòng 61, Nguyễn Duy Kiên).
///
/// Một phép THUẦN: model vào, mảng byte <c>.xlsx</c> ra — không CSDL, không mạng, không tự lấy
/// giờ hệ thống; cùng một model luôn ra cùng một file, máy dev lẫn CI.
///
/// Đây là điểm nối thay cho ba bản <c>BuildWorkbook</c> từng lặp trong
/// <c>RevenueExportService</c>, <c>FillRateExportService</c> và <c>AuditLogExportService</c>.
/// </summary>
public interface IReportExcelService
{
    /// <summary>
    /// Dựng workbook theo khuôn chung (docs/49 §2.7). Ném <see cref="ArgumentException"/> nếu
    /// model thiếu tiêu đề/tên sheet, số ô một dòng lệch số cột, hoặc độ rộng cột không khớp —
    /// hỏng phải nổi lên ngay chứ không xuất ra một chứng từ sai.
    /// </summary>
    byte[] GenerateExcel(ReportExcelModel model);
}
