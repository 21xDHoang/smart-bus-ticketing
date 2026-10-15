using SmartBus.Api.Dtos.Refunds;

namespace SmartBus.Api.Services;

/// <summary>
/// Gọi cổng thanh toán để hoàn tiền cho MỘT khoản hoàn (US 8). Đây là đường ranh giữa phần nghiệp
/// vụ hoàn tiền (đã dựng) và phần nối cổng thật.
///
/// Người gọi duy nhất hiện tại: <c>RefundAttemptService</c> (task *"Xử lý hoàn tiền thất bại +
/// retry theo lịch"* — Phùng Duy Hoàng), chạy trong job thử lại theo lịch (dòng 30/41) và lượt
/// gọi tay từ màn Admin. Service gọi <b>ngay sau</b> khi giành được dòng bằng <c>xmin</c> — thứ tự
/// đó là bắt buộc, xem docs/35 §4.2.
///
/// ⚠️ Bản cài đặt thật là task *"API gọi cổng hoàn tiền"* (dòng 29 — Hiếu), tạm thời chưa dựng;
/// bản đang đăng ký trong DI là <see cref="PlaceholderRefundGatewayCaller"/> (luôn trả
/// <c>Unresolved</c>, tuyệt đối không giả vờ đã hoàn tiền). Nối cổng thật = thay đúng một dòng
/// đăng ký trong Program.cs, không phải sửa service.
///
/// Hợp đồng dành cho bản cài đặt thật:
/// <list type="bullet">
/// <item>Đọc <see cref="RefundGatewayCallRequest.MethodCode"/> để chọn cổng — phương thức đã được
/// chốt là của giao dịch gốc, người cài đặt KHÔNG nhận thêm tham số chọn kênh.</item>
/// <item>Cổng không trả lời (timeout, đứt kết nối) → trả
/// <see cref="Entities.RefundLogResult.Unresolved"/>, <b>đừng</b> đoán là thất bại.</item>
/// <item>Ném ngoại lệ cũng được tha: người gọi bọc try/catch và coi mọi ngoại lệ là
/// <c>Unresolved</c> — một lượt gọi chưa rõ kết quả không bao giờ được phép đoán.</item>
/// </list>
/// </summary>
public interface IRefundGatewayCaller
{
    /// <summary>
    /// Thực hiện một lượt gọi cổng hoàn tiền. Không tự ghi CSDL — mọi thay đổi trạng thái là việc
    /// của <c>RefundAttemptService</c>, nơi đã giành dòng trước khi gọi hàm này.
    /// </summary>
    Task<RefundGatewayCallOutcome> CallAsync(
        RefundGatewayCallRequest request,
        CancellationToken cancellationToken = default);
}
