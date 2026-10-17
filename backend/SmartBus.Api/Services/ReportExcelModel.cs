namespace SmartBus.Api.Services;

/// <summary>
/// Dữ liệu để dựng MỘT file Excel báo cáo — viên gạch chung cho mọi báo cáo xuất Excel (doanh
/// thu, tỷ lệ lấp đầy, nhật ký kiểm toán). Task "Service dùng chung để xuất Excel (ClosedXML) cho
/// mọi báo cáo" — US 21 "Xuất dữ liệu", Sprint 5 dòng 61, Nguyễn Duy Kiên.
///
/// Record PHẲNG, cố ý không tham chiếu entity hay ClosedXML: bên gọi tự gom dữ liệu và chọn kiểu
/// từng ô; <see cref="ReportExcelService"/> chỉ biết cách vẽ, không biết nghiệp vụ. Nhờ vậy service
/// thuần, test được không cần CSDL, và đổi hình dạng entity không làm vỡ tầng vẽ.
///
/// Khuôn file CỐ ĐỊNH (docs/49 §2.7): hàng 1 tiêu đề tài liệu, hàng 2 dòng ngữ cảnh, hàng 3
/// trống, hàng 4 tiêu đề cột, dữ liệu từ hàng 5; auto-filter + cố định hàng tiêu đề; độ rộng cột
/// do bên gọi đặt cứng.
/// </summary>
public sealed record ReportExcelModel
{
    /// <summary>Tên sheet — hiện trong tab dưới đáy Excel, không được để trống.</summary>
    public required string SheetName { get; init; }

    /// <summary>
    /// Tiêu đề tài liệu ở hàng 1, in đậm cỡ 14. Báo cáo không tiêu đề là chứng từ không nhận diện
    /// được khi rời khỏi hệ thống.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Dòng ngữ cảnh ở hàng 2 — khoảng thời gian, bộ lọc, tổng hợp, thời điểm/người xuất.
    /// Trống thì hàng 2 để trống.
    /// </summary>
    public string? Subtitle { get; init; }

    /// <summary>Tiêu đề cột ở hàng 4 — mỗi dòng dữ liệu phải có đúng số ô bằng số tiêu đề.</summary>
    public required IReadOnlyList<string> Headers { get; init; }

    /// <summary>
    /// Các dòng dữ liệu, ghi từ hàng 5. Kỳ không có dữ liệu vẫn hợp lệ — file chỉ có đầu cột để
    /// người đọc thấy "không có dòng nào", không phải một sheet trắng.
    /// </summary>
    public required IReadOnlyList<ReportExcelRow> Rows { get; init; }

    /// <summary>
    /// Độ rộng từng cột, tính theo ký tự. Bỏ trống = dùng độ rộng mặc định cho mọi cột.
    ///
    /// Cố ý đặt cứng thay vì <c>AdjustToContent()</c>: hàm đó phải đo font — đúng chỗ sinh lỗi
    /// "missing font" trên Linux — và kết quả khác nhau giữa máy dev và CI.
    /// </summary>
    public IReadOnlyList<double> ColumnWidths { get; init; } = [];
}

/// <summary>Một dòng dữ liệu — đúng bằng số ô của <see cref="ReportExcelModel.Headers"/>.</summary>
public sealed record ReportExcelRow
{
    public required IReadOnlyList<ReportExcelCell> Cells { get; init; }
}

/// <summary>Kiểu dữ liệu của một ô Excel — quyết định cách ghi và định dạng hiển thị.</summary>
public enum ReportExcelCellKind
{
    /// <summary>Chuỗi — ghi nguyên văn, Excel không diễn giải lại (SĐT không thành số).</summary>
    Text,

    /// <summary>Số thật — cộng/lọc/sort được trong Excel.</summary>
    Number,

    /// <summary>Ngày giờ thật — sort/lọc theo thời gian được.</summary>
    Date,
}

/// <summary>
/// Một ô trong file Excel. Dùng factory <see cref="Text"/>, <see cref="Number"/>,
/// <see cref="Date"/> để không tạo ra tổ hợp kiểu/giá trị sai.
///
/// Số dùng <see cref="decimal"/> chứ không <c>double</c>: quy ước A3 cấm dấu phẩy động cho tiền,
/// và decimal giữ đúng từng đồng khi ghi vào ô.
/// </summary>
public sealed record ReportExcelCell
{
    public required ReportExcelCellKind Kind { get; init; }

    /// <summary>Giá trị khi <see cref="Kind"/> = <see cref="ReportExcelCellKind.Text"/>.</summary>
    public string? StringValue { get; init; }

    /// <summary>Giá trị khi <see cref="Kind"/> = <see cref="ReportExcelCellKind.Number"/>.</summary>
    public decimal? NumericValue { get; init; }

    /// <summary>Giá trị khi <see cref="Kind"/> = <see cref="ReportExcelCellKind.Date"/>.</summary>
    public DateTime? DateTimeValue { get; init; }

    /// <summary>
    /// Định dạng hiển thị của ô số/ngày (ví dụ <c>#,##0</c>, <c>0.0</c>,
    /// <c>yyyy-mm-dd hh:mm:ss</c>). Bỏ trống thì service dùng mặc định theo kiểu.
    /// </summary>
    public string? NumberFormat { get; init; }

    /// <summary>Ô chữ — <c>null</c> thành chuỗi rỗng, không thành ô trống khó đoán.</summary>
    public static ReportExcelCell Text(string? value) => new()
    {
        Kind = ReportExcelCellKind.Text,
        StringValue = value ?? string.Empty,
    };

    /// <summary>
    /// Ô số thật. Mặc định <c>#,##0</c> — dấu phẩy nhóm hàng nghìn theo cách Excel hiển thị,
    /// không phụ thuộc culture của máy mở file.
    /// </summary>
    public static ReportExcelCell Number(decimal value, string format = "#,##0") => new()
    {
        Kind = ReportExcelCellKind.Number,
        NumericValue = value,
        NumberFormat = format,
    };

    /// <summary>Ô ngày giờ thật — bắt buộc truyền định dạng để file tự mô tả được cách đọc.</summary>
    public static ReportExcelCell Date(DateTime value, string format) => new()
    {
        Kind = ReportExcelCellKind.Date,
        DateTimeValue = value,
        NumberFormat = format,
    };
}
