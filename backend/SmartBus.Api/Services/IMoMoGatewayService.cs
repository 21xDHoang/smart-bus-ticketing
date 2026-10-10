using SmartBus.Api.Dtos.Payments;

namespace SmartBus.Api.Services;

/// <summary>
/// Client cổng thanh toán MoMo (US 6 "Cổng thanh toán") — task *"Tích hợp SDK MoMo: tạo giao
/// dịch, nhận callback"* — Trần Trung Hiếu.
///
/// Ranh giới: class này CHỈ nói chuyện với cổng MoMo (tạo giao dịch, kiểm chữ ký IPN, hỏi trạng
/// thái). Nó KHÔNG biết bảng Payments — bảng đó là migration của Vàng Thị Dăm; service nghiệp vụ
/// thanh toán sẽ gọi vào đây khi bảng có. Nhờ tách vậy, phần này test được bằng HTTP giả và
/// không phải chờ migration mới viết.
///
/// Mọi chữ ký theo chuẩn MoMo: HMAC-SHA256 trên chuỗi <c>key=value</c> xếp theo bảng chữ cái,
/// khoá là SecretKey, kết quả hex chữ thường. Chi tiết từng trường trong
/// <see cref="MoMoGatewayService"/>.
/// </summary>
public interface IMoMoGatewayService
{
    /// <summary>
    /// Tạo giao dịch thanh toán trên cổng MoMo (<c>requestType = captureWallet</c>).
    /// Thành công ở bước này chỉ là cổng CHẤP NHẬN giao dịch — trả <c>payUrl</c> để chuyển khách
    /// sang thanh toán; kết quả cuối cùng đến qua callback <see cref="IsValidCallback"/>.
    /// </summary>
    Task<MoMoCreatePaymentResult> CreatePaymentAsync(
        MoMoCreatePaymentRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Kiểm tra chữ ký của một IPN do MoMo gửi về. Trả <c>true</c> chỉ khi chữ ký khớp bằng phép
    /// so sánh hằng thời gian — tin nội dung callback chỉ sau bước này, và người gọi còn phải đối
    /// chiếu PartnerCode/OrderId/Amount với bản ghi của mình (tài liệu MoMo yêu cầu hai kiểm tra).
    /// </summary>
    bool IsValidCallback(MoMoCallback callback);

    /// <summary>
    /// Hỏi cổng trạng thái thật của một giao dịch theo mã đơn hàng — nguồn sự thật cho đối soát
    /// khi IPN lọt mất (mạng nghẽn, app restart giữa chừng). Task *"API kiểm tra trạng thái giao
    /// dịch + đối soát tự động"*.
    /// </summary>
    Task<MoMoQueryTransactionResult> QueryTransactionAsync(
        string orderId,
        CancellationToken cancellationToken = default);
}
