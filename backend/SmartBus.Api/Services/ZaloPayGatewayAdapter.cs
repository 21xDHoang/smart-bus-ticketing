using System.Globalization;
using System.Text.Json;
using SmartBus.Api.Dtos.Payments;

namespace SmartBus.Api.Services;

/// <summary>
/// Adapter ZaloPay — bọc <see cref="IZaloPayGatewayService"/> (task *"Tích hợp ZaloPay và thẻ ngân
/// hàng"* — Nguyễn Duy Kiên), dịch DTO trung tính sang hình dạng ZaloPay và ngược lại. Đi theo đúng
/// con đường <see cref="MoMoGatewayAdapter"/> đã mở: lớp MỎNG, không tự ký, không tự gọi HTTP, không
/// đụng CSDL — mọi phép nặng nằm ở client cổng đã có bộ test riêng.
///
/// Ba điểm KHÁC MoMo, đều do cổng ZaloPay quy định chứ không phải ta chọn:
///
/// 1. <c>app_trans_id</c> KHÔNG phải paymentCode trần: cổng bắt buộc tiền tố <c>yymmdd</c> giờ Việt
///    Nam. Adapter ghép tiền tố lúc gửi (<see cref="ZaloPayGatewayService.BuildAppTransId"/>) và bỏ
///    tiền tố lúc nhận (<see cref="ZaloPayGatewayService.StripAppTransIdPrefix"/>) — nhờ vậy endpoint
///    vẫn chỉ thấy một mã paymentCode duy nhất, y như với MoMo/VNPay.
/// 2. Callback là vỏ ba trường <c>{data, mac, type}</c> với <c>data</c> là CHUỖI JSON, không phải một
///    object JSON như MoMo. Nhờ vậy phép kiểm chữ ký là trên nguyên văn chuỗi, độc lập với việc ta
///    đọc nổi ruột hay không.
/// 3. Vỏ callback KHÔNG mang mã kết quả — xem ghi chú ở <see cref="VerifyCallback"/>.
///
/// THUẦN CỘNG THÊM: client ZaloPay và bộ test của nó không đổi.
/// </summary>
public class ZaloPayGatewayAdapter : IPaymentGateway
{
    /// <summary>
    /// app_user gửi cổng — ZaloPay BẮT BUỘC có trường này (MoMo không có), dùng để cổng ghi log chống
    /// gian lận. Hiện là hằng vì DTO trung tính <see cref="PaymentInitiationRequest"/> không mang mã
    /// hành khách; muốn gửi mã hành khách thật thì phải thêm trường vào DTO trung tính — đó là đổi
    /// hình dạng API, phải sửa docs/api-contract.md trước (luật 5), nên để lại khi có nghiệp vụ cần.
    /// </summary>
    private const string AppUser = "smartbus";

    /// <summary>
    /// Kênh ghim cho mã phương thức <c>ZaloPay</c> — hợp đồng chốt mã này nghĩa là VÍ ZaloPay (bảng
    /// ánh xạ ở mục "Enum chốt" của docs/api-contract.md). Bỏ dòng gán này trong
    /// <see cref="InitiateAsync"/> là khách tự chọn kênh trên trang ZaloPay — đúng một dòng, không
    /// ảnh hưởng gì khác.
    /// </summary>
    private const string WalletChannel = "zalopay_wallet";

    /// <summary>
    /// Đọc vỏ callback theo cùng quy ước với client ZaloPay
    /// (<see cref="JsonSerializerDefaults.Web"/>: khớp tên không phân biệt hoa thường).
    /// </summary>
    private static readonly JsonSerializerOptions CallbackJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IZaloPayGatewayService _zaloPay;

    public ZaloPayGatewayAdapter(IZaloPayGatewayService zaloPay) => _zaloPay = zaloPay;

    public string ProviderCode => PaymentProviderCodes.ZaloPay;

