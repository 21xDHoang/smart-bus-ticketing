using Microsoft.Extensions.DependencyInjection;
using SmartBus.Api.Dtos.Payments;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test cho <see cref="PaymentGatewayResolver"/> — bảng đăng ký cổng của lớp adapter thống nhất
/// (US 6, task *"Adapter pattern thống nhất cổng thanh toán (dễ thêm cổng mới)"* — Phùng Duy
/// Hoàng).
///
/// Dựng DI RIÊNG từng test (không chạy app thật) để đếm được số lần dựng client từng cổng — ghim
/// đúng thiết kế "chỉ dựng cổng được hỏi": máy chỉ cấu hình một cổng không được vỡ vì cổng kia,
/// và lượt tra hụt không được chạm tới client nào.
/// </summary>
public class PaymentGatewayResolverTests
{
    [Theory]
    [InlineData("MoMo", PaymentProviderCodes.MoMo)]
    [InlineData("momo", PaymentProviderCodes.MoMo)]
    [InlineData("VNPay", PaymentProviderCodes.VnPay)]
    [InlineData("vnpay", PaymentProviderCodes.VnPay)]
    [InlineData("ZaloPay", PaymentProviderCodes.ZaloPay)]
    [InlineData("zalopay", PaymentProviderCodes.ZaloPay)]
    [InlineData("BankCard", PaymentProviderCodes.BankCard)]
    [InlineData("bankcard", PaymentProviderCodes.BankCard)]
    public void TryGet_khop_ma_khong_phan_biet_hoa_thuong(string input, string expectedCode)
    {
        using var provider = BuildProvider(new BuildCounters());
        using var scope = provider.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IPaymentGatewayResolver>();

        Assert.True(resolver.TryGet(input, out var gateway));
        Assert.NotNull(gateway);
        Assert.Equal(expectedCode, gateway.ProviderCode);
    }

    /// <summary>
    /// Mã NGOÀI bốn mã của hợp đồng — 'BankTransfer' là mã hợp đồng đã loại (không cổng nào triển
    /// khai, xem bảng "Enum chốt" của docs/api-contract.md); ZaloPay/BankCard đã có adapter nên
    /// KHÔNG còn nằm trong nhóm này.
    /// </summary>
    [Theory]
    [InlineData("BankTransfer")]
    [InlineData("Cash")]
    [InlineData("")]
    public void TryGet_ma_chua_mo_tra_false(string input)
    {
        using var provider = BuildProvider(new BuildCounters());
        using var scope = provider.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IPaymentGatewayResolver>();

        Assert.False(resolver.TryGet(input, out var gateway));
        Assert.Null(gateway);
    }

    [Fact]
    public void TryGet_null_tra_false()
    {
        using var provider = BuildProvider(new BuildCounters());
        using var scope = provider.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IPaymentGatewayResolver>();

        Assert.False(resolver.TryGet(null!, out var gateway));
        Assert.Null(gateway);
    }

    [Fact]
    public void SupportedProviders_liet_ke_bon_cong_da_mo()
    {
        using var provider = BuildProvider(new BuildCounters());
        using var scope = provider.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IPaymentGatewayResolver>();

        var supported = resolver.SupportedProviders;

        // Đúng bốn mã của hợp đồng — BankCard nằm trong đây dù nó không phải một cổng riêng: nó là
        // kênh thẻ của VNPay, và endpoint vẫn phải tra được nó như mọi mã khác.
        Assert.Equal(4, supported.Count);
        Assert.Contains(PaymentProviderCodes.MoMo, supported);
        Assert.Contains(PaymentProviderCodes.VnPay, supported);
        Assert.Contains(PaymentProviderCodes.ZaloPay, supported);
        Assert.Contains(PaymentProviderCodes.BankCard, supported);
    }

