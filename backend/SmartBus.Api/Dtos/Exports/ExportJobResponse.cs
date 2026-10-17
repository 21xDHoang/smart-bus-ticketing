namespace SmartBus.Api.Dtos.Exports;

/// <summary>
/// Trạng thái một job xuất báo cáo — trả về từ <c>POST /api/exports</c> và
/// <c>GET /api/exports/{id}</c>. Hợp đồng ở mục "Xuất file bất đồng bộ — /exports" của
/// docs/api-contract.md.
///
/// Không có trường <c>content</c>: file tải qua endpoint riêng, để lời gọi trạng thái (frontend
/// hỏi lại mỗi vài giây) không bao giờ kéo theo vài chục MB.
/// </summary>
public class ExportJobResponse
{
    public Guid Id { get; set; }

    /// <summary><c>revenue</c> / <c>fill-rate</c> / <c>audit-logs</c> — cùng mã với request.</summary>
    public string ReportType { get; set; } = string.Empty;

    /// <summary><c>xlsx</c> hoặc <c>pdf</c>.</summary>
    public string Format { get; set; } = string.Empty;

    /// <summary><c>Pending</c> / <c>Processing</c> / <c>Succeeded</c> / <c>Failed</c>.</summary>
    public string Status { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    /// <summary>Hạn tải file. Sau mốc này endpoint tải trả 404 và job bị dọn khỏi CSDL.</summary>
    public DateTime? ExpiresAt { get; set; }

    public string? FileName { get; set; }

    public long? FileSizeBytes { get; set; }

    /// <summary>Số dòng dữ liệu của file; <c>null</c> khi chưa xong hoặc với báo cáo doanh thu.</summary>
    public int? RowCount { get; set; }

    /// <summary>Lý do thất bại, chỉ có khi <see cref="Status"/> là <c>Failed</c>.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Đường dẫn tải tương đối, chỉ có khi <c>Succeeded</c>. Frontend không tự ghép đường dẫn —
    /// hôm nay là <c>/api/exports/{id}/download</c>, nhưng để server là nơi duy nhất biết.
    /// </summary>
    public string? DownloadUrl { get; set; }
}
