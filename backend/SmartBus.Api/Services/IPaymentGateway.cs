using SmartBus.Api.Dtos.Payments;

namespace SmartBus.Api.Services;

/// <summary>
/// Cổng thanh toán nhìn từ phía nghiệp vụ — task *"Adapter pattern thống nhất cổng thanh toán
/// (dễ thêm cổng mới)"* — Phùng Duy Hoàng.
///
/// Mỗi cổng (MoMo, VNPay, sau này ZaloPay…) có một adapter cài interface này, dịch giữa DTO trung
/// tính (<see cref="PaymentInitiationRequest"/>, <see cref="PaymentCallbackInput"/>…) và hình dạng
/// riêng của cổng. Endpoint POST /payments và hai endpoint callback chỉ nói chuyện với interface
/// này qua <see cref="IPaymentGatewayResolver"/> — thêm cổng mới KHÔNG phải sửa endpoint, chỉ viết
/// thêm một adapter + đăng ký.
///
/// Adapter là lớp MỎNG có chủ đích: không tự ký, không tự gọi HTTP, không đụng CSDL — mọi phép
/// nặng vẫn nằm ở client cổng (<see cref="MoMoGatewayService"/>, <see cref="VnPayGatewayService"/>)
/// đã có bộ test riêng. Ở đây chỉ có sự dịch hình dạng + chuẩn hoá KẾT LUẬN (thành công/thất bại,
/// số tiền, mã giao dịch) để endpoint không phải biết tên trường của cổng nào.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Mã cổng — khớp giá trị methodCode của hợp đồng API (xem PaymentProviderCodes).</summary>
    string ProviderCode { get; }

    /// <summary>
    /// Khởi tạo giao dịch phía cổng và trả đường dẫn chuyển khách sang trả tiền. MoMo: gọi cổng
    /// (có thể bị từ chối — xem <see cref="PaymentInitiationResult.Success"/>). VNPay: dựng URL ký
    /// sẵn tại chỗ, không gọi mạng, luôn thành công.
    /// </summary>
    Task<PaymentInitiationResult> InitiateAsync(
        PaymentInitiationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Kiểm chữ ký dữ liệu cổng gọi về và chuẩn hoá thành một kết luận chung. KHÔNG ném với dữ
    /// liệu ngoài hỏng (JSON rác, thiếu tham số, chữ ký không phải hex) — trả
    /// <see cref="PaymentCallbackResult.IsValid"/> = false. Người gọi còn phải đối chiếu
    /// PaymentCode/Amount với bản ghi Payments trước khi lật trạng thái giao dịch.
    /// </summary>
    PaymentCallbackResult VerifyCallback(PaymentCallbackInput input);
}
