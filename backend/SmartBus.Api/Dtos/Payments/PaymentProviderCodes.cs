namespace SmartBus.Api.Dtos.Payments;

/// <summary>
/// Mã phương thức thanh toán — giá trị của trường <c>methodCode</c> (POST /payments) và cột method
/// của bảng Payments, đồng thời là KHOÁ tra adapter cổng trong
/// <see cref="Services.IPaymentGatewayResolver"/>.
///
/// Khai báo thành hằng (cùng lối <see cref="Entities.RoleCodes"/>): endpoint, adapter và test dùng
/// chung một nguồn — gõ sai mã là lỗi biên dịch, không phải lỗi âm thầm lúc chạy.
///
/// Hợp đồng API (docs/api-contract.md mục "Thanh toán") chốt đúng 4 mã:
/// 'MoMo' | 'VNPay' | 'ZaloPay' | 'BankCard'. Ở đây mới khai hai mã đã có adapter — thêm cổng mới
/// thì thêm hằng ở đây + một dòng trong bảng đăng ký của
/// <see cref="Services.PaymentGatewayResolver"/>. Viết hoa CamelCase đúng quy ước A3, KHÁC đường
/// dẫn endpoint (viết thường: /payments/vnpay/callback).
/// </summary>
public static class PaymentProviderCodes
{
    public const string MoMo = "MoMo";

    /// <summary>Giá trị hợp đồng là "VNPay" (N hoa giữa từ) — tên hằng theo PascalCase C#.</summary>
    public const string VnPay = "VNPay";
}
