namespace SmartBus.Api.Services;

/// <summary>
/// Cấu hình phần hoàn tiền của VNPay — đọc từ section <c>VnPay</c> của cấu hình (US 8 — task
/// *"API gọi cổng thanh toán để hoàn tiền"*, dòng 29 — Trần Trung Hiếu).
///
/// Vì sao tách class riêng thay vì thêm trường vào <see cref="VnPayOptions"/>: class kia (file của
/// chiều THU tiền) không có trường cho API hoàn — endpoint hoàn là một URL KHÁC hẳn
/// (<c>merchant_webapi/api/transaction</c>), không suy ra được từ <c>PaymentUrl</c>. Tách class mới
/// gắn CÙNG section <c>VnPay</c> thì <c>TmnCode</c>/<c>HashSecret</c> dùng chung cấu hình sẵn có
/// (<c>VnPay__TmnCode</c> / <c>VnPay__HashSecret</c>), không thêm bí mật mới nào, còn endpoint hoàn
/// đổi qua <c>VnPay__RefundEndpoint</c> khi lên thật (production là host <c>merchant.vnpayment.vn</c>).
///
/// 🔴 LUẬT 2 — repo PUBLIC: <c>TmnCode</c>/<c>HashSecret</c> là bí mật đối tác, KHÔNG BAO GIỜ nằm
/// trong code hay appsettings mẫu. Máy dev đặt qua <c>dotnet user-secrets</c>, khi deploy đặt qua
/// biến môi trường — đúng khuôn <see cref="VnPayOptions"/> hiện có.
///
/// ⚠️ Cổng sandbox của VNPay HẠN CHẾ chức năng hoàn tiền — merchant cần liên hệ VNPAY để mở. Trước
/// khi có, lượt gọi hoàn VNPay trên sandbox chỉ kiểm được bằng HTTP giả trong test (xem
/// <c>docs/43-api-goi-cong-hoan-tien.md</c>).
/// </summary>
public class VnPayRefundOptions
{
    public const string SectionName = "VnPay";

    /// <summary>Endpoint API hoàn tiền — mặc định sandbox, production đổi qua cấu hình <c>VnPay__RefundEndpoint</c>.</summary>
    public const string DefaultRefundEndpoint = "https://sandbox.vnpayment.vn/merchant_webapi/api/transaction";

    /// <summary>Mã website (terminal) do VNPay cấp — cùng giá trị với <see cref="VnPayOptions.TmnCode"/>.</summary>
    public string TmnCode { get; set; } = string.Empty;

    /// <summary>Khoá bí mật dùng ký HMAC-SHA512 — cùng giá trị với <see cref="VnPayOptions.HashSecret"/>.</summary>
    public string HashSecret { get; set; } = string.Empty;

    /// <summary>Endpoint API hoàn tiền — mặc định sandbox <see cref="DefaultRefundEndpoint"/>.</summary>
    public string RefundEndpoint { get; set; } = DefaultRefundEndpoint;

    /// <summary>
    /// Thiếu hai giá trị đối tác thì mọi cuộc gọi cổng đều chết với lỗi chữ ký khó hiểu — kiểm sớm
    /// ngay khi dựng service để lỗi nói đúng chỗ thiếu, cùng lối <see cref="VnPayOptions.MissingPiece"/>.
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
