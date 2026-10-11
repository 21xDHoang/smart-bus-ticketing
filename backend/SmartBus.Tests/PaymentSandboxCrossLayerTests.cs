using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Payments;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test API thanh toán với sandbox của 3 cổng (US 6, task dòng r36 — Giàng A Vàng).
///
/// ⚠️ Sáu endpoint <c>/payments</c> của hợp đồng (docs/api-contract.md, mục "Thanh toán") CHƯA
/// ĐƯỢC DỰNG — không có controller thanh toán nào trong <c>SmartBus.Api/Controllers</c>. Nên bài
/// này KHÔNG gọi HTTP: nó đi đúng con đường mà endpoint sẽ đi khi có — dữ liệu thô của cổng →
/// adapter qua <see cref="IPaymentGatewayResolver"/> → <see cref="IPaymentSettlementService"/> →
/// dòng <c>Payments</c> trong CSDL — và lấy cả hai đầu từ DI THẬT của app
/// (<see cref="TestAppFactory"/>), không dựng tay đối tượng nào.
///
/// Vì sao cần: 11 file test thanh toán đang có đều test RỜI từng mảnh. Bộ test adapter chỉ chạm
/// adapter; <c>PaymentSettlementServiceTests</c> dựng thẳng <see cref="PaymentCallbackResult"/>
/// ("ở đây dựng thẳng, không qua adapter" — chú thích của chính nó); hai file <c>*WiringTests</c>
/// chỉ resolve DI rồi thôi. Nghĩa là lời hứa chống trừ tiền hai lần CHƯA từng chạy xuyên ranh giới
/// adapter → CSDL — đúng chỗ bài này phủ.
///
/// TOÀN BỘ OFFLINE: không cuộc gọi mạng nào. Vector chữ ký chép nguyên văn từ các test đã có
/// (tính sẵn ngoài C#); khoá dùng lại đều là khoá giả hoặc khoá sandbox công khai ĐÃ NẰM TRONG
/// repo — không thêm bí mật mới (luật 2).
/// </summary>
public class PaymentSandboxCrossLayerTests
{
    // ---------------------------------------------------------------------------------------
    // Khoá — chép từ các file test đã có, không phải khoá đối tác thật (luật 2, repo public)
    // ---------------------------------------------------------------------------------------

    /// <summary>Cùng bộ giá trị đã ký vector ở <c>MoMoGatewayServiceTests</c>.</summary>
    private const string MoMoPartnerCode = "MOMO0T1D20220107";
    private const string MoMoAccessKey = "F8BBA842ECF85";
    private const string MoMoSecretKey = "K951B6PE1waDMi640xX08PD3vg6EkVlz";

    /// <summary>Bộ khoá SANDBOX CÔNG KHAI của VNPay — in nguyên văn trong tài liệu cổng.</summary>
    private const string VnPayTmnCode = "CGXZLS0Z";
    private const string VnPayHashSecret = "XNBCJFAKAZQSGTARRLGCHVZWCIOIGSHN";

    /// <summary>Bộ khoá GIẢ của test ZaloPay.</summary>
    private const string ZaloPayAppId = "2553";
    private const string ZaloPayKey1 = "test-key1-khong-phai-khoa-that";
    private const string ZaloPayKey2 = "test-key2-khong-phai-khoa-that";

    // ---------------------------------------------------------------------------------------
    // Vector chữ ký tính SẴN — chép nguyên văn, KHÔNG tự ký lại trong test
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Thân IPN MoMo (13 trường, chữ ký tính trên thứ tự bảng chữ cái) — cùng bộ giá trị với
    /// <c>MoMoGatewayServiceTests.ValidCallback()</c> và <c>CallbackSignatureVector</c>.
    /// </summary>
    private const string MoMoThanIpn = """
        {"partnerCode":"MOMO0T1D20220107","orderId":"63e61ad2318d66f22ecad8f7",
         "requestId":"84f36d78-a97e-4bd2-afb4-5f89cbaac15e","amount":175000,
         "orderInfo":"pay with MoMo","orderType":"momo_wallet","transId":2757953842,
         "resultCode":0,"message":"Success","payType":"webApp",
         "responseTime":1700000000000,"extraData":"",
         "signature":"7ad94c925b148569e2195b1b9cc0d802d071f4d222a10a50abcfdd88b2574e93"}
        """;

    /// <summary>
    /// Chữ ký của <see cref="MoMoThanIpn"/> khi số tiền bị sửa mà chữ ký giữ nguyên — bản sao
    /// <c>MoMoGatewayServiceTests</c> dùng ở ca "sửa số tiền thắng chữ ký".
    /// </summary>
    private const string MoMoSoTienSua = "\"amount\":175001";

