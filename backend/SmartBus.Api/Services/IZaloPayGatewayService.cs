using SmartBus.Api.Dtos.Payments;

namespace SmartBus.Api.Services;

/// <summary>
/// Client cổng thanh toán ZaloPay (US 6 "Cổng thanh toán") — task *"Tích hợp ZaloPay và thẻ ngân
/// hàng"* — Nguyễn Duy Kiên.
///
/// Ranh giới: class này CHỈ nói chuyện với cổng ZaloPay (tạo đơn, kiểm chữ ký callback, hỏi trạng
/// thái). Nó KHÔNG biết bảng Payments — bảng đó là migration của Vàng Thị Dăm; service nghiệp vụ
/// thanh toán sẽ gọi vào đây khi bảng có. Nhờ tách vậy, phần này test được bằng HTTP giả và không
/// phải chờ migration mới viết (cùng lối <see cref="MoMoGatewayService"/>).
///
/// Chữ ký ZaloPay dùng HAI khoá cho hai chiều: <c>Key1</c> ký request ta gửi đi, <c>Key2</c> xác
/// thực callback cổng gửi về. Chi tiết chuỗi ký từng lời gọi ở <see cref="ZaloPayGatewayService"/>.
/// </summary>
public interface IZaloPayGatewayService
{
    /// <summary>
    /// Tạo đơn thanh toán trên cổng ZaloPay. Thành công ở bước này chỉ là cổng CHẤP NHẬN đơn — trả
    /// <c>order_url</c> để chuyển khách sang trả tiền; kết quả cuối cùng đến qua callback
    /// <see cref="IsValidCallback"/>. Lỗi vận chuyển (HTTP chết) NÉM LÊN; cổng từ chối thì trả về
    /// trong result kèm <c>Message</c>.
    /// </summary>
    Task<ZaloPayCreateOrderResult> CreateOrderAsync(
        ZaloPayCreateOrderRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Kiểm chữ ký của một callback cổng gửi về: HMAC-SHA256 bằng <c>Key2</c> trên CHÍNH CHUỖI
    /// <see cref="ZaloPayCallback.Data"/> thô. Trả <c>true</c> chỉ khi khớp bằng phép so sánh hằng
    /// thời gian — tin nội dung callback chỉ sau bước này, và người gọi còn phải đối chiếu
    /// app_id/amount với bản ghi của mình.
    /// </summary>
    bool IsValidCallback(ZaloPayCallback callback);

    /// <summary>
    /// Phân tích chuỗi <see cref="ZaloPayCallback.Data"/> ra các trường dùng được. Trả <c>null</c>
    /// khi chuỗi không phải JSON hợp lệ — dữ liệu người ngoài gửi tới, không phải lỗi của ta (cùng
    /// tinh thần "chữ ký không phải hex thì trả false" ở các client khác).
    ///
    /// Tách khỏi <see cref="IsValidCallback"/> có chủ đích: phép kiểm chữ ký chỉ cần nguyên văn
    /// chuỗi, còn việc đọc ruột là chuyện riêng — sai chữ ký thì không cần đọc ruột làm gì.
    /// </summary>
    ZaloPayCallbackData? ParseCallbackData(string data);

    /// <summary>
    /// Hỏi cổng trạng thái thật của một đơn theo app_trans_id — nguồn sự thật cho đối soát khi
    /// callback lọt mất. Cùng vai với <see cref="IMoMoGatewayService.QueryTransactionAsync"/>.
    /// </summary>
    Task<ZaloPayQueryResult> QueryOrderAsync(
        string appTransId,
        CancellationToken cancellationToken = default);
}
