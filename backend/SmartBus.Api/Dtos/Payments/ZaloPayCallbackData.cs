using System.Text.Json.Serialization;

namespace SmartBus.Api.Dtos.Payments;

/// <summary>
/// Phần TRONG của callback ZaloPay — nội dung chuỗi JSON nằm ở
/// <see cref="ZaloPayCallback.Data"/>. Cổng không gửi thẳng object này mà gửi nó dưới dạng CHUỖI,
/// nên <see cref="Services.IZaloPayGatewayService.ParseCallbackData"/> mới là chỗ phân tích nó ra.
///
/// Đây là TẦNG GATEWAY: sau khi adapter chuẩn hoá, endpoint chỉ đọc
/// <see cref="PaymentCallbackResult"/> trung tính, không đụng tên trường ZaloPay.
///
/// ⚠️ Tập trường ở đây lấy theo tài liệu ZaloPay mô tả callback giao dịch. Cổng có thể gửi thêm
/// trường (channel, discount_amount, user_fee_amount…); ta chỉ khai những trường thật sự dùng, vì
/// <c>JsonSerializer</c> bỏ qua trường lạ chứ không ném.
///
/// 🔴 Mọi trường phải ghim tên bằng <see cref="System.Text.Json.Serialization.JsonPropertyNameAttribute"/>:
/// cổng gửi snake_case, còn quy ước <see cref="System.Text.Json.JsonSerializerDefaults.Web"/> chỉ
/// đổi tên thuộc tính C# sang camelCase (<c>AppTransId</c> → <c>appTransId</c>) — KHÔNG tự nối được
/// với <c>app_trans_id</c>, dấu gạch dưới là chỗ chết. Khớp không phân biệt hoa thường cũng không
/// cứu được. Thiếu ghim thì chuỗi vẫn giải mã thành công nhưng MỌI trường về mặc định — callback
/// đọc ra rỗng mà không có lỗi nào để lần theo.
/// </summary>
public class ZaloPayCallbackData
{
    /// <summary>
    /// Mã đối tác (app_id) — đối chiếu thêm với cấu hình của ta. Tài liệu ZaloPay khai trường này là
    /// SỐ (int), không phải chuỗi; khai <c>string</c> ở đây là JsonSerializer ném khi gặp
    /// <c>"app_id":2553</c> và cả ruột callback hỏng theo — chính vì vậy phải khai <c>int?</c>.
    /// </summary>
    [JsonPropertyName("app_id")]
    public int? AppId { get; set; }

    /// <summary>
    /// Mã đơn hàng ta gửi đi (app_trans_id) — khoá để tìm bản ghi Payments. Đây là mã ĐÃ mang tiền
    /// tố <c>yymmdd</c>; muốn ra paymentCode nội bộ thì bỏ tiền tố
    /// (<see cref="Services.ZaloPayGatewayService.StripAppTransIdPrefix"/>).
    /// </summary>
    [JsonPropertyName("app_trans_id")]
    public string? AppTransId { get; set; }

    /// <summary>Mã người dùng ta gửi lúc tạo đơn (app_user).</summary>
    [JsonPropertyName("app_user")]
    public string? AppUser { get; set; }

    /// <summary>Số tiền khách đã trả (VND) — phải khớp Amount của bản ghi giao dịch.</summary>
    [JsonPropertyName("amount")]
    public long Amount { get; set; }

    /// <summary>Mã giao dịch do ZaloPay sinh — lưu làm gatewayTransactionId để đối soát.</summary>
    [JsonPropertyName("zp_trans_id")]
    public long ZpTransId { get; set; }

    /// <summary>Thời điểm cổng ghi nhận kết quả (epoch mili giây).</summary>
    [JsonPropertyName("server_time")]
    public long ServerTime { get; set; }
}
