using System.Globalization;

namespace SmartBus.Api.Services;

/// <summary>
/// Dữ liệu để vẽ MỘT tấm vé PDF (task dòng 42 — *"Sinh file PDF vé có mã QR + gửi kèm qua email"*,
/// US 4, Sprint 3).
///
/// Record PHẲNG, cố ý không tham chiếu entity: <see cref="TicketPdfService"/> không chạm CSDL,
/// không kéo <c>DbContext</c> vào tầng PDF. Bên gọi (luồng phát hành vé — Hiếu) tự gom từ
/// Ticket/Trip/Route/Seat/User rồi truyền vào; nhờ vậy service thuần, test được không cần CSDL,
/// và đổi hình dạng entity không làm vỡ tầng vẽ vé.
/// </summary>
public sealed record TicketPdfModel
{
    /// <summary>Id vé — in nhỏ ở chân vé để tra cứu khi hành khách gọi lên hỗ trợ.</summary>
    public required Guid TicketId { get; init; }

    /// <summary>
    /// Mã QR **đã lưu** trong <c>Tickets.Code</c> (106 ký tự — dòng 39, xem
    /// <see cref="ITicketQrService"/>). KHÔNG sinh lại ở đây: mỗi lượt <c>GenerateCode</c> ra một mã
    /// khác nhau (nonce ngẫu nhiên), in mã mới là vé in không khớp mã đã phát hành và không khớp
    /// mã soát được (docs/28 §6, docs/30 §3.1).
    /// </summary>
    public required string Code { get; init; }

    /// <summary>Họ tên hành khách — in trên vé để người soát đối chiếu.</summary>
    public required string PassengerName { get; init; }

    /// <summary>Tên tuyến — tiêu đề tấm vé (Route.Name).</summary>
    public required string RouteName { get; init; }

    /// <summary>
    /// Thời điểm khởi hành — PHẢI là UTC (chốt <c>timestamptz</c> của dự án). Giá trị Kind
    /// <c>Unspecified</c> bị COI NHƯ UTC thay vì đi qua <c>ToUniversalTime()</c>: hàm đó lấy múi
    /// giờ của máy đang chạy, cùng một vé in trên máy dev Việt Nam và trên CI ubuntu sẽ ra giờ
    /// khác nhau — lệch không tái hiện được.
    /// </summary>
    public required DateTime DepartureTime { get; init; }

    /// <summary>Số ghế (Seat.SeatNumber — chuỗi, giữ nguyên như dữ liệu).</summary>
    public required string SeatNumber { get; init; }

    /// <summary>Giá vé chốt lúc phát hành (snapshot trên <c>Ticket.Price</c>, numeric(12,2)).</summary>
    public required decimal Price { get; init; }

    /// <summary>Biển số xe — có thì in, không có thì bỏ dòng.</summary>
    public string? LicensePlate { get; init; }

    /// <summary>Điểm lên xe — chỉ để hiển thị (chốt: vé đặt theo cả chuyến, A8 mục 1).</summary>
    public string? BoardingStopName { get; init; }

    /// <summary>Điểm xuống xe — chỉ để hiển thị.</summary>
    public string? AlightingStopName { get; init; }

    /// <summary>
    /// Múi giờ Việt Nam — cố định +7, **không có DST** từ 1975. Cố ý KHÔNG dùng
    /// <c>TimeZoneInfo.FindSystemTimeZoneById</c>: tên vùng khác nhau giữa các hệ điều hành
    /// (Windows "SE Asia Standard Time" vs Linux "Asia/Ho_Chi_Minh") — CI ubuntu sẽ ném
    /// <c>TimeZoneNotFoundException</c> với tên kiểu Windows.
    /// </summary>
    private const int VietnamUtcOffsetHours = 7;

    /// <summary>Giờ khởi hành theo đồng hồ Việt Nam (UTC+7) — thứ in lên vé cho hành khách.</summary>
    public DateTime DepartureTimeVietnam =>
        (DepartureTime.Kind == DateTimeKind.Utc ? DepartureTime : DateTime.SpecifyKind(DepartureTime, DateTimeKind.Utc))
        .AddHours(VietnamUtcOffsetHours);

    /// <summary>Giờ khởi hành dạng chữ — "08:30 15/10/2026". InvariantCulture: chỉ để tránh mọi
    /// ảnh hưởng lịch/múi giờ của máy, bản thân chuỗi không có phần số cần bản địa hoá.</summary>
    public string DepartureText => DepartureTimeVietnam.ToString("HH:mm dd/MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>
    /// Giá vé dạng chữ — "150.000 ₫" (nhóm nghìn bằng dấu chấm theo vi-VN, ký hiệu đồng).
    /// Test ghim đúng chuỗi này: đổi culture hay định dạng là đỏ ngay, không đợi khách nhìn thấy
    /// con số sai trên vé.
    /// </summary>
    public string PriceText => Price.ToString("N0", CultureInfo.GetCultureInfo("vi-VN")) + " ₫";
}