    /// <summary>
    /// Vector C của VNPay — bộ tham số callback ĐÃ GIẢI MÃ, qua adapter ra
    /// <c>IsValid = true</c> VÀ <c>Succeeded = true</c>. Chép từ <c>VnPayGatewayAdapterTests</c>.
    ///
    /// ⚠️ KHÁC <c>SecureHashVectorA</c> của <c>VnPayGatewayServiceTests</c>: bộ đó là vector KÝ URL
    /// TẠO ĐƠN, thiếu <c>vnp_ResponseCode</c>/<c>vnp_TransactionStatus</c> nên chỉ ra
    /// <c>IsValid = true, Succeeded = false</c> — dùng nhầm là ca "đã thu tiền" đỏ oan.
    /// </summary>
    private const string VnPayChuKyThanhCong =
        "7345312C162CEA5FBE6DD3A90B1BE25028B92712A81768D2ACDA8810DBA4FC2BB8C12E07BBC1ADF31B35A82A107F33C9BB1D4BFEBACB1398CEB89013F2A6B976";

    /// <summary>
    /// Chuỗi <c>data</c> cổng ZaloPay gửi — ĐÚNG nguyên văn đã đưa vào script tính chữ ký, vì MAC
    /// tính trên chính chuỗi này (thứ tự khoá và khoảng trắng không được dựng lại).
    /// </summary>
    private const string ZaloPayData =
        "{\"app_id\":2553,\"app_trans_id\":\"261010_PM-8f3a2c1d\",\"app_user\":\"smartbus\","
        + "\"amount\":175000,\"zp_trans_id\":2609270000012345,\"server_time\":1791621120000,"
        + "\"channel\":38}";

    /// <summary>MAC hợp lệ của <see cref="ZaloPayData"/> — HMAC-SHA256 bằng Key2.</summary>
    private const string ZaloPayMac = "8a6f812c2e8c56212a8402f4ea7ab9626d3be3932908fe69f744113c6377f80c";

    /// <summary>Cùng chuỗi <c>data</c> nhưng ký bằng Key1 (khoá chiều TA GỬI ĐI) — phải bị từ chối.</summary>
    private const string ZaloPayMacKyBangKey1 =
        "f18066df8723e88a7d934e64f4e0e5f1718a5047a43f53b96299b73aa8a6a966";

    /// <summary>Cùng chuỗi <c>data</c>, số tiền bị sửa, MAC giữ nguyên.</summary>
    private const string ZaloPayDataSuaSoTien = "\"amount\":175001";

    // ---------------------------------------------------------------------------------------
    // Mốc kỳ vọng
    // ---------------------------------------------------------------------------------------

