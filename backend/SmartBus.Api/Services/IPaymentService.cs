using SmartBus.Api.Dtos.Payments;

namespace SmartBus.Api.Services;

/// <summary>
/// Nghiệp vụ tạo giao dịch thanh toán và tra trạng thái (US 6 "Cổng thanh toán", task *"Tạo giao
/// dịch + gọi cổng"* — Trần Trung Hiếu). Hợp đồng đầy đủ ở mục "Thanh toán — /payments" của
/// docs/api-contract.md.
///
/// Ranh giới lỗi:
/// <list type="bullet">
/// <item><see cref="ServiceErrorKind.Invalid"/> → 400: mã phương thức chưa mở, dữ liệu gửi lên sai;</item>
/// <item><see cref="ServiceErrorKind.NotFound"/> → 404: chuyến không tồn tại;</item>
/// <item><see cref="ServiceErrorKind.Conflict"/> → 409: voucher không dùng được, hoặc số tiền tụt
/// dưới sàn của cổng;</item>
/// <item>ngoại lệ (<c>InvalidOperationException</c> thiếu cấu hình cổng, <c>HttpRequestException</c>
/// mạng chết, cổng từ chối) → controller dịch thành 502 — lỗi phía cổng / triển khai, không phải
/// lỗi dữ liệu khách gửi lên.</item>
/// </list>
/// </summary>
public interface IPaymentService
{
    /// <summary>
    /// Lưu giao dịch Pending rồi gọi cổng lấy URL thanh toán. <paramref name="ipnUrl"/> là địa chỉ
    /// cổng gọi về khi có kết quả (controller dựng từ scheme + host của chính request);
    /// <paramref name="ipAddress"/> là IP của khách gọi lên (chỉ VNPay dùng, <c>vnp_IpAddr</c> bắt
    /// buộc — controller lấy từ HttpContext).
    /// </summary>
    Task<ServiceResult<CreatePaymentResult>> CreateAsync(
        Guid userId,
        CreatePaymentRequest request,
        string ipnUrl,
        string ipAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tra trạng thái giao dịch của chính người gọi — cửa sổ đọc thuần, không hỏi cổng. Giao dịch
    /// không tồn tại hoặc của người khác → NotFound (cùng một câu, không phân biệt).
    /// </summary>
    Task<ServiceResult<PaymentStatusResponse>> GetStatusAsync(
        Guid userId,
        string paymentCode,
        CancellationToken cancellationToken = default);
}
