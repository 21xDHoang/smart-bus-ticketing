using System.Globalization;
using SmartBus.Api.Dtos.Payments;

namespace SmartBus.Api.Services;

/// <summary>
/// Adapter VNPay — bọc <see cref="IVnPayGatewayService"/> (task *"Tích hợp VNPay: tạo URL thanh
/// toán + verify chữ ký"* — Nguyễn Duy Kiên), dịch DTO trung tính sang tham số vnp_ và ngược lại.
/// Task *"Adapter pattern thống nhất cổng thanh toán (dễ thêm cổng mới)"* — Phùng Duy Hoàng.
///
/// Khác adapter MoMo đúng một điểm bản chất: VNPay không có API server-to-server để tạo giao dịch
/// — URL ký sẵn ngay tại chỗ nên <see cref="InitiateAsync"/> không chạm mạng và
/// <see cref="PaymentInitiationResult.Success"/> luôn true; thành hay hỏng thực sự chỉ biết qua
/// callback. THUẦN CỘNG THÊM: client VNPay và bộ test của nó không đổi.
/// </summary>
public class VnPayGatewayAdapter : IPaymentGateway
{
    private readonly IVnPayGatewayService _vnpay;

    public VnPayGatewayAdapter(IVnPayGatewayService vnpay) => _vnpay = vnpay;

    public string ProviderCode => PaymentProviderCodes.VnPay;

    public Task<PaymentInitiationResult> InitiateAsync(
        PaymentInitiationRequest request, CancellationToken cancellationToken = default)
    {
        // request.IpnUrl không dùng: VNPay đăng ký IPN một lần trên portal, không nhận qua URL.
        // request.Amount là đồng trần — client tự nhân 100 khi ký (quy ước riêng của VNPay).
        var url = _vnpay.CreatePaymentUrl(new VnPayCreatePaymentRequest
        {
            TxnRef = request.PaymentCode,
            Amount = request.Amount,
            OrderInfo = request.OrderInfo,
            ReturnUrl = request.ReturnUrl,
            IpAddress = request.IpAddress,
        });

        return Task.FromResult(new PaymentInitiationResult { Success = true, RedirectUrl = url });
    }

    public PaymentCallbackResult VerifyCallback(PaymentCallbackInput input)
    {
        var isValid = _vnpay.IsValidSignature(input.Query);

        var responseCode = Value(input.Query, "vnp_ResponseCode");
        var transactionStatus = Value(input.Query, "vnp_TransactionStatus");

        return new PaymentCallbackResult
        {
            IsValid = isValid,
            // Cả HAI mã đều phải "00" theo tài liệu VNPay: ResponseCode là kết quả thanh toán,
            // TransactionStatus là trạng thái ghi nhận tiền — một trong hai khác 00 (khách huỷ,
            // ngân hàng từ chối) là chưa chắc có tiền.
            Succeeded = isValid && responseCode == "00" && transactionStatus == "00",
            PaymentCode = Value(input.Query, "vnp_TxnRef"),
            Amount = ParseAmount(Value(input.Query, "vnp_Amount")),
            GatewayTransactionId = Value(input.Query, "vnp_TransactionNo"),
            ProviderResponseCode = responseCode,
            // vnp_Message không phải tham số bắt buộc của mọi bản cổng — thiếu thì để trống, muốn
            // biết vì sao hỏng thì tra ProviderResponseCode theo tài liệu VNPay.
            Message = Value(input.Query, "vnp_Message"),
        };
    }

    /// <summary>Đọc một tham số, thiếu thì trả chuỗi rỗng — DTO trung tính cam kết không null.</summary>
    private static string Value(IReadOnlyDictionary<string, string> parameters, string name)
        => parameters.TryGetValue(name, out var value) ? value : string.Empty;

    /// <summary>
    /// vnp_Amount trên callback là số tiền ×100 (đơn vị nhỏ nhất của VNPay) — chia lại ra đồng.
    /// decimal chứ không long: thà giữ 0,5 đồng lẻ LỘ ra để bước đối chiếu thấy lệch, còn hơn cắt
    /// lén thành số tròn rồi khớp nhầm. Không đọc được (hiếm — chữ ký đã hợp lệ) thì trả 0, tự
    /// khắc lệch với bản ghi và bị bước đối chiếu chặn.
    /// </summary>
    private static decimal ParseAmount(string raw)
        => long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var minorUnits)
            ? minorUnits / 100m
            : 0m;
}
