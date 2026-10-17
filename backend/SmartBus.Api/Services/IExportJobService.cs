using SmartBus.Api.Dtos.Exports;

namespace SmartBus.Api.Services;

/// <summary>
/// Đặt và đọc trạng thái yêu cầu xuất báo cáo bất đồng bộ —
/// <c>POST /api/exports</c>, <c>GET /api/exports/{id}</c>, <c>GET /api/exports/{id}/download</c>.
///
/// Task *"Xử lý xuất file lớn bất đồng bộ + thông báo khi xong"* (Sprint 5 dòng 64, Vàng Thị Dăm).
/// Việc DỰNG file nằm ở <see cref="IExportJobProcessor"/> (job nền gọi), không nằm ở đây — API chỉ
/// ghi một dòng <c>Pending</c> rồi trả 202, không chờ báo cáo chạy xong.
///
/// Mọi thao tác đều giới hạn theo <c>userId</c> của người gọi: không có tham số nào để đọc job của
/// người khác, và job của người khác trả 404 (không phải 403) để không tiết lộ sự tồn tại của nó.
/// </summary>
public interface IExportJobService
{
    /// <summary>
    /// Kiểm tham số rẻ (định dạng, loại báo cáo, khoảng ngày, tuyến tồn tại) rồi ghi một job
    /// <c>Pending</c>. Trả 400/404 ngay khi tham số chắc chắn hỏng — không đẩy một job chắc chắn
    /// thất bại vào hàng đợi để người dùng chờ rồi nhận thông báo lỗi.
    ///
    /// Lỗi chỉ phát hiện được khi đọc dữ liệu (ví dụ dữ liệu mồ côi) vẫn rơi vào nhánh job
    /// <c>Failed</c> — xem <see cref="IExportJobProcessor"/>.
    /// </summary>
    Task<ServiceResult<ExportJobResponse>> EnqueueAsync(
        CreateExportJobRequest request,
        Guid userId,
        string? userName,
        CancellationToken cancellationToken);

    /// <summary>Trạng thái job của chính người gọi. Không có/không phải của họ/hết hạn → 404.</summary>
    Task<ServiceResult<ExportJobResponse>> GetAsync(
        Guid jobId,
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// File của job đã <c>Succeeded</c>. Chưa xong → <see cref="ServiceErrorKind.Conflict"/> (409);
    /// không có job, của người khác, hoặc đã hết hạn → 404.
    /// </summary>
    Task<ServiceResult<ReportExportFile>> GetFileAsync(
        Guid jobId,
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Đưa job <c>Processing</c> bị bỏ quên (app tắt giữa lúc dựng file) về <c>Pending</c> để lần
    /// quét sau làm lại. Chỉ job nền gọi — không có endpoint nào cho người dùng.
    /// </summary>
    Task<int> RecoverStaleAsync(DateTime utcNow, TimeSpan staleAfter, CancellationToken cancellationToken);

    /// <summary>
    /// Xoá job đã quá <c>ExpiresAt</c> (cả file). Chỉ job nền gọi.
    /// </summary>
    Task<int> CleanupExpiredAsync(DateTime utcNow, CancellationToken cancellationToken);
}
