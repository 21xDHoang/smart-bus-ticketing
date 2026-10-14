namespace SmartBus.Api.Dtos.Refunds;

/// <summary>
/// Một khoản hoàn tiền trả về cho chính chủ — GET /api/refunds và GET /api/refunds/{refundCode}
/// (US 8 *"Hoàn tiền tự động"*, task *"API tra cứu trạng thái hoàn tiền của giao dịch"*, dòng 42
/// Sprint 4 — Vàng Thị Dăm). Hợp đồng đầy đủ ở mục "Hoàn tiền — /refunds" của docs/api-contract.md.
///
/// Tên trường giữ NGUYÊN XI interface <c>Refund</c> trong <c>frontend/src/api/refundAdminApi.ts</c>
/// (bản nháp FE đã merge, PR #187 — màn quản trị của Băng): frontend đã code xong bảng, cột và nhãn
/// trạng thái dựa trên đúng bộ tên này, đổi một chữ là vỡ màn hình mà không ai thấy lỗi biên dịch
/// (TypeScript của họ không đọc được file này).
///
/// <b>Bảy trường nullable</b> — <see cref="TicketCode"/>, <see cref="RouteCode"/>,
/// <see cref="RouteName"/>, <see cref="Origin"/>, <see cref="Destination"/>,
/// <see cref="DepartureTime"/>, <see cref="SeatNumber"/> — đều suy từ <c>Tickets → Trips → Routes →
/// Seats</c> nên null CÙNG LÚC. Khoản hoàn nguồn <c>PaymentFailure</c> thường không có vé nào (vé chỉ
/// phát hành sau khi tiền về thành công), và đó là nhánh US 8 nêu ra đầu tiên. Bản nháp FE khai bảy
/// trường này là <c>string</c> không nullable — **FE phải nới kiểu**, xem hợp đồng mục "Bảy trường
/// nullable". Cố ý KHÔNG trả chuỗi rỗng: <c>departureTime: ""</c> đi qua <c>new Date("")</c> ra
/// <c>Invalid Date</c>.
///
/// <see cref="Source"/> và <see cref="Status"/> là CHUỖI chứ không phải enum: dự án không bật
/// <c>JsonStringEnumConverter</c> nên enum tuôn ra JSON thành con số (bẫy đã ghi ở
/// <c>TicketResponse</c> và <c>VoucherStatisticsService</c>) — service <c>ToString()</c> trong bộ nhớ
/// rồi mới đưa vào DTO.
/// </summary>
public class RefundResponse
{
    public Guid Id { get; set; }

    /// <summary>Mã khoản hoàn nội bộ, dạng "RF-xxxxxxxx" — duy nhất, là khoá tra cứu bằng mắt.</summary>
    public string RefundCode { get; set; } = string.Empty;

    /// <summary><c>PaymentFailure</c> / <c>TicketCancellation</c> — xem <c>Entities.RefundSource</c>.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Mã vé bị hoàn — null khi khoản hoàn không gắn vé nào (nhánh giao dịch hỏng).</summary>
    public string? TicketCode { get; set; }

    /// <summary>Mã tuyến hiển thị — "01", "B10"; null cùng lúc với <see cref="TicketCode"/>.</summary>
    public string? RouteCode { get; set; }

    public string? RouteName { get; set; }

    public string? Origin { get; set; }

    public string? Destination { get; set; }

    /// <summary>Giờ khởi hành của chuyến (UTC) — null khi khoản hoàn không gắn vé.</summary>
    public DateTime? DepartureTime { get; set; }

    public string? SeatNumber { get; set; }

    /// <summary>
    /// Họ tên người NHẬN LẠI tiền (<c>Users.FullName</c>) — chép sẵn ở tầng CSDL để màn quản trị vẽ
    /// một dòng mà không phải gọi thêm endpoint nào. Màn hành khách không hiện trường này (mọi dòng
    /// đều là "của tôi") nhưng vẫn nhận, vì hai màn dùng chung một hình dạng.
    /// </summary>
    public string PassengerName { get; set; } = string.Empty;

    /// <summary>Số tiền hoàn (VND) — con số hệ thống ĐÃ gửi lên cổng, không phải con số tính lại.</summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Mã phương thức của giao dịch gốc — <c>MoMo</c> / <c>VNPay</c> / <c>ZaloPay</c> / <c>BankCard</c>.
    ///
    /// Là MÃ PHƯƠNG THỨC của hợp đồng (cột <c>Payments.MethodCode</c>), không phải tên hiển thị của
    /// cổng: <c>BankCard</c> chạy qua kênh thẻ của VNPay chứ không có cổng riêng. Trường tên là
    /// <c>methodName</c> vì bản nháp FE đã đặt vậy — dịch mã sang nhãn tiếng Việt là việc của
    /// frontend, cùng lối <c>REFUND_STATUS_META</c> của họ.
    /// </summary>
    public string MethodName { get; set; } = string.Empty;

    /// <summary>Mã giao dịch gốc (<c>Payments.PaymentCode</c>) — khoá tra theo giao dịch.</summary>
    public string PaymentCode { get; set; } = string.Empty;

    /// <summary>Mã giao dịch phía cổng của lượt hoàn THÀNH CÔNG; null khi chưa có lượt nào thành công.</summary>
    public string? GatewayTransactionId { get; set; }

    /// <summary><c>Pending</c> / <c>Succeeded</c> / <c>Failed</c> — xem <c>Entities.RefundStatus</c>.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Lý do lượt gọi GẦN NHẤT thất bại. Bất biến: chỉ khác null khi <see cref="Status"/> là
    /// <c>Failed</c> — khoản <c>Pending</c> chưa có câu trả lời nào để mà trích, khoản
    /// <c>Succeeded</c> thì lý do cũ đã bị xoá khi lật trạng thái.
    /// </summary>
    public string? FailReason { get; set; }

    /// <summary>
    /// Số lượt GỌI LẠI cổng — không tính lượt gọi đầu tiên, nên dòng vừa tạo luôn là <c>0</c>.
    ///
    /// Là phép ĐẾM trên <c>RefundLogs</c> (<c>Trigger &lt;&gt; 'Initial'</c>) chứ không phải một cột
    /// của <c>Refunds</c> — bảng <c>Refunds</c> cố ý không có cột đếm, lý do ở
    /// <c>docs/35-csdl-hoan-tien.md</c> §2.4; chỗ cài đặt là phép chiếu <c>RetryCount</c> trong
    /// <c>RefundLookupService</c>. Định nghĩa đầy đủ: docs/api-contract.md mục
    /// "<c>retryCount</c> — con số này đếm ở đâu ra".
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>Thời điểm hệ thống tạo khoản hoàn — danh sách sắp mới nhất trước theo cột này.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Thời điểm cổng xác nhận kết quả CUỐI; null khi khoản còn <c>Pending</c>.</summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>Chỉ khác null khi dòng đã từng bị sửa (thử lại, lật trạng thái).</summary>
    public DateTime? UpdatedAt { get; set; }
}
