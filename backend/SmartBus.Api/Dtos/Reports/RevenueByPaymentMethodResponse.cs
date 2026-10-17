namespace SmartBus.Api.Dtos.Reports;

/// <summary>
/// Phản hồi của GET /api/reports/revenue/by-payment-method — doanh thu theo phương thức thanh
/// toán (US 19 "Doanh thu", Sprint 4 — Nguyễn Duy Kiên).
///
/// Nhóm theo đúng giá trị cột <c>Payments.MethodCode</c> (<c>MoMo</c> / <c>VNPay</c> /
/// <c>ZaloPay</c> / <c>BankCard</c>). Chỉ phương thức có ít nhất một giao dịch <c>Success</c>
/// trong kỳ mới thành dòng — cùng quy tắc với báo cáo theo tuyến / theo xe. Thứ tự: doanh thu
/// giảm dần, bằng nhau thì mã phương thức tăng dần (Ordinal).
/// </summary>
public class RevenueByPaymentMethodResponse : RevenueBreakdownResponse
{
    /// <summary>Doanh thu từng phương thức trong kỳ, đã sắp xếp cố định.</summary>
    public required IReadOnlyList<RevenuePaymentMethodRowResponse> PaymentMethods { get; set; }
}

/// <summary>Một dòng doanh thu của một phương thức thanh toán.</summary>
public class RevenuePaymentMethodRowResponse
{
    /// <summary>
    /// Mã phương thức — đúng giá trị cột <c>Payments.MethodCode</c> (xem
    /// <c>Dtos/Payments/PaymentProviderCodes</c>).
    /// </summary>
    public string MethodCode { get; set; } = string.Empty;

    /// <summary>Doanh thu (VND) — chỉ giao dịch Success qua phương thức này.</summary>
    public decimal Revenue { get; set; }

    /// <summary>Số giao dịch Success qua phương thức này.</summary>
    public int Payments { get; set; }
}
