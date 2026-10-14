using System.Globalization;

namespace SmartBus.Api.Services;

/// <summary>
/// Dữ liệu để vẽ MỘT báo cáo PDF (task dòng 77 Sprint 4 — *"Service xuất PDF có tiêu đề, logo,
/// ngày xuất"*, US 21 "Xuất dữ liệu", Trần Trung Hiếu).
///
/// Record PHẲNG, cố ý không tham chiếu entity: <see cref="ReportPdfService"/> không chạm CSDL,
/// không kéo <c>DbContext</c> vào tầng vẽ. Bên gọi (API xuất báo cáo doanh thu / lấp đầy / nhật
/// ký — task của Hoàng) tự gom dữ liệu rồi truyền vào; nhờ vậy service thuần, test được không
/// cần CSDL, và đổi hình dạng entity không làm vỡ tầng vẽ báo cáo.
/// </summary>
public sealed record ReportPdfModel
{
    /// <summary>
    /// Tiêu đề báo cáo — dòng đầu trang, in đậm. Thiếu thì ném <see cref="ArgumentException"/>:
    /// báo cáo không tiêu đề là chứng từ không nhận diện được khi in ra khỏi hệ thống.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Logo in góc trái đầu trang — chuỗi SVG (markup hoàn chỉnh, có thẻ <c>&lt;svg&gt;</c>).
    /// Trống/whitespace = cố ý không in logo. Chọn SVG thay vì ảnh bitmap cùng lý do mã QR trên
    /// vé (xem <see cref="TicketPdfService"/> chốt 1): vector in cỡ nào cũng sắc, và không kéo
    /// đường giải mã ảnh native vốn dễ vỡ giữa máy dev Windows và CI ubuntu.
    /// </summary>
    public string? LogoSvg { get; init; }

    /// <summary>
    /// Thời điểm xuất báo cáo — PHẢI là UTC (chốt <c>timestamptz</c> của dự án). Giá trị Kind
    /// <c>Unspecified</c> bị COI NHƯ UTC thay vì đi qua <c>ToUniversalTime()</c>: hàm đó lấy múi
    /// giờ của máy đang chạy, cùng một báo cáo xuất trên máy dev Việt Nam và trên CI ubuntu sẽ
    /// ra giờ khác nhau. In ra là giờ Việt Nam — xem <see cref="ExportedAtText"/>.
    /// </summary>
    public required DateTime ExportedAt { get; init; }

    /// <summary>
    /// Dòng phụ dưới tiêu đề — thường là khoảng thời gian báo cáo, ví dụ
    /// "01/10/2026 → 14/10/2026". Có thì in, không có thì bỏ.
    /// </summary>
    public string? Subtitle { get; init; }

    /// <summary>
    /// Người xuất (họ tên hoặc tài khoản) — in cạnh ngày xuất trên cùng dòng tự mô tả. Có thì
    /// in, không có thì bỏ.
    /// </summary>
    public string? GeneratedBy { get; init; }

    /// <summary>
    /// Bảng số liệu của báo cáo — có thì in, không có thì bỏ (báo cáo chỉ có chữ vẫn hợp lệ).
    /// </summary>
    public ReportTable? Table { get; init; }

    /// <summary>
    /// Ghi chú cuối trang, bên trái số trang — ví dụ nguồn số liệu. Có thì in, không có thì bỏ.
    /// </summary>
    public string? FooterNote { get; init; }

    /// <summary>
    /// Múi giờ Việt Nam — cố định +7, **không có DST** từ 1975. Cố ý KHÔNG dùng
    /// <c>TimeZoneInfo.FindSystemTimeZoneById</c>: tên vùng khác nhau giữa các hệ điều hành
    /// (Windows "SE Asia Standard Time" vs Linux "Asia/Ho_Chi_Minh") — CI ubuntu sẽ ném
    /// <c>TimeZoneNotFoundException</c> với tên kiểu Windows.
    /// </summary>
    private const int VietnamUtcOffsetHours = 7;

    /// <summary>Giờ xuất theo đồng hồ Việt Nam (UTC+7) — thứ in lên báo cáo cho người đọc.</summary>
    public DateTime ExportedAtVietnam =>
        (ExportedAt.Kind == DateTimeKind.Utc ? ExportedAt : DateTime.SpecifyKind(ExportedAt, DateTimeKind.Utc))
        .AddHours(VietnamUtcOffsetHours);

    /// <summary>
    /// Ngày xuất dạng chữ — "09:30 14/10/2026" (cùng khuôn với
    /// <see cref="TicketPdfModel.DepartureText"/>). InvariantCulture: chỉ để tránh mọi ảnh hưởng
    /// lịch/múi giờ của máy, bản thân chuỗi không có phần số cần bản địa hoá.
    /// </summary>
    public string ExportedAtText => ExportedAtVietnam.ToString("HH:mm dd/MM/yyyy", CultureInfo.InvariantCulture);
}

/// <summary>
/// Bảng số liệu in trong báo cáo — đầu bảng là hàng tiêu đề <see cref="Headers"/> (in đậm, nền
/// xám nhạt, lặp lại trên mỗi trang), dưới là các dòng <see cref="Rows"/>. Ô là chuỗi in sẵn —
/// bên gọi tự định dạng số tiền/giờ (cùng lối <see cref="TicketPdfModel.PriceText"/>), service
/// không đoán kiểu dữ liệu.
/// </summary>
public sealed record ReportTable
{
    /// <summary>Tiêu đề cột. Rỗng + có dòng dữ liệu thì ném <see cref="ArgumentException"/>.</summary>
    public required IReadOnlyList<string> Headers { get; init; }

    /// <summary>
    /// Các dòng dữ liệu — mỗi dòng phải có ĐÚNG bằng số cột của <see cref="Headers"/>, lệch thì
    /// ném <see cref="ArgumentException"/>: bảng lệch cột in ra là chứng từ sai số liệu.
    /// </summary>
    public required IReadOnlyList<IReadOnlyList<string>> Rows { get; init; }
}
