using SmartBus.Api.Dtos.AuditLogs;

namespace SmartBus.Api.Services;

/// <summary>
/// Xuất nhật ký kiểm toán ra file Excel — task story 23, Phùng Duy Hoàng.
///
/// Cố ý TÁCH khỏi <see cref="IAuditLogService"/> (của Vàng Thị Dăm, hiện chỉ có
/// <see cref="IAuditLogService.RecordAsync"/>): đường truy vấn danh sách
/// <c>GET /api/audit-logs</c> là task riêng của Nguyễn Duy Kiên, và hai người cùng mở một
/// interface là nguồn conflict. File này đứng một mình, chỉ ĐỌC bảng AuditLogs.
/// </summary>
public interface IAuditLogExportService
{
    /// <summary>
    /// Dựng file .xlsx chứa các bản ghi khớp bộ lọc, mới nhất trước.
    ///
    /// Trả <see cref="ServiceErrorKind.Invalid"/> khi khoảng ngày ngược nhau hoặc khi vượt trần
    /// dòng — <b>không bao giờ</b> trả về file thiếu dòng.
    /// </summary>
    /// <param name="request">Bộ lọc bind từ query string.</param>
    /// <param name="exportedBy">
    /// Họ tên người xuất, ghi vào dòng mô tả của file. <c>null</c> nếu không lấy được — thiếu tên
    /// không phải lý do để từ chối xuất.
    /// </param>
    /// <param name="cancellationToken">
    /// Có nhận token, khác <see cref="IAuditLogService.RecordAsync"/>. Đây là thao tác ĐỌC dài:
    /// client ngắt kết nối thì dừng ngay được, không có bản ghi nào bị mất vì dừng.
    /// </param>
    Task<ServiceResult<AuditLogExportFile>> ExportAsync(
        ExportAuditLogsRequest request,
        string? exportedBy,
        CancellationToken cancellationToken);
}
