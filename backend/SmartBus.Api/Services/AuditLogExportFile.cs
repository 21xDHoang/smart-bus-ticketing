namespace SmartBus.Api.Services;

/// <summary>
/// Một file nhật ký kiểm toán đã dựng xong (Excel hoặc PDF), kèm tên file và Content-Type.
///
/// Không phải DTO trả ra JSON: Controller đưa thẳng <see cref="Content"/> cho
/// <c>File(byte[], string, string)</c> của <c>ControllerBase</c>. Đặt ở Services/ cạnh
/// <see cref="AuthResult"/> vì cùng là "kết quả nội bộ giữa Service và Controller", không phải
/// hình dạng dây của API.
/// </summary>
public class AuditLogExportFile
{
    /// <summary>
    /// Toàn bộ file nằm trong bộ nhớ. Xem chú thích trần dòng ở
    /// <see cref="AuditLogExportService.MaxRows"/> và
    /// <see cref="AuditLogExportService.MaxPdfRows"/> để biết vì sao dựng đủ rồi mới trả.
    /// </summary>
    public byte[] Content { get; set; } = [];

    /// <summary>Tên file ASCII không dấu — xem chú thích ở <c>AuditLogExportController.Export</c>.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Content-Type của file vừa dựng. Mặc định Excel cho mọi nơi gán thiếu — đường xuất cũ chưa
    /// biết định dạng thì vẫn trả đúng loại như trước.
    /// </summary>
    public string ContentType { get; set; } = ReportExportFormats.XlsxContentType;
}
