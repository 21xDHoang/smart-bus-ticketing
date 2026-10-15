namespace SmartBus.Api.Dtos.TicketValidations;

/// <summary>
/// Vé mà một lượt soát tra được — phần <c>ticket</c> của POST /api/tickets/validate.
///
/// Khớp đúng interface <c>ValidatedTicket</c> mà màn quét QR đã viết sẵn ở
/// <c>frontend/src/api/ticketValidationApi.ts</c> (Nguyễn Đình Băng, dòng 20), CỘNG thêm
/// <see cref="UsedAt"/> — docs/29 §3 ghi lý do <c>AlreadyUsed</c> phải "kèm usedAt để phụ xe đối
/// chiếu". Khác bảng trường "Hình dạng Ticket" của /tickets ở hai điểm, đều là chủ ý của màn soát:
/// (1) có <see cref="PassengerName"/> — phụ xe đối chiếu khách đứng trước mặt;
/// (2) bỏ <c>origin</c>/<c>destination</c>/<c>price</c>/<c>createdAt</c> — phụ xe không cần chúng
/// lúc soát, và đây là response của màn soát chứ không phải màn "vé của tôi".
///
/// Các trường tên tuyến / số ghế / tên trạm là tên HIỂN THỊ do service nối sẵn, cùng lối
/// <see cref="Tickets.TicketResponse"/> — FE không gọi thêm endpoint nào để vẽ kết quả quét.
/// </summary>
public class ValidatedTicketResponse
{
    public Guid Id { get; init; }

    /// <summary>Mã vé = nội dung mã QR in trên vé, là thứ cổng soát vé đọc được.</summary>
    public string Code { get; init; } = string.Empty;

    public string RouteCode { get; init; } = string.Empty;

    public string RouteName { get; init; } = string.Empty;

    /// <summary>Giờ khởi hành của chuyến (UTC) — để phụ xe nhận ra "vé này cho chuyến sáng hay chiều".</summary>
    public DateTime DepartureTime { get; init; }

    public string SeatNumber { get; init; } = string.Empty;

    /// <summary>Tên trạm lên — null khi vé không có trạm cụ thể (vé mua ở màn chọn ghế có thể lên ở bến đầu).</summary>
    public string? BoardingStopName { get; init; }

    /// <summary>Tên trạm xuống — cùng tính chất hiển thị như <see cref="BoardingStopName"/>.</summary>
    public string? AlightingStopName { get; init; }

    /// <summary>Họ tên hành khách chủ vé — null khi không tra được tài khoản.</summary>
    public string? PassengerName { get; init; }

    /// <summary><c>Paid</c> / <c>Used</c> / <c>Cancelled</c> — xem <see cref="Entities.TicketStatus"/>.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Thời điểm vé đã được soát lần đầu — chỉ khác null khi <see cref="Status"/> = "Used".</summary>
    public DateTime? UsedAt { get; init; }
}
