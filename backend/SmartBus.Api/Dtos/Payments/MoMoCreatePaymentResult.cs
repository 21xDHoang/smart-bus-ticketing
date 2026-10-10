namespace SmartBus.Api.Dtos.Payments;

/// <summary>
/// Kết quả gọi tạo giao dịch MoMo (<see cref="Services.IMoMoGatewayService.CreatePaymentAsync"/>).
/// Cổng trả <c>resultCode = 0</c> là chấp nhận tạo giao dịch — lúc này khách mới được đưa sang
/// <see cref="PayUrl"/> để thanh toán; giao dịch CHƯA thành công cho tới khi IPN báo <c>0</c>.
/// </summary>
public class MoMoCreatePaymentResult
{
    /// <summary>MoMo chấp nhận tạo giao dịch (<c>resultCode = 0</c>) và có <see cref="PayUrl"/> để chuyển khách.</summary>
    public bool Success { get; set; }

    /// <summary>URL thanh toán MoMo — FE redirect khách sang đây rồi chuyển sang màn chờ kết quả.</summary>
    public string PayUrl { get; set; } = string.Empty;

    /// <summary>Deeplink mở app MoMo trên điện thoại — null khi cổng không trả.</summary>
    public string? Deeplink { get; set; }

    /// <summary>Ảnh QR của giao dịch — null khi cổng không trả.</summary>
    public string? QrCodeUrl { get; set; }

    /// <summary>Mã kết quả do MoMo trả — 0 là chấp nhận, khác 0 kèm <see cref="Message"/> giải thích.</summary>
    public int ResultCode { get; set; }

    /// <summary>Thông báo do MoMo trả — để trả nguyên văn cho người gọi khi <see cref="Success"/> là false.</summary>
    public string Message { get; set; } = string.Empty;
}
