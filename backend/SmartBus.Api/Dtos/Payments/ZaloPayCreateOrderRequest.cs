namespace SmartBus.Api.Dtos.Payments;

/// <summary>
/// Đầu vào cho <see cref="Services.IZaloPayGatewayService.CreateOrderAsync"/> — một đơn thanh toán
/// ZaloPay sắp gửi lên cổng (US 6 "Cổng thanh toán", task *"Tích hợp ZaloPay và thẻ ngân hàng"* —
/// Nguyễn Duy Kiên).
///
/// Đây là DTO của TẦNG GATEWAY, chưa phải DTO của endpoint POST /payments: endpoint đó cần bảng
/// Payments (migration của Vàng Thị Dăm) mới lưu được giao dịch. Khi bảng có, service nghiệp vụ
/// thanh toán sẽ dịch từ request của endpoint sang DTO này — hình dạng chốt ở mục "Thanh toán —
/// /payments" của docs/api-contract.md.
/// </summary>
public class ZaloPayCreateOrderRequest
{
    /// <summary>
    /// Mã đơn hàng gửi cổng (app_trans_id). KHÁC MoMo/VNPay: ZaloPay bắt buộc mã này **bắt đầu bằng
    /// <c>yymmdd</c> theo giờ Việt Nam (GMT+7)** và dài tối đa 40 ký tự; gửi trùng mã cổng trả
    /// <c>-68 DUPLICATE_APPS_TRANS_ID</c>. Dựng mã theo <see cref="Services.ZaloPayGatewayService.BuildAppTransId"/>.
    /// </summary>
    public string AppTransId { get; set; } = string.Empty;

    /// <summary>Mã người dùng phía ta (app_user) — ZaloPay ghi log chống gian lận, không cần là GUID.</summary>
    public string AppUser { get; set; } = string.Empty;

    /// <summary>Số tiền thanh toán (VND) — số nguyên, KHÔNG nhân 100 (khác VNPay).</summary>
    public long Amount { get; set; }

    /// <summary>Mô tả đơn hàng hiển thị cho khách trên cổng.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Dữ liệu riêng đi kèm đơn, dạng **chuỗi JSON** (cổng nhận nguyên văn chuỗi này trong body, và
    /// chuỗi này cũng vào thẳng chuỗi ký). Để trống thì gửi "{}".
    /// </summary>
    public string EmbedData { get; set; } = "{}";

    /// <summary>
    /// Danh sách mặt hàng, dạng **chuỗi JSON** như <see cref="EmbedData"/>. Để trống thì gửi "[]".
    /// </summary>
    public string Item { get; set; } = "[]";

    /// <summary>
    /// Mã ngân hàng để vào thẳng kênh đó (bank_code). Để trống thì khách chọn trên cổng.
    /// ⚠️ ZaloPay KHÔNG có trường <c>method</c> — chọn kênh bằng trường này và/hoặc
    /// <see cref="PreferredPaymentMethod"/>.
    /// </summary>
    public string? BankCode { get; set; }

    /// <summary>
    /// Kênh muốn ghim, đặt trong <c>embed_data.preferred_payment_method</c> — ví dụ
    /// <c>domestic_card</c>, <c>international_card</c>, <c>zalopay_wallet</c>, <c>vietqr</c>. Để
    /// trống thì cổng dùng mặc định (ví ZaloPay). Chỉ có tác dụng khi chuỗi JSON của
    /// <see cref="EmbedData"/> đang rỗng/không có khoá này.
    /// </summary>
    public string? PreferredPaymentMethod { get; set; }

    /// <summary>
    /// URL cổng gọi về khi có kết quả. Để trống thì dùng URL đã đăng ký trên portal ZaloPay.
    /// </summary>
    public string? CallbackUrl { get; set; }

    /// <summary>
    /// Thời điểm tạo đơn (app_time, epoch mili giây) — để trống thì service lấy giờ hệ thống. Có mặt
    /// để người gọi ghim được mốc thời gian (test dùng để so với vector tính sẵn).
    /// </summary>
    public DateTimeOffset? AppTime { get; set; }
}
