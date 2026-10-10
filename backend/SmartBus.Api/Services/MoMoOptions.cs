namespace SmartBus.Api.Services;

/// <summary>
/// Cấu hình cổng thanh toán MoMo — đọc từ section <c>MoMo</c> của cấu hình (US 6 "Cổng thanh toán").
///
/// 🔴 LUẬT 2 — repo PUBLIC: PartnerCode/AccessKey/SecretKey là bí mật đối tác, KHÔNG BAO GIỜ nằm
/// trong code hay appsettings mẫu. Máy dev đặt qua <c>dotnet user-secrets</c>, khi deploy đặt qua
/// biến môi trường <c>MoMo__SecretKey</c>… — đúng khuôn JWT hiện có.
///
/// Đường dẫn cổng là thứ công khai nên có giá trị mặc định là sandbox của MoMo. Lên thật
/// (production) thì đổi qua cấu hình, không phải sửa code.
/// </summary>
public class MoMoOptions
{
    public const string SectionName = "MoMo";

    /// <summary>Đường dẫn gốc của cổng — mặc định sandbox, production đổi qua cấu hình.</summary>
    public const string DefaultEndpoint = "https://test-payment.momo.vn";

    /// <summary>Mã đối tác do MoMo cấp — ví dụ "MOMO0T1D20220107".</summary>
    public string PartnerCode { get; set; } = string.Empty;

    /// <summary>Khoá công khai của đối tác — gửi kèm trong chuỗi chữ ký, không phải bí mật.</summary>
    public string AccessKey { get; set; } = string.Empty;

    /// <summary>Khoá bí mật dùng ký HMAC-SHA256 — TUYỆT ĐỐI không lộ ra ngoài (luật 2).</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Gốc API của cổng — mặc định sandbox <see cref="DefaultEndpoint"/>.</summary>
    public string Endpoint { get; set; } = DefaultEndpoint;

    /// <summary>
    /// Thiếu ba giá trị đối tác thì mọi cuộc gọi cổng đều chết với lỗi chữ ký khó hiểu — kiểm sớm
    /// ngay khi dựng service để lỗi nói đúng chỗ thiếu, cùng lối ChuoiKetNoiCsdl.KiemTra.
    /// </summary>
    public string? MissingPiece()
    {
        if (string.IsNullOrWhiteSpace(PartnerCode))
        {
            return "MoMo:PartnerCode";
        }

        if (string.IsNullOrWhiteSpace(AccessKey))
        {
            return "MoMo:AccessKey";
        }

        if (string.IsNullOrWhiteSpace(SecretKey))
        {
            return "MoMo:SecretKey";
        }

        return null;
    }
}
