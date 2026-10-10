using SmartBus.Api.Dtos.Payments;

namespace SmartBus.Api.Services;

/// <summary>
/// Client cổng thanh toán VNPay (US 6 "Cổng thanh toán") — task *"Tích hợp VNPay: tạo URL thanh
/// toán + verify chữ ký"* — Nguyễn Duy Kiên.
///
/// Ranh giới: class này CHỈ dựng URL thanh toán và kiểm chữ ký tham số cổng trả về. Nó KHÔNG biết
/// bảng Payments — bảng đó là migration của Vàng Thị Dăm; service nghiệp vụ thanh toán sẽ gọi vào
/// đây khi bảng có. Khác MoMo ở bản chất: VNPay không có API server-to-server để tạo giao dịch —
/// ta KÝ SẴN một URL rồi chuyển khách sang cổng, nên client này không gọi mạng, không cần
/// <c>HttpClient</c>, và test được ngay không chờ migration.
///
/// Mọi chữ ký theo chuẩn VNPay: HMAC-SHA512 trên chuỗi <c>key=value</c> xếp theo bảng chữ cái,
/// giá trị mã hoá URL kiểu PHP (dấu cách thành <c>+</c>), khoá là HashSecret, kết quả hex chữ HOA.
/// Chi tiết từng trường trong <see cref="VnPayGatewayService"/>.
/// </summary>
public interface IVnPayGatewayService
{
    /// <summary>
    /// Dựng URL thanh toán đã ký để FE chuyển khách sang cổng. Cổng CHẤP NHẬN hay không chỉ biết
    /// được khi khách quay về (hoặc IPN gọi tới) — kết quả cuối cùng đến qua
    /// <see cref="IsValidSignature"/>.
    /// </summary>
    string CreatePaymentUrl(VnPayCreatePaymentRequest request);

    /// <summary>
    /// Kiểm tra chữ ký (<c>vnp_SecureHash</c>) của tham số cổng trả về — dùng chung cho cả Return
    /// URL (khách quay về) và IPN (cổng gọi thẳng vào backend). Trả <c>true</c> chỉ khi chữ ký khớp
    /// bằng phép so sánh hằng thời gian — tin nội dung tham số chỉ sau bước này, và người gọi còn
    /// phải đối chiếu TxnRef/Amount với bản ghi của mình như tài liệu VNPay yêu cầu.
    ///
    /// <paramref name="parameters"/> là TOÀN BỘ tham số nhận được (từ query string); service tự lọc
    /// các tham số <c>vnp_</c>, bỏ <c>vnp_SecureHash</c>/<c>vnp_SecureHashType</c> rồi tính lại.
    /// </summary>
    bool IsValidSignature(IReadOnlyDictionary<string, string> parameters);
}
