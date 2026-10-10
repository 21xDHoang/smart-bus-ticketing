namespace SmartBus.Api.Dtos.Payments;

/// <summary>
/// Kết quả KHỞI TẠO thanh toán — đầu ra của <see cref="Services.IPaymentGateway.InitiateAsync"/>,
/// đã chuẩn hoá khỏi hình dạng riêng của từng cổng để endpoint POST /payments trả cho FE bằng một
/// hình dạng duy nhất.
/// </summary>
public class PaymentInitiationResult
{
    /// <summary>
    /// Cổng CHẤP NHẬN yêu cầu (MoMo: resultCode = 0). VNPay luôn true — không có bước "cổng từ
    /// chối lúc tạo" vì ta ký URL tại chỗ; thành hay hỏng thực sự chỉ biết qua callback.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// URL chuyển khách sang trang trả tiền: MoMo là payUrl cổng trả về, VNPay là URL vpcpay.html
    /// đã ký sẵn. Khác rỗng khi <see cref="Success"/> là true.
    /// </summary>
    public string RedirectUrl { get; set; } = string.Empty;

    /// <summary>Deeplink mở app MoMo trên điện thoại — chỉ MoMo có, cổng khác để null.</summary>
    public string? Deeplink { get; set; }

    /// <summary>Link ảnh QR của giao dịch — chỉ MoMo có, cổng khác để null.</summary>
    public string? QrCodeUrl { get; set; }

    /// <summary>Thông báo cổng trả kèm khi <see cref="Success"/> là false — trả nguyên văn cho người gọi.</summary>
    public string Message { get; set; } = string.Empty;
}
