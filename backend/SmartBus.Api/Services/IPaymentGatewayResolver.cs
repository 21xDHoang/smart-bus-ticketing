using System.Diagnostics.CodeAnalysis;

namespace SmartBus.Api.Services;

/// <summary>
/// Tra cổng thanh toán theo mã phương thức — điểm DUY NHẤT endpoint biết để lấy
/// <see cref="IPaymentGateway"/> (task *"Adapter pattern thống nhất cổng thanh toán (dễ thêm cổng
/// mới)"* — Phùng Duy Hoàng). Thêm cổng mới = viết adapter + đăng ký, không sửa endpoint lẫn
/// resolver.
/// </summary>
public interface IPaymentGatewayResolver
{
    /// <summary>Mã các cổng ĐÃ MỞ (có adapter) — dùng cho thông báo 400 "chưa mở".</summary>
    IReadOnlyCollection<string> SupportedProviders { get; }

    /// <summary>
    /// Tra cổng theo mã, khớp KHÔNG phân biệt hoa thường ('vnpay' hay 'VNPay' đều được — lòng vòng
    /// cho query/FE gõ lệch). Mã chưa có adapter thì trả false (endpoint trả 400 "chưa mở").
    /// Cổng CÓ trong danh sách nhưng thiếu cấu hình thì NÉM ngay khi dựng adapter, nêu đúng tên
    /// khoá thiếu (luật 2) — cố ý không trả false: thiếu cấu hình là lỗi triển khai, không phải
    /// "cổng chưa mở", hai chuyện phải nhìn ra khác nhau.
    /// </summary>
    bool TryGet(string providerCode, [NotNullWhen(true)] out IPaymentGateway? gateway);
}
