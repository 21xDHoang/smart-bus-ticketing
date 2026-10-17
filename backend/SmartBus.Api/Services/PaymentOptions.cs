namespace SmartBus.Api.Services;

/// <summary>
/// Cấu hình luồng thanh toán — đọc từ section <c>Payments</c> của cấu hình (US 6, task *"Tạo giao
/// dịch + gọi cổng"* — Trần Trung Hiếu).
///
/// Chỉ chứa đường dẫn CÔNG KHAI của frontend, không có bí mật nào (luật 2): <c>ReturnUrl</c> gửi
/// cổng phải trỏ về màn chờ của FE — một origin KHÁC backend, không suy ra được từ request. Còn
/// IPN URL (cổng gọi vào backend) suy từ chính request tới endpoint nên không cần cấu hình.
///
/// Deploy thật thì đổi qua <c>Payments__FrontendBaseUrl</c> — đúng khuôn Cors:Origins.
/// </summary>
public class PaymentOptions
{
    public const string SectionName = "Payments";

    /// <summary>Gốc URL của frontend — mặc định khớp máy dev (frontend chạy port 5173).</summary>
    public const string DefaultFrontendBaseUrl = "http://localhost:5173";

    /// <summary>Đường dẫn màn chờ kết quả thanh toán của FE — nhận paymentCode qua query string.</summary>
    public const string WaitingPagePath = "/payment-waiting";

    /// <summary>Gốc URL của frontend, KHÔNG có dấu gạch chéo ở cuối — nối thẳng vào đường dẫn màn chờ.</summary>
    public string FrontendBaseUrl { get; set; } = DefaultFrontendBaseUrl;
}
