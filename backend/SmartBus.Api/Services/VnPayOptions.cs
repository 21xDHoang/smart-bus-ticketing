namespace SmartBus.Api.Services;

/// <summary>
/// Cấu hình cổng thanh toán VNPay — đọc từ section <c>VnPay</c> của cấu hình (US 6 "Cổng thanh toán").
///
/// 🔴 LUẬT 2 — repo PUBLIC: <c>TmnCode</c>/<c>HashSecret</c> là bí mật đối tác, KHÔNG BAO GIỜ nằm
/// trong code hay appsettings mẫu. Máy dev đặt qua <c>dotnet user-secrets</c>, khi deploy đặt qua
/// biến môi trường <c>VnPay__HashSecret</c>… — đúng khuôn MoMoOptions/JWT hiện có.
///
/// Đường dẫn cổng là thứ công khai nên có giá trị mặc định là sandbox của VNPay. Lên thật
/// (production) thì đổi qua cấu hình, không phải sửa code.
/// </summary>
public class VnPayOptions
{
    public const string SectionName = "VnPay";

    /// <summary>
    /// Đường dẫn cổng thanh toán — mặc định sandbox. Production đổi qua cấu hình
    /// (<c>https://vnpayment.vn/paymentv2/vpcpay.html</c>).
    /// </summary>
    public const string DefaultPaymentUrl = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html";

    /// <summary>Mã website (terminal) do VNPay cấp — ví dụ sandbox công khai "CGXZLS0Z".</summary>
    public string TmnCode { get; set; } = string.Empty;

    /// <summary>Khoá bí mật dùng ký HMAC-SHA512 — TUYỆT ĐỐI không lộ ra ngoài (luật 2).</summary>
    public string HashSecret { get; set; } = string.Empty;

    /// <summary>URL cổng thanh toán — mặc định sandbox <see cref="DefaultPaymentUrl"/>.</summary>
    public string PaymentUrl { get; set; } = DefaultPaymentUrl;

    /// <summary>
    /// Thiếu hai giá trị đối tác thì URL dựng ra sẽ bị cổng từ chối với lỗi chữ ký khó hiểu — kiểm
    /// sớm ngay khi dựng service để lỗi nói đúng chỗ thiếu, cùng lối <see cref="MoMoOptions.MissingPiece"/>.
    /// </summary>
    public string? MissingPiece()
    {
        if (string.IsNullOrWhiteSpace(TmnCode))
        {
            return "VnPay:TmnCode";
        }

        if (string.IsNullOrWhiteSpace(HashSecret))
        {
            return "VnPay:HashSecret";
        }

        return null;
    }
}
