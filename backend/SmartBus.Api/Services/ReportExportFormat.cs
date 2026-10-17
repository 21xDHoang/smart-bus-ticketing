namespace SmartBus.Api.Services;

/// <summary>
/// Định dạng file của ba endpoint xuất báo cáo (doanh thu / tỷ lệ lấp đầy / nhật ký kiểm toán).
///
/// Chỉ hai giá trị, và Excel là MẶC ĐỊNH khi client không gửi <c>format</c> — giữ nguyên hành vi
/// của endpoint nhật ký trước khi có PDF. Giá trị lạ là lỗi đầu vào (400), không phải "đoán hộ".
/// </summary>
public enum ReportExportFormat
{
    /// <summary>File .xlsx — mặc định, mở và lọc được trong Excel.</summary>
    Xlsx,

    /// <summary>File .pdf — bản in để trình ký, không có bộ lọc.</summary>
    Pdf,
}

/// <summary>
/// Hằng số và bộ đọc <c>format</c> dùng chung cho cả ba bản xuất.
///
/// Một chỗ duy nhất biết content-type và cách so khớp chuỗi: nếu mỗi service tự viết
/// <c>"pdf"</c> / <c>"application/pdf"</c> thì sớm muộn cũng có một chỗ quên
/// <c>OrdinalIgnoreCase</c> hoặc trả sai MIME, và frontend tải file hỏng mà không hiểu vì sao.
/// </summary>
public static class ReportExportFormats
{
    /// <summary>MIME của file Excel — dùng cho <c>Content-Disposition</c> và trình duyệt.</summary>
    public const string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>MIME của PDF.</summary>
    public const string PdfContentType = "application/pdf";

    /// <summary>
    /// Câu lỗi dùng chung cho <c>errors.format</c>. Nêu đúng hai giá trị hợp lệ để người gọi sửa
    /// được ngay, không phải mở tài liệu.
    /// </summary>
    public const string InvalidMessage = "Định dạng file phải là xlsx hoặc pdf.";

    /// <summary>
    /// Đọc tham số <c>format</c>: bỏ trống = <see cref="ReportExportFormat.Xlsx"/>; so khớp không
    /// phân biệt hoa/thường; mọi giá trị khác trả <c>false</c> để service dựng lỗi 400 gắn vào
    /// trường <c>format</c>.
    ///
    /// Cố ý KHÔNG dùng <c>Enum.TryParse</c> — nó chấp nhận cả chuỗi số (<c>"1"</c> ra
    /// <c>Pdf</c>) và tên enum, trái quy ước A3 "giá trị lưu/trao đổi dạng chuỗi đọc được".
    /// </summary>
    public static bool TryParse(string? value, out ReportExportFormat format)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            format = ReportExportFormat.Xlsx;
            return true;
        }

        if (string.Equals(trimmed, "xlsx", StringComparison.OrdinalIgnoreCase))
        {
            format = ReportExportFormat.Xlsx;
            return true;
        }

        if (string.Equals(trimmed, "pdf", StringComparison.OrdinalIgnoreCase))
        {
            format = ReportExportFormat.Pdf;
            return true;
        }

        format = default;
        return false;
    }

    /// <summary>MIME tương ứng — Controller truyền thẳng vào <c>File(byte[], string, string)</c>.</summary>
    public static string ContentType(this ReportExportFormat format) => format == ReportExportFormat.Pdf
        ? PdfContentType
        : XlsxContentType;

    /// <summary>Phần đuôi tên file, kèm dấu chấm — nối thẳng sau mốc thời gian.</summary>
    public static string Extension(this ReportExportFormat format) =>
        format == ReportExportFormat.Pdf ? ".pdf" : ".xlsx";
}