    [Fact]
    public void Chi_dung_client_cong_duoc_hoi()
    {
        var counters = new BuildCounters();
        using var provider = BuildProvider(counters);
        using var scope = provider.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IPaymentGatewayResolver>();

        Assert.True(resolver.TryGet(PaymentProviderCodes.MoMo, out _));
        Assert.Equal(1, counters.MoMo);
        Assert.Equal(0, counters.VnPay); // cổng chưa được hỏi thì client chưa được dựng

        Assert.True(resolver.TryGet(PaymentProviderCodes.VnPay, out _));
        Assert.Equal(1, counters.VnPay);
        Assert.Equal(1, counters.MoMo); // scoped nhớ trong scope — không dựng lại

        // BankCard hỏi SAU VNPay: hai adapter khác nhau nhưng chung một client VNPay, mà client là
        // scoped nên vẫn chỉ dựng một lần — đây là lý do BankCard uỷ thác được cho adapter VNPay.
        Assert.True(resolver.TryGet(PaymentProviderCodes.BankCard, out _));
        Assert.Equal(1, counters.VnPay);
    }

    [Fact]
    public void Tra_ma_chua_mo_khong_cham_client_nao()
    {
        var counters = new BuildCounters();
        using var provider = BuildProvider(counters);
        using var scope = provider.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IPaymentGatewayResolver>();

        Assert.False(resolver.TryGet("BankTransfer", out _));
        Assert.Equal(0, counters.MoMo);
        Assert.Equal(0, counters.VnPay);
        Assert.Equal(0, counters.ZaloPay);
    }

    /// <summary>Đếm số lần client từng cổng được DI dựng — nguyên liệu ghim thiết kế dựng muộn.</summary>
    private sealed class BuildCounters
    {
        public int MoMo { get; set; }

        public int VnPay { get; set; }

        public int ZaloPay { get; set; }
    }

    private static ServiceProvider BuildProvider(BuildCounters counters)
    {
        var services = new ServiceCollection();

        services.AddScoped<IMoMoGatewayService>(_ =>
        {
            counters.MoMo++;
            return new StubMoMoGatewayService();
        });
        services.AddScoped<IVnPayGatewayService>(_ =>
        {
            counters.VnPay++;
            return new StubVnPayGatewayService();
        });
        services.AddScoped<IZaloPayGatewayService>(_ =>
        {
            counters.ZaloPay++;
            return new StubZaloPayGatewayService();
        });
        services.AddScoped<MoMoGatewayAdapter>();
        services.AddScoped<VnPayGatewayAdapter>();
        services.AddScoped<ZaloPayGatewayAdapter>();
        // BankCardGatewayAdapter nhận CẢ client VNPay (để ghim kênh thẻ) lẫn adapter VNPay (để uỷ
        // thác phép chuẩn hoá callback) — đăng ký thiếu một trong hai là resolver nổ lúc dựng.
        services.AddScoped<BankCardGatewayAdapter>();
        services.AddScoped<IPaymentGatewayResolver, PaymentGatewayResolver>();

        return services.BuildServiceProvider();
    }

    /// <summary>Đứng chỗ client MoMo thật — resolver chỉ cần dựng nó, không gọi nghiệp vụ.</summary>
    private sealed class StubMoMoGatewayService : IMoMoGatewayService
    {
        public Task<MoMoCreatePaymentResult> CreatePaymentAsync(
            MoMoCreatePaymentRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public bool IsValidCallback(MoMoCallback callback) => throw new NotSupportedException();

        public Task<MoMoQueryTransactionResult> QueryTransactionAsync(
            string orderId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubVnPayGatewayService : IVnPayGatewayService
    {
        public string CreatePaymentUrl(VnPayCreatePaymentRequest request) => throw new NotSupportedException();

        public bool IsValidSignature(IReadOnlyDictionary<string, string> parameters)
            => throw new NotSupportedException();
    }

    private sealed class StubZaloPayGatewayService : IZaloPayGatewayService
    {
        public Task<ZaloPayCreateOrderResult> CreateOrderAsync(
            ZaloPayCreateOrderRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public bool IsValidCallback(ZaloPayCallback callback) => throw new NotSupportedException();

        public ZaloPayCallbackData? ParseCallbackData(string data) => throw new NotSupportedException();

        public Task<ZaloPayQueryResult> QueryOrderAsync(
            string appTransId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
