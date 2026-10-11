namespace SmartBus.Api.Dtos.Tickets;

/// <summary>
/// Một vé điện tử trả về cho chính chủ — GET /api/tickets/me và GET /api/tickets/{id}
/// (US 4, task *"API tra cứu vé của tôi (đã mua / đã dùng / đã huỷ)"* — Trần Trung Hiếu, dòng 45).
/// Hình dạng khớp đúng bảng trường "Hình dạng Ticket" của docs/api-contract.md và interface
/// <c>Ticket</c> trong bản nháp FE (frontend/src/api/ticketApi.ts).
///
/// Các trường tên tuyến / điểm đi / điểm đến / tên trạm là tên HIỂN THỊ được service nối sẵn từ
/// <c>Trips → Routes</c>, <c>Seats</c> và <c>Stops</c> — FE không phải gọi thêm endpoint nào để
/// vẽ một dòng vé.
///
/// <see cref="Status"/> là chuỗi chứ không phải enum: dự án không bật JsonStringEnumConverter nên
/// enum tuôn ra JSON thành con số (lỗi dễ mắc ở VoucherStatisticsService); service cố ý
/// <c>ToString()</c> trong bộ nhớ rồi mới đưa vào DTO — cùng lối <see cref="Vouchers.VoucherStatisticsItemResponse"/>.
/// </summary>
public class TicketResponse
{
    public Guid Id { get; set; }

    /// <summary>Mã vé = nội dung mã QR in trên vé, là thứ cổng soát vé đọc được.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Mã tuyến hiển thị — "01", "B10".</summary>
    public string RouteCode { get; set; } = string.Empty;

    public string RouteName { get; set; } = string.Empty;

    public string Origin { get; set; } = string.Empty;

    public string Destination { get; set; } = string.Empty;

    /// <summary>Giờ khởi hành của chuyến (UTC) — căn cứ để FE suy "hết hạn" (chuyến đã đi mà chưa soát).</summary>
    public DateTime DepartureTime { get; set; }

    public string SeatNumber { get; set; } = string.Empty;

    /// <summary>
    /// Tên trạm lên — null khi vé không có trạm cụ thể (vé mua ở màn chọn ghế có thể lên ở bến đầu).
    /// CHỈ để hiển thị, không phải khoá nghiệp vụ (A8.1).
    /// </summary>
    public string? BoardingStopName { get; set; }

    /// <summary>Tên trạm xuống — cùng tính chất hiển thị như <see cref="BoardingStopName"/>.</summary>
    public string? AlightingStopName { get; set; }

    /// <summary>Giá đã thanh toán (VND) — ảnh chụp lúc phát hành, không tra lại bảng giá.</summary>
    public decimal Price { get; set; }

    /// <summary><c>Paid</c> / <c>Used</c> / <c>Cancelled</c> — xem <see cref="Entities.TicketStatus"/>.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Thời điểm phát hành (tiền đã về) — FE hiển thị "ngày mua".</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Thời điểm soát vé — chỉ khác null khi <see cref="Status"/> = "Used".</summary>
    public DateTime? UsedAt { get; set; }
}
