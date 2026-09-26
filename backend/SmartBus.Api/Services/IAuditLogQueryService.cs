using SmartBus.Api.Dtos.AuditLogs;

namespace SmartBus.Api.Services;

/// <summary>
/// Truy vấn danh sách nhật ký kiểm toán — task story 23, Nguyễn Duy Kiên.
///
/// Cố ý TÁCH khỏi <see cref="IAuditLogService"/> (của Vàng Thị Dăm, chỉ có
/// <see cref="IAuditLogService.RecordAsync"/>) và khỏi <see cref="IAuditLogExportService"/> (của
/// Phùng Duy Hoàng): ba người cùng mở một interface là nguồn conflict, mà ba việc cũng khác nhau
/// thật — ghi, xuất file, và truy vấn. File này đứng một mình, chỉ ĐỌC bảng AuditLogs.
/// </summary>
public interface IAuditLogQueryService
{
    /// <summary>
    /// Lấy một trang nhật ký khớp bộ lọc, mới nhất trước.
    ///
    /// Trả <see cref="ServiceErrorKind.Invalid"/> chỉ khi khoảng ngày ngược nhau. Bộ lọc không
    /// khớp bản ghi nào là kết quả <b>hợp lệ</b> — trang rỗng với <c>Total = 0</c>, không phải lỗi:
    /// câu hỏi kiểm toán hay gặp nhất là "tuần đó có ai xoá gì không?", và "không" là một câu trả lời.
    /// </summary>
    /// <param name="request">Bộ lọc + phân trang bind từ query string.</param>
    /// <param name="cancellationToken">
    /// Có nhận token, khác <see cref="IAuditLogService.RecordAsync"/>. Đây là thao tác ĐỌC:
    /// client ngắt kết nối thì dừng ngay được, không có bản ghi nào bị mất vì dừng.
    /// </param>
    Task<ServiceResult<AuditLogListResponse>> ListAsync(
        ListAuditLogsRequest request,
        CancellationToken cancellationToken);
}
