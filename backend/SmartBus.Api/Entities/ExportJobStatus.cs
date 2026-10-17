namespace SmartBus.Api.Entities;

/// <summary>
/// Vòng đời của một <see cref="ExportJob"/>. Lưu dạng chuỗi trong CSDL (quy ước A3).
///
/// <code>
/// Pending ──► Processing ──► Succeeded
///                   └──────► Failed
/// </code>
///
/// Không có trạng thái <c>Cancelled</c>: task chưa yêu cầu huỷ job, và một job bỏ đi giữa chừng
/// vẫn chiếm một dòng + có thể đã ghi file — thêm trạng thái đó là mở một nhánh nghiệp vụ không
/// ai gọi tới. Job <c>Processing</c> bị app tắt giữa chừng được job nền đưa lại <c>Pending</c>
/// (xem <c>ExportJobProcessor.RecoverStaleAsync</c>), không thành <c>Cancelled</c>.
/// </summary>
public enum ExportJobStatus
{
    /// <summary>Đã nhận yêu cầu, đang chờ job nền. Chưa có file.</summary>
    Pending,

    /// <summary>Job nền đang dựng file. Chưa tải được.</summary>
    Processing,

    /// <summary>File đã sẵn sàng, tải được tới <c>ExpiresAt</c>.</summary>
    Succeeded,

    /// <summary>Dựng file thất bại. <see cref="ExportJob.ErrorMessage"/> nói lý do.</summary>
    Failed,
}
