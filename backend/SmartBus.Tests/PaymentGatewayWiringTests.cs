using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using SmartBus.Api.Dtos.Payments;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Nối dây thật trong Program.cs — task *"Adapter pattern thống nhất cổng thanh toán (dễ thêm cổng
/// mới)"* — Phùng Duy Hoàng. Chạy trên APP THẬT qua <see cref="TestAppFactory"/> (CSDL InMemory,
/// job noop) và bơm khoá cổng giả bằng UseSetting — chứng minh các dòng đăng ký trong Program.cs đủ
/// để resolver dựng adapter thật, và ghim đúng thiết kế "chỉ dựng cổng được hỏi": máy chỉ cấu hình
/// MoMo vẫn resolve MoMo ngon lành, còn cổng thiếu khoá chỉ nổ ĐÚNG LÚC được hỏi.
///
/// Hai adapter ZaloPay/BankCard thêm sau (task *"Tích hợp ZaloPay và thẻ ngân hàng"* — Nguyễn Duy
/// Kiên) đi đúng con đường mà lớp adapter này mở ra: thêm hằng mã cổng + một dòng trong bảng đăng
/// ký của resolver + một dòng AddScoped, KHÔNG sửa file nào của lớp adapter.
/// </summary>
public class PaymentGatewayWiringTests
{
    /// <summary>Khoá giả rõ ràng của test — không phải khoá đối tác thật (luật 2).</summary>
    private static void BomCauHinhMoMo(IWebHostBuilder builder)
    {
        builder.UseSetting("MoMo:PartnerCode", "MOMO_TEST");
        builder.UseSetting("MoMo:AccessKey", "access-key-test");
        builder.UseSetting("MoMo:SecretKey", "secret-key-test");
    }

    /// <summary>Khoá giả rõ ràng của test — không phải khoá đối tác thật (luật 2).</summary>
    private static void BomCauHinhZaloPay(IWebHostBuilder builder)
    {
        // AppId phải là số nguyên: client ZaloPay kiểm ngay lúc dựng (tài liệu cổng khai app_id là
        // int32), nên đặt "2553" chứ không đặt một chuỗi mô tả như hai khoá kia.
        builder.UseSetting("ZaloPay:AppId", "2553");
        builder.UseSetting("ZaloPay:Key1", "key1-gia-cua-test");
        builder.UseSetting("ZaloPay:Key2", "key2-gia-cua-test");
    }

    [Fact]
    public void App_that_resolve_duoc_bon_adapter()
    {
        using var factory = new TestAppFactory().WithWebHostBuilder(builder =>
        {
            BomCauHinhMoMo(builder);
            BomCauHinhZaloPay(builder);
            builder.UseSetting("VnPay:TmnCode", "CGXZLS0Z");
            builder.UseSetting("VnPay:HashSecret", "khoa-bi-mat-gia-cua-test");
        });
        using var scope = factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IPaymentGatewayResolver>();

        Assert.True(resolver.TryGet(PaymentProviderCodes.MoMo, out var momo));
        Assert.IsType<MoMoGatewayAdapter>(momo);

        Assert.True(resolver.TryGet(PaymentProviderCodes.VnPay, out var vnpay));
        Assert.IsType<VnPayGatewayAdapter>(vnpay);

        Assert.True(resolver.TryGet(PaymentProviderCodes.ZaloPay, out var zalopay));
        Assert.IsType<ZaloPayGatewayAdapter>(zalopay);

        // BankCard là adapter RIÊNG nhưng đứng trên client VNPay — cả hai điều đó đều phải đúng.
        Assert.True(resolver.TryGet(PaymentProviderCodes.BankCard, out var bankCard));
        Assert.IsType<BankCardGatewayAdapter>(bankCard);

        Assert.Contains(PaymentProviderCodes.MoMo, resolver.SupportedProviders);
        Assert.Contains(PaymentProviderCodes.VnPay, resolver.SupportedProviders);
        Assert.Contains(PaymentProviderCodes.ZaloPay, resolver.SupportedProviders);
        Assert.Contains(PaymentProviderCodes.BankCard, resolver.SupportedProviders);
    }

    [Fact]
    public void Cong_chua_mo_khong_lan_voi_cong_thieu_cau_hinh()
    {
        using var factory = new TestAppFactory().WithWebHostBuilder(BomCauHinhMoMo);
        using var scope = factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IPaymentGatewayResolver>();

        // Mã NGOÀI bốn mã của hợp đồng thì không có adapter — chuyện BÌNH THƯỜNG, trả false để
        // endpoint báo 400 "chưa mở" kèm danh sách cổng đã mở, không ném.
        Assert.False(resolver.TryGet("BankTransfer", out var gateway));
        Assert.Null(gateway);
        Assert.Contains(PaymentProviderCodes.MoMo, resolver.SupportedProviders);

        // Còn ZaloPay ĐÃ có adapter, chỉ thiếu cấu hình — chạy app với mỗi MoMo thì đây là ca "lỗi
        // triển khai", phải NÉM nêu đúng tên khoá thiếu chứ không được trả false: trả false là
        // endpoint báo 400 "chưa mở" và ta đi tìm nhầm hướng trong nhiều giờ.
        var exception = Assert.Throws<InvalidOperationException>(
            () => resolver.TryGet(PaymentProviderCodes.ZaloPay, out _));
        Assert.Contains("ZaloPay:AppId", exception.Message);
    }

    [Fact]
    public void May_chi_cau_hinh_MoMo_van_resolve_MoMo_con_VNPay_no_dung_luc_hoi()
    {
        using var factory = new TestAppFactory().WithWebHostBuilder(builder =>
        {
            BomCauHinhMoMo(builder);
            // VNPay cố ý có TmnCode nhưng THIẾU HashSecret — để thông báo lỗi tất định, nêu đúng
            // khoá thiếu (luật 2).
            builder.UseSetting("VnPay:TmnCode", "CGXZLS0Z");
        });
        using var scope = factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IPaymentGatewayResolver>();

        // Cổng được hỏi dựng được — cấu hình thiếu của cổng KHÁC không kéo theo (nhờ resolver dựng
        // muộn theo mã; nhận IEnumerable<IPaymentGateway> là máy này vỡ ngay từ lượt hỏi đầu).
        Assert.True(resolver.TryGet(PaymentProviderCodes.MoMo, out var momo));
        Assert.IsType<MoMoGatewayAdapter>(momo);

        // Chỉ khi hỏi tới VNPay mới nổ — và nổ NÊU ĐÚNG TÊN KHOÁ THIẾU, cố ý khác hẳn "chưa mở".
        var exception = Assert.Throws<InvalidOperationException>(
            () => resolver.TryGet(PaymentProviderCodes.VnPay, out _));
        Assert.Contains("VnPay:HashSecret", exception.Message);
    }
}