    public async Task<PaymentInitiationResult> InitiateAsync(
        PaymentInitiationRequest request, CancellationToken cancellationToken = default)
    {
        // Một mốc thời gian dùng cho CẢ tiền tố yymmdd lẫn app_time: lấy giờ hai lần có thể vắt qua
        // nửa đêm giờ Việt Nam, và khi ấy mã đơn mang ngày cũ còn app_time mang ngày mới.
        var now = DateTimeOffset.UtcNow;

        var result = await _zaloPay.CreateOrderAsync(new ZaloPayCreateOrderRequest
        {
            AppTransId = ZaloPayGatewayService.BuildAppTransId(request.PaymentCode, now),
            AppUser = AppUser,
            // Số tiền đồng trần — ZaloPay KHÔNG nhân 100 (chuyện ×100 là quy ước riêng của VNPay).
            Amount = request.Amount,
            Description = request.OrderInfo,
            AppTime = now,
            PreferredPaymentMethod = WalletChannel,
            // request.IpnUrl: ZaloPay nhận callback_url theo từng đơn (khác VNPay đăng ký một lần
            // trên portal). Để trống thì cổng dùng URL đã đăng ký — client tự bỏ qua khi rỗng.
            CallbackUrl = request.IpnUrl,
        }, cancellationToken);

        return new PaymentInitiationResult
        {
            Success = result.Success,
            RedirectUrl = result.OrderUrl,
            QrCodeUrl = result.QrCode,
            // Cổng trả hai tầng thông báo: return_message thường là câu chung, sub_return_message mới
            // nói rõ vì sao (ví dụ -68 trùng mã đơn). Ưu tiên câu chi tiết khi cổng có gửi.
            Message = Detail(result),
        };
    }

    public PaymentCallbackResult VerifyCallback(PaymentCallbackInput input)
    {
        // Thân rỗng/thiếu (cổng gọi nhầm kiểu, bot dò endpoint) — dữ liệu người ngoài, không phải lỗi
        // của ta. Chặn sớm vì JsonSerializer.Deserialize ném ArgumentNullException với null.
        if (string.IsNullOrWhiteSpace(input.Body))
        {
            return new PaymentCallbackResult();
        }

        ZaloPayCallback? callback;
        try
        {
            callback = JsonSerializer.Deserialize<ZaloPayCallback>(input.Body, CallbackJsonOptions);
        }
        catch (JsonException)
        {
            // Không phải JSON của ZaloPay — trả "không hợp lệ" thay vì để 500.
            callback = null;
        }

        if (callback is null)
        {
            return new PaymentCallbackResult();
        }

        var isValid = _zaloPay.IsValidCallback(callback);

        // Chỉ đọc ruột SAU khi chữ ký hợp lệ: ruột là dữ liệu người ngoài gửi tới, đọc trước cũng
        // chẳng dùng được vào việc gì (mọi trường đều chỉ có nghĩa khi chữ ký đã đúng).
        var data = isValid ? _zaloPay.ParseCallbackData(callback.Data) : null;

        return new PaymentCallbackResult
        {
            IsValid = isValid,
            // 🔴 ZaloPay CHỈ gọi callback khi đã thu được tiền — tài liệu: "Khi và chỉ khi Zalopay nhận
            // tín hiệu khách hàng thành công thì mới thông báo kết quả." Vỏ callback không mang mã kết
            // quả nào (khác resultCode của MoMo, vnp_ResponseCode của VNPay), nên "chữ ký hợp lệ" +
            // "đọc được ruột" chính là "cổng báo đã thanh toán". KHÔNG có nhánh thất bại để so thêm —
            // đừng đi tìm trường trạng thái trong data, cổng không gửi.
            Succeeded = isValid && data is not null,
            // Bỏ tiền tố yymmdd_ để endpoint thấy đúng paymentCode nội bộ như với mọi cổng khác.
            PaymentCode = ZaloPayGatewayService.StripAppTransIdPrefix(data?.AppTransId ?? string.Empty),
            Amount = data?.Amount ?? 0,
            GatewayTransactionId = data is null
                ? string.Empty
                : data.ZpTransId.ToString(CultureInfo.InvariantCulture),
            // Cố ý để TRỐNG chứ không bịa: callback ZaloPay không có mã kết quả. Ai cần mã để tra tài
            // liệu thì dùng job đối soát gọi QueryOrderAsync (return_code 1/2/3).
            ProviderResponseCode = string.Empty,
            Message = string.Empty,
        };
    }

    /// <summary>Câu mô tả lỗi chi tiết nhất cổng có gửi — sub_return_message trước, return_message sau.</summary>
    private static string Detail(ZaloPayCreateOrderResult result)
        => string.IsNullOrWhiteSpace(result.SubReturnMessage) ? result.Message : result.SubReturnMessage;
}
