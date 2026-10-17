using ClosedXML.Excel;

namespace SmartBus.Api.Services;

/// <summary>
/// Dựng file Excel báo cáo bằng ClosedXML — service dùng chung cho mọi báo cáo (task
/// "Service dùng chung để xuất Excel (ClosedXML) cho mọi báo cáo" — US 21 "Xuất dữ liệu",
/// Sprint 5 dòng 61, Nguyễn Duy Kiên).
///
/// Service KHÔNG biết nghiệp vụ: bên gọi truyền <see cref="ReportExcelModel"/> đã gom sẵn dữ liệu
/// và chọn kiểu từng ô. Nhờ vậy ba báo cáo (doanh thu, tỷ lệ lấp đầy, nhật ký) dùng chung đúng
/// một khuôn file và không phải chép lại code ClosedXML.
///
/// Khuôn cố định (docs/49 §2.7):
///
/// <code>
/// hàng 1: tiêu đề tài liệu (bold 14)
/// hàng 2: dòng ngữ cảnh (khoảng thời gian, bộ lọc, người xuất…)
/// hàng 3: trống
/// hàng 4: tiêu đề cột (bold) + auto-filter + cố định hàng
/// hàng 5+: dữ liệu
/// </code>
///
/// Ô số/ngày là GIÁ TRỊ THẬT để cộng/lọc/sort được trong Excel; ô chữ giữ nguyên chuỗi để Excel
/// không diễn giải số điện thoại thành số. Độ rộng cột đặt cứng, không gọi
/// <c>AdjustToContent()</c> — hàm đó đo font và vỡ trên Linux.
/// </summary>
public sealed class ReportExcelService : IReportExcelService
{
    private const int TitleRow = 1;
    private const int SubtitleRow = 2;
    private const int HeaderRow = 4;
    private const int FirstDataRow = HeaderRow + 1;

    private const double DefaultColumnWidth = 14;
    private const string DefaultNumberFormat = "#,##0";
    private const string DefaultDateFormat = "yyyy-mm-dd hh:mm:ss";

    public byte[] GenerateExcel(ReportExcelModel model)
    {
        Validate(model);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(model.SheetName);

        // Khối tự mô tả: file xuất ra rời khỏi hệ thống (thư mục tải về, email, bản in) nên nó
        // phải tự nói nó phủ khoảng nào và ai xuất lúc nào.
        var titleCell = sheet.Cell(TitleRow, 1);
        titleCell.Value = model.Title;
        titleCell.Style.Font.Bold = true;
        titleCell.Style.Font.FontSize = 14;

        if (!string.IsNullOrWhiteSpace(model.Subtitle))
        {
            sheet.Cell(SubtitleRow, 1).Value = model.Subtitle;
        }

        for (var columnIndex = 0; columnIndex < model.Headers.Count; columnIndex++)
        {
            var cell = sheet.Cell(HeaderRow, columnIndex + 1);
            cell.Value = model.Headers[columnIndex];
            cell.Style.Font.Bold = true;
        }

        var rowIndex = FirstDataRow;

        foreach (var row in model.Rows)
        {
            for (var columnIndex = 0; columnIndex < row.Cells.Count; columnIndex++)
            {
                WriteCell(sheet.Cell(rowIndex, columnIndex + 1), row.Cells[columnIndex]);
            }

            rowIndex++;
        }

        for (var columnIndex = 0; columnIndex < model.Headers.Count; columnIndex++)
        {
            sheet.Column(columnIndex + 1).Width = columnIndex < model.ColumnWidths.Count
                ? model.ColumnWidths[columnIndex]
                : DefaultColumnWidth;
        }

        sheet.Range(HeaderRow, 1, HeaderRow, model.Headers.Count).SetAutoFilter();
        sheet.SheetView.FreezeRows(HeaderRow);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return stream.ToArray();
    }

    private static void WriteCell(IXLCell cell, ReportExcelCell source)
    {
        switch (source.Kind)
        {
            case ReportExcelCellKind.Text:
                cell.Value = source.StringValue ?? string.Empty;
                break;

            case ReportExcelCellKind.Number:
                cell.Value = source.NumericValue!.Value;
                cell.Style.NumberFormat.Format = source.NumberFormat ?? DefaultNumberFormat;
                break;

            case ReportExcelCellKind.Date:
                cell.Value = source.DateTimeValue!.Value;
                cell.Style.DateFormat.Format = source.NumberFormat ?? DefaultDateFormat;
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(source), source.Kind, "Kiểu ô Excel không được hỗ trợ.");
        }
    }

    private static void Validate(ReportExcelModel model)
    {
        if (string.IsNullOrWhiteSpace(model.SheetName))
        {
            throw new ArgumentException("Tên sheet không được để trống.", nameof(model));
        }

        if (string.IsNullOrWhiteSpace(model.Title))
        {
            throw new ArgumentException("Tiêu đề báo cáo không được để trống.", nameof(model));
        }

        if (model.Headers.Count == 0)
        {
            throw new ArgumentException("Báo cáo phải có ít nhất một cột.", nameof(model));
        }

        if (model.ColumnWidths.Count > 0 && model.ColumnWidths.Count != model.Headers.Count)
        {
            throw new ArgumentException(
                "Số độ rộng cột phải bằng số cột của báo cáo.", nameof(model));
        }

        foreach (var width in model.ColumnWidths)
        {
            if (width <= 0)
            {
                throw new ArgumentException("Độ rộng cột phải lớn hơn 0.", nameof(model));
            }
        }

        foreach (var row in model.Rows)
        {
            if (row.Cells.Count != model.Headers.Count)
            {
                throw new ArgumentException(
                    "Số ô của một dòng phải bằng số cột của báo cáo.", nameof(model));
            }

            foreach (var cell in row.Cells)
            {
                ValidateCell(cell);
            }
        }
    }

    private static void ValidateCell(ReportExcelCell cell)
    {
        switch (cell.Kind)
        {
            case ReportExcelCellKind.Text:
                if (cell.StringValue is null)
                {
                    throw new ArgumentException("Ô chữ phải có giá trị.", nameof(cell));
                }

                break;

            case ReportExcelCellKind.Number:
                if (cell.NumericValue is null)
                {
                    throw new ArgumentException("Ô số phải có giá trị.", nameof(cell));
                }

                break;

            case ReportExcelCellKind.Date:
                if (cell.DateTimeValue is null)
                {
                    throw new ArgumentException("Ô ngày phải có giá trị.", nameof(cell));
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(cell), cell.Kind, "Kiểu ô Excel không được hỗ trợ.");
        }
    }
}
