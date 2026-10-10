namespace SmartBus.Api.Entities;

/// <summary>
/// Trạng thái một giao dịch thanh toán trong bảng <c>Payments</c> (US 6 — A9 #17). Lưu dạng chuỗi
/// trong CSDL (quy ước A3). Giá trị chốt ở docs/api-contract.md mục "Thanh toán" sau khi gộp hai
/// bản nháp FE: <c>'Pending' | 'Success' | 'Failed'</c>.
///
/// ⚠️ KHÔNG có giá trị "Paid" — đó là trạng thái của VÉ, không phải của giao dịch. Cột này chỉ
/// được lật bởi callback cổng (IPN) hoặc job đối soát, và mỗi <c>PaymentCode</c> chỉ lật đúng một
/// lần (idempotency — task dòng 33).
/// </summary>
public enum PaymentStatus
{
    /// <summary>
    /// Vừa tạo, chờ cổng xác nhận. Job đối soát quét các dòng còn ở trạng thái này quá 5 phút
    /// để hỏi lại cổng.
    /// </summary>
    Pending,

    /// <summary>Cổng xác nhận đã thu tiền (MoMo resultCode = 0 · VNPay ResponseCode "00").</summary>
    Success,

    /// <summary>Cổng báo thất bại hoặc khách huỷ — lý do nằm ở <see cref="Payment.Message"/>.</summary>
    Failed
}
