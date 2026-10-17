namespace SmartBus.Api.Services;

/// <summary>
/// Một file báo cáo đã dựng xong: nội dung nhị phân, tên tải về và MIME để Controller trả thẳng
/// cho trình duyệt mà không phải đoán lại định dạng.
///
/// Dùng chung cho các endpoint xuất mới (doanh thu, tỷ lệ lấp đầy). Bản xuất nhật ký giữ kiểu
/// <c>AuditLogExportFile</c> riêng vì nó có trước và đã được test ghim.
/// </summary>
public sealed record ReportExportFile
{
    /// <summary>Toàn bộ bytes của file — Excel hoặc PDF, tuỳ <see cref="ContentType"/>.</summary>
    public required byte[] Content { get; init; }

    /// <summary>Tên file ASCII (không dấu) để <c>Content-Disposition</c> không phải mã hoá.</summary>
    public required string FileName { get; init; }

    /// <summary>MIME thật của <see cref="Content"/> — quyết định trình duyệt mở bằng Excel hay PDF.</summary>
    public required string ContentType { get; init; }
}
