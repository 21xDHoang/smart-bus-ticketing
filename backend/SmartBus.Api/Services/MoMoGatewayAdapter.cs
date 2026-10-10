using System.Globalization;
using System.Text.Json;
using SmartBus.Api.Dtos.Payments;

namespace SmartBus.Api.Services;

/// <summary>
/// Adapter MoMo — dịch DTO trung tính sang hình dạng MoMo và ngược lại, bọc
/// <see cref="IMoMoGatewayService"/> (task *"Tích hợp SDK MoMo: tạo giao dịch, nhận callback"* —
/// Trần Trung Hiếu) để endpoint không phải biết tên trường MoMo. Task *"Adapter pattern thống nhất
/// cổng thanh toán (dễ thêm cổng mới)"* — Phùng Duy Hoàng.
///
/// THUẦN CỘNG THÊM: không sửa một dòng nào trong client MoMo lẫn bộ test của nó — adapter đứng
/// ngoài, chỉ dịch và chuẩn hoá kết luận.
/// </summary>
public class MoMoGatewayAdapter : IPaymentGateway
{
    /// <summary>
    /// Đọc JSON theo cùng quy ước với client MoMo (<see cref="JsonSerializerDefaults.Web"/>: tên
    /// trường camelCase như cổng gửi, khớp không phân biệt hoa thường).
    /// </summary>
    private static readonly JsonSerializerOptions CallbackJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IMoMoGatewayService _momo;

    public MoMoGatewayAdapter(IMoMoGatewayService momo) => _momo = momo;

    public string ProviderCode => PaymentProviderCodes.MoMo;

    public async Task<PaymentInitiationResult> InitiateAsync(
        PaymentInitiationRequest request, CancellationToken cancellationToken = default)
    {
        // RequestId/ExtraData để nguyên giá trị mặc định — client MoMo tự sinh GUID cho RequestId
        // và ExtraData rỗng là hợp lệ; chưa có nghiệp vụ nào cần dữ liệu riêng.
        var result = await _momo.CreatePaymentAsync(new MoMoCreatePaymentRequest
        {
            OrderId = request.PaymentCode,
            Amount = request.Amount,
            OrderInfo = request.OrderInfo,
            RedirectUrl = request.ReturnUrl,
            IpnUrl = request.IpnUrl,
        }, cancellationToken);

        return new PaymentInitiationResult
        {
            Success = result.Success,
            RedirectUrl = result.PayUrl,
            Deeplink = result.Deeplink,
            QrCodeUrl = result.QrCodeUrl,
            Message = result.Message,
        };
    }

    public PaymentCallbackResult VerifyCallback(PaymentCallbackInput input)
    {
        // Thân rỗng/thiếu (cổng gọi nhầm kiểu, bot dò endpoint) — dữ liệu người ngoài, không phải
        // lỗi của ta. Chặn sớm vì JsonSerializer.Deserialize ném ArgumentNullException với null.
        if (string.IsNullOrWhiteSpace(input.Body))
        {
            return new PaymentCallbackResult();
        }

        MoMoCallback? callback;
        try
        {
            callback = JsonSerializer.Deserialize<MoMoCallback>(input.Body, CallbackJsonOptions);
        }
        catch (JsonException)
        {
            // Không phải JSON của MoMo — trả "không hợp lệ" thay vì để 500 (cùng tinh thần với
            // chữ ký không phải hex ở VnPayGatewayService).
            callback = null;
        }

        if (callback is null)
        {
            return new PaymentCallbackResult();
        }

        var isValid = _momo.IsValidCallback(callback);

        return new PaymentCallbackResult
        {
            IsValid = isValid,
            // Không bao giờ báo thành công trên dữ liệu chưa qua kiểm chữ ký — thứ tự && quan trọng.
            Succeeded = isValid && callback.ResultCode == 0,
            // JSON người ngoài có thể gửi null dù DTO khai non-null — DTO trung tính cam kết không null.
            PaymentCode = callback.OrderId ?? string.Empty,
            Amount = callback.Amount,
            GatewayTransactionId = callback.TransId.ToString(CultureInfo.InvariantCulture),
            ProviderResponseCode = callback.ResultCode.ToString(CultureInfo.InvariantCulture),
            Message = callback.Message ?? string.Empty,
        };
    }
}
