namespace SmartBus.Api.Services;

/// <summary>
/// Cấu hình cổng thanh toán ZaloPay — đọc từ section <c>ZaloPay</c> của cấu hình (US 6 "Cổng thanh
/// toán", task *"Tích hợp ZaloPay và thẻ ngân hàng"* — Nguyễn Duy Kiên).
///
/// 🔴 LUẬT 2 — repo PUBLIC: AppId/Key1/Key2 là bí mật đối tác, KHÔNG BAO GIỜ nằm trong code hay
/// appsettings mẫu. Máy dev đặt qua <c>dotnet user-secrets</c>, khi deploy đặt qua biến môi trường
/// <c>ZaloPay__Key1</c>… — đúng khuôn MoMoOptions/VnPayOptions hiện có.
///
/// Khác MoMo/VNPay một điểm: ZaloPay dùng HAI khoá cho HAI CHIỀU. <c>Key1</c> ký request ta gửi
/// đi, <c>Key2</c> xác thực callback cổng gửi về. Dùng lẫn hai khoá thì mọi chữ ký đều sai mà
/// thông báo của cổng không chỉ ra chỗ nhầm — nên hai trường tách bạch và kiểm riêng.
///
/// Đường dẫn cổng là thứ công khai nên có giá trị mặc định là sandbox của ZaloPay. Lên thật
/// (production) thì đổi qua cấu hình, không phải sửa code.
/// </summary>
public class ZaloPayOptions
{
    public const string SectionName = "ZaloPay";

    /// <summary>Đường dẫn gốc của cổng — mặc định sandbox, production đổi qua cấu hình.</summary>
    public const string DefaultEndpoint = "https://sb-openapi.zalopay.vn";

    /// <summary>
    /// Mã đối tác (app_id) do ZaloPay cấp — ví dụ "2553". Cấu hình luôn là CHUỖI (mọi nguồn cấu hình
    /// của .NET đều ra chuỗi) nhưng tài liệu ZaloPay khai app_id là <c>int32</c>: body gửi cổng phải
    /// là số, không phải chuỗi. <see cref="ZaloPayGatewayService"/> kiểm và đổi sang số ngay lúc dựng
    /// — giá trị không phải số nguyên (hoặc có số 0 thừa) bị chặn ở đó, vì khi ấy chuỗi ký và body
    /// gửi cổng sẽ mang hai giá trị khác nhau.
    /// </summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>Khoá bí mật ký request GỬI ĐI (HMAC-SHA256) — TUYỆT ĐỐI không lộ ra ngoài (luật 2).</summary>
    public string Key1 { get; set; } = string.Empty;

    /// <summary>Khoá bí mật xác thực callback cổng GỬI VỀ (HMAC-SHA256) — bí mật riêng, khác Key1.</summary>
    public string Key2 { get; set; } = string.Empty;

    /// <summary>Gốc API của cổng — mặc định sandbox <see cref="DefaultEndpoint"/>.</summary>
    public string Endpoint { get; set; } = DefaultEndpoint;

    /// <summary>
    /// Thiếu giá trị đối tác thì mọi cuộc gọi cổng đều chết với lỗi chữ ký khó hiểu — kiểm sớm ngay
    /// khi dựng service để lỗi nói đúng chỗ thiếu, cùng lối <see cref="MoMoOptions.MissingPiece"/>.
    /// Ba trường kiểm riêng: thiếu Key2 mà vẫn dựng được thì callback nào cũng bị coi là giả mạo.
    /// </summary>
    public string? MissingPiece()
    {
        if (string.IsNullOrWhiteSpace(AppId))
        {
            return "ZaloPay:AppId";
        }

        if (string.IsNullOrWhiteSpace(Key1))
        {
            return "ZaloPay:Key1";
        }

        if (string.IsNullOrWhiteSpace(Key2))
        {
            return "ZaloPay:Key2";
        }

        return null;
    }
}
