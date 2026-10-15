namespace SmartBus.Api.Services;

/// <summary>
/// Hợp đồng sinh file PDF báo cáo (task dòng 77 Sprint 4 — *"Service xuất PDF có tiêu đề, logo,
/// ngày xuất"*, US 21 "Xuất dữ liệu").
///
/// Một phép THUẦN: model vào, mảng byte PDF ra. Không CSDL, không mạng, không đọc file ngoài
/// assembly (font Roboto nhúng trong csproj), không tự lấy giờ hệ thống — ngày xuất do bên gọi
/// truyền qua <see cref="ReportPdfModel.ExportedAt"/> — nên test không cần hạ tầng gì và cùng
/// một model luôn ra cùng một file, máy dev lẫn CI.
///
/// Đây là viên gạch CHUNG cho mọi báo cáo xuất PDF của Sprint 4 (doanh thu, tỷ lệ lấp đầy, nhật
/// ký): API xuất file (task của Hoàng) gom dữ liệu vào model rồi gọi đúng một phương thức này.
/// </summary>
public interface IReportPdfService
{
    /// <summary>
    /// Sinh PDF báo cáo khổ A4 từ dữ liệu đã gom sẵn. Ném <see cref="ArgumentException"/> nếu
    /// thiếu tiêu đề, logo không phải SVG, hoặc bảng lệch cột — hỏng phải nổi lên ngay chứ
    /// không in ra một chứng từ sai.
    /// </summary>
    byte[] GeneratePdf(ReportPdfModel model);
}
