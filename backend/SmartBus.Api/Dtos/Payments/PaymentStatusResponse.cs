using SmartBus.Api.Entities;

namespace SmartBus.Api.Dtos.Payments;

/// <summary>
/// Kết quả của GET /api/payments/{paymentCode} — màn chờ poll mỗi 3 giây để biết tiền đã về chưa
/// (US 6, task *"API kiểm tra trạng thái giao dịch"* — Trần Trung Hiếu). Khớp đúng bản nháp
/// <c>PaymentStatusResult</c> mà Băng đã code ở <c>frontend/src/api/paymentApi.ts</c>.
///
/// Cửa sổ ĐỌC thuần: endpoint này không tự hỏi cổng — hỏi cổng là việc của job đối soát. Trạng
/// thái chỉ lật qua callback hoặc job; timeout của màn chờ là khái niệm PHÍA FRONTEND.
/// </summary>
public class PaymentStatusResponse
{
    /// <summary>Mã giao dịch nội bộ — khoá poll, và là khoá chống trùng của bảng Payments.</summary>
    public string PaymentCode { get; set; } = string.Empty;

    /// <summary>Số tiền phải trả (VND) — con số đã gửi lên cổng.</summary>
    public decimal Amount { get; set; }

    /// <summary>Mã phương thức thanh toán — <c>'MoMo' | 'VNPay' | 'ZaloPay' | 'BankCard'</c>.</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// Trạng thái hiện tại: "Pending" / "Success" / "Failed". Khai <c>string</c> chứ không phải enum —
    /// cùng lý do đã ghi ở <see cref="CreatePaymentResult.Status"/>.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Thời điểm cổng ghi nhận thu tiền (UTC) — null khi chưa Success.</summary>
    public DateTime? PaidAt { get; set; }

    /// <summary>Mã giao dịch phía cổng — null khi chưa Success.</summary>
    public string? GatewayTransactionId { get; set; }

    /// <summary>Lý do thất bại khi Status = Failed — hiển thị nguyên nhân cho khách.</summary>
    public string? Message { get; set; }
}
