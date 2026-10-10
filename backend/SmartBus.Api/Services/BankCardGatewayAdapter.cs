using SmartBus.Api.Dtos.Payments;

namespace SmartBus.Api.Services;

/// <summary>
/// Adapter cho mã phương thức <c>BankCard</c> ("thẻ ngân hàng") — task *"Tích hợp ZaloPay và thẻ ngân
/// hàng"* — Nguyễn Duy Kiên. Đứng trên client VNPay: dự án KHÔNG có cổng riêng nào cho thẻ ngân
/// hàng, còn VNPay có sẵn kênh thẻ, nên <c>BankCard</c> là VNPay với kênh thẻ GHIM SẴN.
///
/// Vì sao là adapter RIÊNG chứ không dùng chung <see cref="VnPayGatewayAdapter"/>: chỉ khác nhau đúng
/// một tham số <c>vnp_BankCode</c>, mà tham số ấy lại phải đặt lúc dựng URL — tức <c>InitiateAsync</c>
/// của adapter VNPay (không ghim kênh, để khách tự chọn) không dùng lại được. Hai mã phương thức của
/// hợp đồng nhờ vậy có hành vi KHÁC NHAU thật: <c>VNPay</c> cho khách chọn phương thức trên trang
/// cổng, <c>BankCard</c> vào thẳng kênh thẻ.
///
/// Còn <see cref="VerifyCallback"/> thì uỷ thác nguyên cho adapter VNPay đã đăng ký trong DI: callback
/// của kênh thẻ có ĐÚNG hình dạng callback VNPay (cùng cổng, cùng tham số vnp_, cùng chữ ký
/// SHA512). Chép lại phép chuẩn hoá ấy ở đây là hai bản sao sẽ lệch nhau ngay lần đầu VNPay đổi gì.
///
/// ⚠️ Hệ quả cho endpoint callback: kênh thẻ gọi về CHÍNH endpoint callback của VNPay
/// (<c>/payments/vnpay/callback</c>) — không có endpoint riêng cho <c>BankCard</c>. Phân biệt giao
/// dịch nào là thẻ là việc của cột method trong bảng Payments, không phải của đường dẫn callback.
///
/// THUẦN CỘNG THÊM: client VNPay, adapter VNPay và bộ test của chúng không đổi.
/// </summary>
public class BankCardGatewayAdapter : IPaymentGateway
{
    /// <summary>
    /// Mã kênh thẻ ATM nội địa / tài khoản ngân hàng của VNPay — nghĩa thông dụng của "thẻ ngân hàng"
    /// trong tiếng Việt, và là chốt đã ghi ở mục "Enum chốt" của docs/api-contract.md. VNPay còn mã
    /// <c>INTCARD</c> cho thẻ quốc tế: nếu nhóm muốn <c>BankCard</c> hiểu là CẢ HAI loại thẻ thì đổi
    /// đúng hằng này — không ảnh hưởng chỗ nào khác. Để trống mã kênh là khách tự chọn trên trang
    /// VNPay, và khi ấy <c>BankCard</c> trùng hẳn với <c>VNPay</c>, mất lý do tồn tại.
    /// </summary>
    private const string DomesticCardChannel = "VNBANK";

    private readonly IVnPayGatewayService _vnpay;
    private readonly VnPayGatewayAdapter _vnpayAdapter;

    public BankCardGatewayAdapter(IVnPayGatewayService vnpay, VnPayGatewayAdapter vnpayAdapter)
    {
        _vnpay = vnpay;
        _vnpayAdapter = vnpayAdapter;
    }

    public string ProviderCode => PaymentProviderCodes.BankCard;

    public Task<PaymentInitiationResult> InitiateAsync(
        PaymentInitiationRequest request, CancellationToken cancellationToken = default)
    {
        // Cùng lối adapter VNPay: URL ký sẵn tại chỗ nên không chạm mạng, Success luôn true; thành hay
        // hỏng thực sự chỉ biết qua callback. request.IpnUrl bỏ qua — VNPay đăng ký IPN một lần trên
        // portal, không nhận theo từng đơn.
        var url = _vnpay.CreatePaymentUrl(new VnPayCreatePaymentRequest
        {
            TxnRef = request.PaymentCode,
            Amount = request.Amount,
            OrderInfo = request.OrderInfo,
            ReturnUrl = request.ReturnUrl,
            IpAddress = request.IpAddress,
            BankCode = DomesticCardChannel,
        });

        return Task.FromResult(new PaymentInitiationResult { Success = true, RedirectUrl = url });
    }

    /// <summary>
    /// Uỷ thác nguyên cho adapter VNPay: cùng cổng, cùng hình dạng tham số, cùng chữ ký — nên mọi kết
    /// luận (chữ ký hợp lệ, đã thu tiền, mã giao dịch) đều giống hệt nhau. Việc đối chiếu
    /// PaymentCode/Amount với bảng Payments vẫn thuộc về endpoint, như tài liệu VNPay yêu cầu.
    /// </summary>
    public PaymentCallbackResult VerifyCallback(PaymentCallbackInput input)
        => _vnpayAdapter.VerifyCallback(input);
}
