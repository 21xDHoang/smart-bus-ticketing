namespace SmartBus.Api.Dtos.Payments;

/// <summary>
/// Nội dung IPN (Instant Payment Notification) MoMo gửi về <c>ipnUrl</c> của chúng ta khi giao
/// dịch có kết quả — đầu vào của <see cref="Services.IMoMoGatewayService.IsValidCallback"/>.
///
/// MoMo gửi server-to-server (JSON POST) và yêu cầu trả lời trong 15 giây. Tin callback chỉ sau
/// khi qua HAI kiểm tra của tài liệu MoMo: (1) chữ ký <see cref="Signature"/> hợp lệ với secretKey,
/// (2) đối chiếu <see cref="PartnerCode"/>/<see cref="OrderId"/>/<see cref="Amount"/> với bản ghi
/// giao dịch của mình — bước 2 cần bảng Payments (migration của Vàng Thị Dăm) nên endpoint callback
/// sẽ làm trọn khi bảng có.
///
/// Đặt tên trường camelCase đúng chuẩn JSON mà MoMo gửi — System.Text.Json mặc định của
/// ASP.NET Core cũng dùng camelCase nên map thẳng.
/// </summary>
public class MoMoCallback
{
    /// <summary>Mã đối tác — phải khớp PartnerCode đã cấu hình.</summary>
    public string PartnerCode { get; set; } = string.Empty;

    /// <summary>Mã đơn hàng chúng ta gửi lúc tạo giao dịch — khoá để tìm bản ghi Payment.</summary>
    public string OrderId { get; set; } = string.Empty;

    /// <summary>Mã định danh request lúc tạo giao dịch.</summary>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>Số tiền khách đã trả (VND) — phải khớp Amount của bản ghi giao dịch.</summary>
    public long Amount { get; set; }

    public string OrderInfo { get; set; } = string.Empty;

    /// <summary>Loại đơn — MoMo gửi "momo_wallet" cho ví MoMo.</summary>
    public string OrderType { get; set; } = string.Empty;

    /// <summary>Mã giao dịch do MoMo sinh — lưu làm gatewayTransactionId để đối soát.</summary>
    public long TransId { get; set; }

    /// <summary>0 = thành công · 9000 = đã xác thực, chờ chụp tiền · khác = thất bại.</summary>
    public int ResultCode { get; set; }

    public string Message { get; set; } = string.Empty;

    /// <summary>Kênh thanh toán — "webApp", "app", "qr" hoặc "miniapp".</summary>
    public string PayType { get; set; } = string.Empty;

    /// <summary>Thời điểm MoMo ghi nhận kết quả (epoch mili giây).</summary>
    public long ResponseTime { get; set; }

    /// <summary>Dữ liệu riêng đi kèm — chuỗi chúng ta gửi lúc tạo giao dịch.</summary>
    public string ExtraData { get; set; } = string.Empty;

    /// <summary>Chữ ký HMAC-SHA256 của MoMo — kiểm bằng <see cref="Services.MoMoGatewayService"/>.</summary>
    public string Signature { get; set; } = string.Empty;
}
