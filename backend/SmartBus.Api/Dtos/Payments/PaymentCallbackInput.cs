namespace SmartBus.Api.Dtos.Payments;

/// <summary>
/// Dữ liệu THÔ của một lượt cổng gọi về (IPN hoặc Return URL) — đầu vào của
/// <see cref="Services.IPaymentGateway.VerifyCallback"/>. Giữ nguyên dữ liệu thô rồi để adapter tự
/// biết lấy gì: VNPay đọc <see cref="Query"/> (GET kèm tham số vnp_ trên query string), MoMo đọc
/// <see cref="Body"/> (POST JSON). Hình dạng callback là chuyện riêng của từng cổng — endpoint
/// không được biết, nếu không thì thêm cổng mới là phải sửa endpoint.
/// </summary>
public class PaymentCallbackInput
{
    /// <summary>
    /// Tham số query string đã tách sẵn (VNPay đọc ở đây). Endpoint lấy từ Request.Query —
    /// framework đã giải mã URL sẵn.
    /// </summary>
    public IReadOnlyDictionary<string, string> Query { get; set; } = new Dictionary<string, string>();

    /// <summary>Thân request dạng chuỗi (MoMo đọc ở đây — JSON). Cổng gọi bằng GET thì để null.</summary>
    public string? Body { get; set; }
}
