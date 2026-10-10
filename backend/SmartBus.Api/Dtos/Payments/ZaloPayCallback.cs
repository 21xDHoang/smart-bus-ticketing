namespace SmartBus.Api.Dtos.Payments;

/// <summary>
/// Vỏ ngoài của callback ZaloPay gửi về <c>callback_url</c> của ta khi giao dịch có kết quả — đầu
/// vào của <see cref="Services.IZaloPayGatewayService.IsValidCallback"/>.
///
/// Kỳ dị hơn callback MoMo/VNPay ở chỗ cổng gửi đúng BA trường, và trường <see cref="Data"/> là
/// một **CHUỖI JSON** (không phải object lồng): chữ ký <see cref="Mac"/> được tính trên chính chuỗi
/// đó, nên phải ký và kiểm trên nguyên văn chuỗi — phân tích ra object rồi ký lại là chữ ký sai
/// ngay (thứ tự khoá và khoảng trắng do cổng quyết định, không phải ta).
///
/// Nhờ vậy phép kiểm chữ ký không phụ thuộc việc ta có đọc nổi <see cref="Data"/> hay không: sai
/// chữ ký là chắc chắn không tin, kể cả khi phần trong là JSON ta chưa biết.
///
/// Cổng gửi bằng POST JSON; ta phải trả <c>{"return_code":1,"return_message":"Success"}</c> thì
/// cổng mới coi là đã nhận — khác MoMo (204 No Content) và VNPay (<c>RspCode</c>).
/// </summary>
public class ZaloPayCallback
{
    /// <summary>Chuỗi JSON chứa kết quả giao dịch — chuỗi chữ ký, giữ nguyên văn.</summary>
    public string Data { get; set; } = string.Empty;

    /// <summary>Chữ ký HMAC-SHA256 của cổng trên <see cref="Data"/>, tính bằng <c>Key2</c>.</summary>
    public string Mac { get; set; } = string.Empty;

    /// <summary>
    /// Loại callback — ZaloPay gửi SỐ <c>1</c> cho callback giao dịch (không phải chuỗi "1" như
    /// thoạt nhìn trong tài liệu). Khai <c>int</c> chứ KHÔNG khai <c>string</c>: JsonSerializer không
    /// đọc được số JSON vào thuộc tính chuỗi, khai sai là cả vỏ callback giải mã hỏng và MỌI callback
    /// thật đều bị từ chối. Khai <c>int</c> lại còn nhận được cả <c>"1"</c> dạng chuỗi, vì quy ước
    /// <see cref="System.Text.Json.JsonSerializerDefaults.Web"/> cho phép đọc số từ chuỗi.
    /// </summary>
    public int Type { get; set; }
}