    /// <summary>MoMo responseTime mili-giây epoch → UTC (adapter tự đổi).</summary>
    private static readonly DateTime MoMoMocCong =
        DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000).UtcDateTime;

    /// <summary>VNPay vnp_PayDate "20261010153200" giờ Việt Nam → UTC (adapter tự trừ 7 giờ).</summary>
    private static readonly DateTime VnPayMocCong = new(2026, 10, 10, 8, 32, 0, DateTimeKind.Utc);

    // ---------------------------------------------------------------------------------------
    // Nhóm 1 — mặc định cấu hình của cả ba cổng phải trỏ SANDBOX
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Chỉ bơm khoá, KHÔNG bơm địa chỉ cổng — địa chỉ phải rơi về hằng sandbox trong code. Đây là
    /// chốt an toàn: quên cấu hình endpoint ở môi trường thật thì giao dịch đi vào sandbox (thấy
    /// ngay), không đi vào cổng thật (không thấy gì cho tới lúc đối soát).
    /// </summary>
    [Fact]
    public void Chi_bom_khoa_thi_dia_chi_cong_mac_dinh_la_sandbox()
    {
        using var factory = NhaMayTest();
        using var scope = factory.Services.CreateScope();

        var momo = scope.ServiceProvider.GetRequiredService<IOptions<MoMoOptions>>().Value;
        var zaloPay = scope.ServiceProvider.GetRequiredService<IOptions<ZaloPayOptions>>().Value;
        var vnPay = scope.ServiceProvider.GetRequiredService<IOptions<VnPayOptions>>().Value;

        Assert.Equal(MoMoOptions.DefaultEndpoint, momo.Endpoint);
        Assert.StartsWith("https://test-payment.momo.vn", momo.Endpoint);

        Assert.Equal(ZaloPayOptions.DefaultEndpoint, zaloPay.Endpoint);
        Assert.StartsWith("https://sb-openapi.zalopay.vn", zaloPay.Endpoint);

        Assert.Equal(VnPayOptions.DefaultPaymentUrl, vnPay.PaymentUrl);
        Assert.StartsWith("https://sandbox.vnpayment.vn", vnPay.PaymentUrl);
    }

    /// <summary>
    /// VNPay ký URL ngay tại chỗ nên chứng minh được tới tận chuỗi trả về mà không cần mạng: URL
    /// khách bị đẩy sang phải nằm trên host sandbox.
    /// </summary>
    [Fact]
    public async Task VnPay_tra_ve_url_nam_tren_host_sandbox_va_khong_ghim_kenh()
    {
        using var factory = NhaMayTest();
        using var scope = factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IPaymentGatewayResolver>();

        Assert.True(resolver.TryGet(PaymentProviderCodes.VnPay, out var cong));
        var ket = await cong!.InitiateAsync(YeuCauTaoDon());

        Assert.True(ket.Success);
        Assert.StartsWith(VnPayOptions.DefaultPaymentUrl + "?", ket.RedirectUrl);

        // Không ghim kênh: để khách tự chọn phương thức trên trang cổng.
        Assert.DoesNotContain("vnp_BankCode", ket.RedirectUrl);
    }

    /// <summary>
    /// Khác biệt DUY NHẤT giữa hai mã phương thức <c>BankCard</c> và <c>VNPay</c> là tham số
    /// <c>vnp_BankCode</c> — và nó phải lộ ra ngay trên URL tạo đơn, vì đó là thứ duy nhất khiến
    /// "thẻ ngân hàng" vào thẳng kênh thẻ còn "VNPay" thì không.
    /// </summary>
    [Fact]
    public async Task BankCard_ghim_kenh_the_con_VNPay_de_khach_tu_chon()
    {
        using var factory = NhaMayTest();
        using var scope = factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IPaymentGatewayResolver>();

        Assert.True(resolver.TryGet(PaymentProviderCodes.BankCard, out var bankCard));
        Assert.True(resolver.TryGet(PaymentProviderCodes.VnPay, out var vnPay));

        var urlThe = (await bankCard!.InitiateAsync(YeuCauTaoDon())).RedirectUrl;
        var urlVnPay = (await vnPay!.InitiateAsync(YeuCauTaoDon())).RedirectUrl;

        Assert.Contains("vnp_BankCode=VNBANK", urlThe);
        Assert.DoesNotContain("vnp_BankCode", urlVnPay);

        // Cả hai vẫn là VNPay: cùng host sandbox, cùng tham số tạo đơn.
        Assert.StartsWith(VnPayOptions.DefaultPaymentUrl, urlThe);
        Assert.StartsWith(VnPayOptions.DefaultPaymentUrl, urlVnPay);
    }

    // ---------------------------------------------------------------------------------------
    // Nhóm 2 — vòng đời xuyên tầng: payload sandbox → adapter → chốt giao dịch trong CSDL
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(PaymentProviderCodes.MoMo)]
    [InlineData(PaymentProviderCodes.VnPay)]
    [InlineData(PaymentProviderCodes.ZaloPay)]
    public async Task Callback_sandbox_qua_adapter_roi_chot_duoc_giao_dich(string ma)
    {
        var cong = CongCua(ma);
        using var factory = NhaMayTest();
        await GieoGiaoDich(factory, cong);

        var ketQua = await TrongMotRequest(factory, async (resolver, settlement) =>
        {
            Assert.True(resolver.TryGet(ma, out var adapter));

            // Bước 1 — lớp nhận callback thật, đọc dữ liệu thô đúng như cổng gửi.
            var tuAdapter = adapter!.VerifyCallback(cong.Payload());
            Assert.True(tuAdapter.IsValid, "chữ ký sandbox phải qua được adapter");
            Assert.True(tuAdapter.Succeeded, "cổng báo đã thu tiền");
            Assert.Equal(cong.PaymentCode, tuAdapter.PaymentCode);
            Assert.Equal(cong.Amount, tuAdapter.Amount);

            // Bước 2 — đưa THẲNG thứ adapter vừa trả về vào bước chốt, không dựng tay lại.
            return await settlement.SettleAsync(ma, tuAdapter);
        });

        Assert.True(ketQua.Success, $"chốt {ma} phải thành công");
        Assert.True(ketQua.Data!.SettledNow);
        Assert.Equal(PaymentStatus.Success, ketQua.Data.Status);
        Assert.True(ketQua.Data.ShouldIssueTickets);

        // Đọc lại bằng context KHÁC — thay đổi phải đã xuống CSDL, không chỉ nằm trong change tracker.
        var sau = await DocLai(factory, cong.PaymentCode);

        Assert.Equal(PaymentStatus.Success, sau.Status);
        Assert.Equal(cong.GatewayTransactionId, sau.GatewayTransactionId);
        Assert.NotNull(sau.PaidAt);

        // Cổng gửi mốc thu tiền thì cột phải mang ĐÚNG mốc của cổng (đã quy về UTC), không phải
        // giờ hệ thống — cột này là thứ job đối soát đem so với sao kê.
        if (cong.MocCongCuaCong is { } moc)
        {
            Assert.Equal(moc, sau.PaidAt!.Value);
        }
    }

    /// <summary>
    /// Lời hứa trung tâm của cả luồng thanh toán: cổng gửi lại callback (MoMo retry tới khi nhận
    /// 204) thì KHÔNG trừ tiền lần hai. Mỗi lượt chốt đi qua một scope DI riêng — đúng như mỗi lần
    /// cổng gọi về là một request mới với <c>AppDbContext</c> mới, không đi đường tắt qua change
    /// tracker của lượt trước.
    /// </summary>
    [Theory]
    [InlineData(PaymentProviderCodes.MoMo)]
    [InlineData(PaymentProviderCodes.VnPay)]
    [InlineData(PaymentProviderCodes.ZaloPay)]
    public async Task Goi_lai_lan_hai_khong_chot_lan_nua(string ma)
    {
        var cong = CongCua(ma);
        using var factory = NhaMayTest();
        await GieoGiaoDich(factory, cong);

        var lanDau = await TrongMotRequest(factory, async (resolver, settlement) =>
        {
            resolver.TryGet(ma, out var adapter);
            return await settlement.SettleAsync(ma, adapter!.VerifyCallback(cong.Payload()));
        });

        Assert.True(lanDau.Data!.SettledNow);
        var sauLanDau = await DocLai(factory, cong.PaymentCode);

        var lanHai = await TrongMotRequest(factory, async (resolver, settlement) =>
        {
            resolver.TryGet(ma, out var adapter);
            return await settlement.SettleAsync(ma, adapter!.VerifyCallback(cong.Payload()));
        });

        Assert.True(lanHai.Success, "bản gửi lại vẫn phải trả 2xx cho cổng, nếu không cổng retry vô hạn");
        Assert.False(lanHai.Data!.SettledNow);

        var sauLanHai = await DocLai(factory, cong.PaymentCode);

        Assert.Equal(PaymentStatus.Success, sauLanHai.Status);
        Assert.Equal(sauLanDau.PaidAt, sauLanHai.PaidAt);
        Assert.Equal(sauLanDau.GatewayTransactionId, sauLanHai.GatewayTransactionId);
    }

    /// <summary>
    /// 🔴 Ghi nhận HÀNH VI ĐANG CÓ, không phải chốt rằng nó đúng (báo cáo mục T2): callback ZaloPay
    /// CÓ gửi mốc thu tiền (<c>server_time</c>), nhưng adapter không đọc nên cột <c>PaidAt</c> rơi về
    /// giờ hệ thống — khác MoMo (<c>responseTime</c>) và VNPay (<c>vnp_PayDate</c>) đều lấy giờ cổng.
    ///
    /// Ca này cố ý ghim cả hai đầu để không nói suông: mốc cổng gửi ĐÚNG BẰNG mốc mà hai cổng kia
    /// đưa vào <c>PaidAt</c>, mà vẫn bị bỏ. Sửa adapter cho đọc <c>server_time</c> thì ca này đỏ —
    /// lúc đó cập nhật kèm báo cáo.
    /// </summary>
    [Fact]
    public async Task ZaloPay_co_gui_moc_thu_tien_ma_adapter_khong_doc()
    {
        var cong = CongCua(PaymentProviderCodes.ZaloPay);
        using var factory = NhaMayTest();
        await GieoGiaoDich(factory, cong);

        // Mốc cổng gửi trong chính vector — server_time = 1791621120000 ms.
        var mocCongGui = DateTimeOffset.FromUnixTimeMilliseconds(1_791_621_120_000).UtcDateTime;
        Assert.Equal(VnPayMocCong, mocCongGui);

        var ketQua = await TrongMotRequest(factory, async (resolver, settlement) =>
        {
            resolver.TryGet(PaymentProviderCodes.ZaloPay, out var adapter);

            var tuAdapter = adapter!.VerifyCallback(cong.Payload());
            Assert.True(tuAdapter.IsValid);
            Assert.True(tuAdapter.Succeeded);

            // Cổng gửi mốc, adapter không lấy: khe hở nằm đúng ở đây.
            Assert.Null(tuAdapter.PaidAt);

            return await settlement.SettleAsync(PaymentProviderCodes.ZaloPay, tuAdapter);
        });

        Assert.True(ketQua.Data!.SettledNow);

        var sau = await DocLai(factory, cong.PaymentCode);

        // Hệ quả xuống tới CSDL: mốc lưu là giờ hệ thống lúc chốt, KHÔNG phải mốc cổng ghi nhận.
        Assert.NotNull(sau.PaidAt);
        Assert.True(
            sau.PaidAt > mocCongGui,
            "mốc lưu phải là giờ chốt (rơi về DateTime.UtcNow), không phải server_time của cổng");
    }

    // ---------------------------------------------------------------------------------------
    // Nhóm 3 — dữ liệu cổng sai thì không lật được trạng thái
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Sửa số tiền mà giữ chữ ký cũ: adapter phải từ chối, và bước chốt phải chặn TRƯỚC khi chạm
    /// CSDL — dòng giao dịch còn nguyên Pending.
    /// </summary>
    [Theory]
    [InlineData(PaymentProviderCodes.MoMo)]
    [InlineData(PaymentProviderCodes.VnPay)]
    [InlineData(PaymentProviderCodes.ZaloPay)]
    public async Task Sua_so_tien_thi_chu_ky_sai_va_dong_giu_Pending(string ma)
    {
        var cong = CongCua(ma);
        using var factory = NhaMayTest();
        await GieoGiaoDich(factory, cong);

        var ketQua = await TrongMotRequest(factory, async (resolver, settlement) =>
        {
            resolver.TryGet(ma, out var adapter);

            var tuAdapter = adapter!.VerifyCallback(cong.PayloadSuaSoTien());
            Assert.False(tuAdapter.IsValid, "sửa số tiền thì chữ ký phải sai");
            Assert.False(tuAdapter.Succeeded);

            return await settlement.SettleAsync(ma, tuAdapter);
        });

        Assert.False(ketQua.Success);
        Assert.Equal(ServiceErrorKind.Invalid, ketQua.ErrorKind);

        await KiemDongConNguyenPending(factory, cong);
    }

    /// <summary>
    /// Băm một ký tự của chữ ký: vẫn đúng hình dạng (hex, đúng độ dài), chỉ sai giá trị. Adapter
    /// không được để lọt, và cũng không được ném ra ngoài.
    /// </summary>
    [Theory]
    [InlineData(PaymentProviderCodes.MoMo)]
    [InlineData(PaymentProviderCodes.VnPay)]
    [InlineData(PaymentProviderCodes.ZaloPay)]
    public async Task Chu_ky_bi_bam_thi_khong_qua_duoc_adapter(string ma)
    {
        var cong = CongCua(ma);
        using var factory = NhaMayTest();
        await GieoGiaoDich(factory, cong);

        await TrongMotRequest(factory, (resolver, _) =>
        {
            resolver.TryGet(ma, out var adapter);

            var tuAdapter = adapter!.VerifyCallback(cong.PayloadBamChuKy());

            Assert.False(tuAdapter.IsValid);
            Assert.False(tuAdapter.Succeeded);

            return Task.FromResult(true);
        });

        await KiemDongConNguyenPending(factory, cong);
    }

    /// <summary>
    /// Ký bằng KHOÁ SAI, không phải băm chữ ký: ca này mới đúng thứ một kẻ giả mạo làm được (hắn
    /// không có khoá bí mật nên phải ký lại bằng khoá khác).
    /// </summary>
    [Fact]
    public async Task ZaloPay_ky_bang_Key1_thi_bi_tu_choi()
    {
        using var factory = NhaMayTest();
        using var scope = factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IPaymentGatewayResolver>();

        Assert.True(resolver.TryGet(PaymentProviderCodes.ZaloPay, out var adapter));

        // Callback phải ký bằng Key2 (khoá cổng gửi cho TA); Key1 là khoá chiều ta gửi đi.
        var tuAdapter = adapter!.VerifyCallback(new PaymentCallbackInput
        {
            Body = VoZaloPay(ZaloPayData, ZaloPayMacKyBangKey1),
        });

        Assert.False(tuAdapter.IsValid);
        Assert.False(tuAdapter.Succeeded);
    }

    /// <summary>
    /// Chữ ký HỢP LỆ nhưng lệch số tiền so với bản ghi: đây là cách duy nhất chạm được bước đối
    /// chiếu số tiền (sửa <c>vnp_Amount</c> là hỏng chữ ký trước, rơi vào ca trên).
    /// </summary>
    [Fact]
    public async Task Lech_so_tien_thi_khong_tim_thay_giao_dich()
    {
        var cong = CongCua(PaymentProviderCodes.VnPay);
        using var factory = NhaMayTest();

        // Gieo SAI số tiền — payload và chữ ký không đổi nên vẫn hợp lệ.
        await GieoGiaoDich(factory, cong, soTien: cong.Amount - 1_000m);

        var ketQua = await TrongMotRequest(factory, async (resolver, settlement) =>
        {
            resolver.TryGet(PaymentProviderCodes.VnPay, out var adapter);

            var tuAdapter = adapter!.VerifyCallback(cong.Payload());
            Assert.True(tuAdapter.IsValid);
            Assert.True(tuAdapter.Succeeded);

            return await settlement.SettleAsync(PaymentProviderCodes.VnPay, tuAdapter);
        });

        Assert.False(ketQua.Success);
        Assert.Equal(ServiceErrorKind.NotFound, ketQua.ErrorKind);

        await KiemDongConNguyenPending(factory, cong);
    }

    /// <summary>
    /// Hai cổng KHÔNG lẫn nhau — và ca này không dựng tình huống giả: vector VNPay và ZaloPay
    /// TRÙNG NHAU cả mã đơn lẫn số tiền, nên chỉ còn cột phương thức phân biệt được. Callback
    /// ZaloPay hợp lệ vẫn không được chốt giao dịch VNPay.
    /// </summary>
    [Fact]
    public async Task Callback_ZaloPay_khong_chot_duoc_giao_dich_VNPay_du_trung_ma_don_va_so_tien()
    {
        var zaloPay = CongCua(PaymentProviderCodes.ZaloPay);
        var vnPay = CongCua(PaymentProviderCodes.VnPay);

        // Chốt lại giả định của chính ca này — trùng mã đơn và trùng số tiền là sự thật của vector,
        // không phải ta sắp đặt.
        Assert.Equal(vnPay.PaymentCode, zaloPay.PaymentCode);
        Assert.Equal(vnPay.Amount, zaloPay.Amount);

        using var factory = NhaMayTest();
        await GieoGiaoDich(factory, vnPay); // dòng mang phương thức VNPay

        var ketQua = await TrongMotRequest(factory, async (resolver, settlement) =>
        {
            resolver.TryGet(PaymentProviderCodes.ZaloPay, out var adapter);

            var tuAdapter = adapter!.VerifyCallback(zaloPay.Payload());
            Assert.True(tuAdapter.IsValid, "callback ZaloPay này hoàn toàn hợp lệ với chính cổng nó");

            return await settlement.SettleAsync(PaymentProviderCodes.ZaloPay, tuAdapter);
        });

        Assert.False(ketQua.Success);
        Assert.Equal(ServiceErrorKind.NotFound, ketQua.ErrorKind);

        await KiemDongConNguyenPending(factory, zaloPay);
    }

    /// <summary>
    /// Soi gương ca trên: <c>BankCard</c> dùng CHUNG hình dạng callback với <c>VNPay</c> (cùng cổng,
    /// cùng tham số <c>vnp_</c>, cùng chữ ký SHA512 — <c>BankCardGatewayAdapter.VerifyCallback</c>
    /// uỷ thác nguyên cho adapter VNPay), nên hai mã này chỉ phân biệt được bằng cột phương thức.
    /// </summary>
    [Fact]
    public async Task Callback_VNPay_khong_chot_duoc_giao_dich_the_ngan_hang()
    {
        var vnPay = CongCua(PaymentProviderCodes.VnPay);
        using var factory = NhaMayTest();

        // Dòng mang phương thức BankCard, nhưng callback y hệt callback VNPay.
        await GieoGiaoDich(factory, vnPay, maPhuongThuc: PaymentProviderCodes.BankCard);

        var ketQua = await TrongMotRequest(factory, async (resolver, settlement) =>
        {
            resolver.TryGet(PaymentProviderCodes.VnPay, out var adapter);
            return await settlement.SettleAsync(
                PaymentProviderCodes.VnPay, adapter!.VerifyCallback(vnPay.Payload()));
        });

        Assert.False(ketQua.Success);
        Assert.Equal(ServiceErrorKind.NotFound, ketQua.ErrorKind);

        await KiemDongConNguyenPending(factory, vnPay);
    }

    // ---------------------------------------------------------------------------------------
    // Nhóm 4 — khuôn chung của seam, cho CẢ BỐN mã (BankCard không có vector riêng —
    // dùng chung chữ ký VNPay — nên chỉ kiểm được phần khuôn ở đây)
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(PaymentProviderCodes.MoMo)]
    [InlineData(PaymentProviderCodes.VnPay)]
    [InlineData(PaymentProviderCodes.ZaloPay)]
    [InlineData(PaymentProviderCodes.BankCard)]
    public void Bon_cong_cung_giu_dung_khuon_seam(string ma)
    {
        using var factory = NhaMayTest();
        using var scope = factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IPaymentGatewayResolver>();

        Assert.True(resolver.TryGet(ma, out var cong));
        Assert.Equal(ma, cong!.ProviderCode);

        // Dữ liệu người ngoài gửi tới — bot dò endpoint, cổng gọi nhầm kiểu, cổng gọi nhầm cổng.
        // Không ca nào được ném ra ngoài (thành 500) và không ca nào được báo hợp lệ/thành công.
        var rac = new[]
        {
            new PaymentCallbackInput(),
            new PaymentCallbackInput { Body = string.Empty },
            new PaymentCallbackInput { Body = "   " },
            new PaymentCallbackInput { Body = "khong-phai-json" },
            new PaymentCallbackInput { Body = "{}" },
            new PaymentCallbackInput { Body = "{\"mac\":\"khong-phai-hex\",\"data\":\"{}\",\"type\":1}" },
            new PaymentCallbackInput { Body = "{\"signature\":\"day-khong-phai-hex\"}" },
            new PaymentCallbackInput { Query = new Dictionary<string, string>() },
            new PaymentCallbackInput
            {
                Query = new Dictionary<string, string> { ["vnp_SecureHash"] = "khong-phai-hex" },
            },
            new PaymentCallbackInput
            {
                Query = new Dictionary<string, string> { ["vnp_SecureHash"] = "   " },
            },
        };

        foreach (var dauVao in rac)
        {
            var ket = cong.VerifyCallback(dauVao);

            Assert.False(ket.IsValid);
            // Bất biến của cả lớp adapter: chưa kiểm chữ ký thì KHÔNG BAO GIỜ báo thành công.
            Assert.False(ket.Succeeded);
            // DTO trung tính cam kết không null — endpoint nối thẳng vào thông báo lỗi.
            Assert.NotNull(ket.PaymentCode);
            Assert.NotNull(ket.GatewayTransactionId);
            Assert.NotNull(ket.ProviderResponseCode);
            Assert.NotNull(ket.Message);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Bộ dữ liệu từng cổng
    // ---------------------------------------------------------------------------------------

    /// <summary>Một cổng: cách dựng payload, giá trị gieo vào bảng Payments, mốc kỳ vọng sau khi chốt.</summary>
    private sealed record Cong(
        string MaPhuongThuc,
        string PaymentCode,
        decimal Amount,
        string GatewayTransactionId,
        DateTime? MocCongCuaCong,
        Func<PaymentCallbackInput> Payload,
        Func<PaymentCallbackInput> PayloadSuaSoTien,
        Func<PaymentCallbackInput> PayloadBamChuKy);

    /// <summary>
    /// Bộ giá trị của từng cổng — mã đơn và số tiền lấy ĐÚNG từ vector đã ký, không dựng lại.
    /// </summary>
    private static Cong CongCua(string ma) => ma switch
    {
        PaymentProviderCodes.MoMo => new Cong(
            MaPhuongThuc: PaymentProviderCodes.MoMo,
            PaymentCode: "63e61ad2318d66f22ecad8f7",
            Amount: 175_000m,
            GatewayTransactionId: "2757953842",
            MocCongCuaCong: MoMoMocCong,
            Payload: () => new PaymentCallbackInput { Body = MoMoThanIpn },
            PayloadSuaSoTien: () => new PaymentCallbackInput
            {
                Body = MoMoThanIpn.Replace("\"amount\":175000", MoMoSoTienSua),
            },
            PayloadBamChuKy: () => new PaymentCallbackInput
            {
                Body = MoMoThanIpn.Replace(
                    "7ad94c925b148569e2195b1b9cc0d802d071f4d222a10a50abcfdd88b2574e93",
                    BamChuKy("7ad94c925b148569e2195b1b9cc0d802d071f4d222a10a50abcfdd88b2574e93")),
            }),

        PaymentProviderCodes.VnPay => CongVnPay(),

        PaymentProviderCodes.ZaloPay => new Cong(
            MaPhuongThuc: PaymentProviderCodes.ZaloPay,
            PaymentCode: "PM-8f3a2c1d",
            Amount: 175_000m,
            GatewayTransactionId: "2609270000012345",
            // ⚠️ Adapter ZaloPay KHÔNG gán PaidAt (khác MoMo và VNPay đều lấy giờ cổng), nên bước
            // chốt rơi về giờ hệ thống. Để null ở đây nghĩa là "chỉ kỳ vọng khác null" — xem báo cáo
            // mục T2. Đây là ghim HÀNH VI ĐANG CÓ, không phải chốt rằng nó đúng.
            MocCongCuaCong: null,
            Payload: () => new PaymentCallbackInput { Body = VoZaloPay(ZaloPayData, ZaloPayMac) },
            PayloadSuaSoTien: () => new PaymentCallbackInput
            {
                Body = VoZaloPay(
                    ZaloPayData.Replace("\"amount\":175000", ZaloPayDataSuaSoTien), ZaloPayMac),
            },
            PayloadBamChuKy: () => new PaymentCallbackInput
            {
                Body = VoZaloPay(ZaloPayData, BamChuKy(ZaloPayMac)),
            }),

        _ => throw new ArgumentOutOfRangeException(nameof(ma), ma, "Cổng chưa có bộ vector"),
    };

    /// <summary>
    /// VNPay cũng dùng cho <c>BankCard</c> — cùng cổng bên dưới, cùng hình dạng callback.
    /// </summary>
    private static Cong CongVnPay() => new(
        MaPhuongThuc: PaymentProviderCodes.VnPay,
        PaymentCode: "PM-8f3a2c1d",
        Amount: 175_000m,
        GatewayTransactionId: "14000001",
        MocCongCuaCong: VnPayMocCong,
        Payload: () => VnPayPayload(),
        PayloadSuaSoTien: () => VnPayPayload(
            thay: new Dictionary<string, string> { ["vnp_Amount"] = "1" }),
        PayloadBamChuKy: () => VnPayPayload(
            thay: new Dictionary<string, string>
            {
                ["vnp_SecureHash"] = BamChuKy(VnPayChuKyThanhCong),
            }));

    /// <summary>
    /// Vector C của VNPay — giá trị ĐÃ GIẢI MÃ, đúng hình dạng endpoint đưa vào (ASP.NET bind
    /// Request.Query thành giá trị đã giải mã). <paramref name="thay"/> để dựng bản bị sửa.
    /// </summary>
    private static PaymentCallbackInput VnPayPayload(Dictionary<string, string>? thay = null)
    {
        var thamSo = new Dictionary<string, string>
        {
            ["vnp_Amount"] = "17500000",
            ["vnp_BankCode"] = "NCB",
            ["vnp_BankTranNo"] = "NCB20261010153200",
            ["vnp_CardType"] = "ATM",
            ["vnp_OrderInfo"] = "Thanh toan ve xe SmartBus",
            ["vnp_PayDate"] = "20261010153200",
            ["vnp_ResponseCode"] = "00",
            ["vnp_TmnCode"] = VnPayTmnCode,
            ["vnp_TransactionNo"] = "14000001",
            ["vnp_TransactionStatus"] = "00",
            ["vnp_TxnRef"] = "PM-8f3a2c1d",
            ["vnp_SecureHash"] = VnPayChuKyThanhCong,
        };

        foreach (var (key, value) in thay ?? [])
        {
            thamSo[key] = value;
        }

        return new PaymentCallbackInput { Query = thamSo };
    }

    /// <summary>
    /// Vỏ ba trường của ZaloPay đúng như cổng gửi: <c>data</c> là CHUỖI JSON đã escape,
    /// <c>type</c> là SỐ. Serialize bằng <see cref="JsonSerializer"/> để phần escape là thật.
    /// </summary>
    private static string VoZaloPay(string data, string mac)
        => JsonSerializer.Serialize(new { data, mac, type = 1 });

    /// <summary>
    /// Lật một ký tự hex cuối của chữ ký — vẫn đúng hình dạng, đúng độ dài, chỉ sai giá trị. Không
    /// cần vector mới và cũng không tự ký trong test.
    /// </summary>
    private static string BamChuKy(string chuKy)
        => chuKy[..^1] + (chuKy[^1] == '0' ? '1' : '0');

    // ---------------------------------------------------------------------------------------
    // Dựng app thật + gieo dữ liệu
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// App thật (<see cref="TestAppFactory"/>: CSDL InMemory, job nền noop) với ĐÚNG bộ khoá đã dùng
    /// để ký vector. Cố ý KHÔNG đặt <c>Endpoint</c>/<c>PaymentUrl</c> — nhóm 1 kiểm chính chỗ đó.
    ///
    /// Trả về lớp BỌC mà <c>WithWebHostBuilder</c> sinh ra (không phải <see cref="TestAppFactory"/>),
    /// nên bài này gieo dữ liệu qua DI chứ không qua <c>SeedAsync</c> — cùng một container nên vẫn
    /// đúng một CSDL InMemory.
    /// </summary>
    private static WebApplicationFactory<Program> NhaMayTest()
        => new TestAppFactory().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("MoMo:PartnerCode", MoMoPartnerCode);
            builder.UseSetting("MoMo:AccessKey", MoMoAccessKey);
            builder.UseSetting("MoMo:SecretKey", MoMoSecretKey);

            builder.UseSetting("VnPay:TmnCode", VnPayTmnCode);
            builder.UseSetting("VnPay:HashSecret", VnPayHashSecret);

            // AppId phải là số nguyên: client ZaloPay kiểm ngay lúc dựng.
            builder.UseSetting("ZaloPay:AppId", ZaloPayAppId);
            builder.UseSetting("ZaloPay:Key1", ZaloPayKey1);
            builder.UseSetting("ZaloPay:Key2", ZaloPayKey2);
        });

    private static async Task GieoGiaoDich(
        WebApplicationFactory<Program> factory,
        Cong cong,
        decimal? soTien = null,
        string? maPhuongThuc = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.Payments.Add(new Payment
        {
            PaymentCode = cong.PaymentCode,
            MethodCode = maPhuongThuc ?? cong.MaPhuongThuc,
            Amount = soTien ?? cong.Amount,
            Status = PaymentStatus.Pending,
            UserId = Guid.NewGuid(),
            TripId = Guid.NewGuid(),
            SeatNumbers = "A1;A2",
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
        });

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Một "lượt request": scope DI riêng, lấy cả resolver lẫn settlement từ DI thật. Dùng lại một
    /// scope cho nhiều lượt chốt là đi đường tắt qua change tracker của <c>AppDbContext</c> — lượt
    /// sau sẽ thấy entity đã tracked thay vì đọc lại từ CSDL như một request thật.
    /// </summary>
    private static async Task<T> TrongMotRequest<T>(
        WebApplicationFactory<Program> factory,
        Func<IPaymentGatewayResolver, IPaymentSettlementService, Task<T>> viec)
    {
        using var scope = factory.Services.CreateScope();

        return await viec(
            scope.ServiceProvider.GetRequiredService<IPaymentGatewayResolver>(),
            scope.ServiceProvider.GetRequiredService<IPaymentSettlementService>());
    }

    /// <summary>Đọc lại dòng giao dịch bằng context khác, không theo dõi thay đổi.</summary>
    private static async Task<Payment> DocLai(WebApplicationFactory<Program> factory, string paymentCode)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Payments.AsNoTracking().SingleAsync(p => p.PaymentCode == paymentCode);
    }

    /// <summary>Giao dịch phải còn nguyên: chưa có tiền vào thì không được lật trạng thái.</summary>
    private static async Task KiemDongConNguyenPending(
        WebApplicationFactory<Program> factory, Cong cong)
    {
        var sau = await DocLai(factory, cong.PaymentCode);

        Assert.Equal(PaymentStatus.Pending, sau.Status);
        Assert.Null(sau.PaidAt);
        Assert.Null(sau.GatewayTransactionId);
    }

    private static PaymentInitiationRequest YeuCauTaoDon() => new()
    {
        PaymentCode = "PM-8f3a2c1d",
        Amount = 175_000,
        OrderInfo = "Thanh toan ve xe SmartBus",
        ReturnUrl = "http://localhost:5173/payment-waiting",
        IpAddress = "203.113.131.1",
    };
}
