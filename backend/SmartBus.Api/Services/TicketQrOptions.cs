namespace SmartBus.Api.Services;

/// <summary>
/// Cấu hình mã QR vé điện tử — đọc từ section <c>TicketQr</c> của cấu hình (US 4, Sprint 3, task
/// *"Service sinh mã QR duy nhất + ký số chống làm giả"*).
///
/// 🔴 LUẬT 2 — repo PUBLIC: <c>SigningKey</c> là khoá bí mật ký vé, KHÔNG BAO GIỜ nằm trong code
/// hay appsettings mẫu. Máy dev đặt qua <c>dotnet user-secrets</c>, khi deploy đặt qua biến môi
/// trường <c>TicketQr__SigningKey</c> — đúng khuôn VnPayOptions/ZaloPayOptions hiện có.
///
/// ⚠️ Khoá này khác khoá cổng thanh toán: nó ký CHỨNG TỪ đã phát hành. Đổi khoá là mọi vé đã ký
/// bằng khoá cũ mất hiệu lực soát (chữ ký không còn khớp) — muốn đổi phải tính chuyện ký lại vé
/// cũ hoặc chấp nhận vé cũ hỏng, nên đổi khoá là việc báo nhóm trước, không tự làm trên môi
/// trường đang chạy.
/// </summary>
public class TicketQrOptions
{
    public const string SectionName = "TicketQr";

    /// <summary>
    /// Khoá bí mật đối xứng (HMAC-SHA256) dùng ký mã QR vé — TUYỆT ĐỐI không lộ ra ngoài (luật 2).
    /// Nên là chuỗi càng dài càng tốt (khuyến nghị từ 32 ký tự trở lên).
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// Thiếu khoá thì mã sinh ra vẫn "chạy" nhưng không ai kiểm được chữ ký (hoặc tệ hơn: ký bằng
    /// khoá rỗng ai cũng đoán ra) — kiểm sớm ngay khi dựng service để lỗi nói đúng chỗ thiếu, cùng
    /// lối <see cref="VnPayOptions.MissingPiece"/>.
    /// </summary>
    public string? MissingPiece()
    {
        if (string.IsNullOrWhiteSpace(SigningKey))
        {
            return "TicketQr:SigningKey";
        }

        return null;
    }
}
