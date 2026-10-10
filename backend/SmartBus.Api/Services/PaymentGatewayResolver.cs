using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using SmartBus.Api.Dtos.Payments;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IPaymentGatewayResolver"/> — bảng đăng ký mã cổng → adapter (task *"Adapter
/// pattern thống nhất cổng thanh toán (dễ thêm cổng mới)"* — Phùng Duy Hoàng).
///
/// Vì sao tra qua <see cref="IServiceProvider"/> theo mã thay vì nhận
/// <c>IEnumerable&lt;IPaymentGateway&gt;</c>: nhận IEnumerable thì DI dựng SẴN MỌI adapter ngay khi
/// resolver được tạo — máy chỉ cấu hình một cổng sẽ vỡ cả dây chuyền ngay từ lượt resolve đầu
/// (adapter cổng kia dựng client của nó, client ném vì thiếu khoá bí mật). Tra theo mã thì chỉ
/// cổng được hỏi mới dựng — cấu hình cổng này không kéo theo cổng kia (xem test "máy chỉ cấu hình
/// MoMo" — ghim đúng hành vi này).
/// </summary>
public class PaymentGatewayResolver : IPaymentGatewayResolver
{
    /// <summary>
    /// Bảng đăng ký — NGUỒN DUY NHẤT cho cả ba việc: tra cứu, danh sách "đã mở", và phép khớp hoa
    /// thường (OrdinalIgnoreCase), nên không có chuyện danh sách hứa một đằng tra cứu làm một nẻo.
    /// Thêm cổng mới: viết adapter rồi thêm đúng một dòng vào đây + một dòng AddScoped adapter
    /// trong Program.cs.
    /// </summary>
    private static readonly Dictionary<string, Type> Registered =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [PaymentProviderCodes.MoMo] = typeof(MoMoGatewayAdapter),
            [PaymentProviderCodes.VnPay] = typeof(VnPayGatewayAdapter),
            [PaymentProviderCodes.ZaloPay] = typeof(ZaloPayGatewayAdapter),
            // BankCard KHÔNG phải một cổng riêng: nó là kênh thẻ của VNPay (xem BankCardGatewayAdapter).
            [PaymentProviderCodes.BankCard] = typeof(BankCardGatewayAdapter),
        };

    private readonly IServiceProvider _services;

    public PaymentGatewayResolver(IServiceProvider services) => _services = services;

    public IReadOnlyCollection<string> SupportedProviders => Registered.Keys;

    public bool TryGet(string providerCode, [NotNullWhen(true)] out IPaymentGateway? gateway)
    {
        gateway = null;

        if (providerCode is null || !Registered.TryGetValue(providerCode, out var adapterType))
        {
            return false;
        }

        // Dựng MUỘN có chủ đích: chỉ cổng được hỏi mới chạm tới client của nó (và cấu hình của
        // nó). Cổng có trong bảng mà thiếu cấu hình thì lỗi ném từ đây, nêu đúng tên khoá (luật 2).
        gateway = (IPaymentGateway)_services.GetRequiredService(adapterType);
        return true;
    }
}
