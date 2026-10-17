using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;
using RouteEntity = SmartBus.Api.Entities.Route;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test tích hợp bề mặt thanh toán — POST /payments, GET /payments/{paymentCode}, hai callback
/// MoMo/ZaloPay (US 6, nợ Sprint 3 — Trần Trung Hiếu). Hợp đồng đầy đủ ở mục "Thanh toán —
/// /payments" của docs/api-contract.md.
///
/// App thật (<see cref="TestAppFactory"/>: CSDL InMemory, job nền noop) cộng bộ khoá giả / sandbox
/// công khai ĐÃ NẰM SẴN trong repo (luật 2), qua <c>WithWebHostBuilder</c> + <c>UseSetting</c> — đúng
/// lối <see cref="PaymentSandboxCrossLayerTests"/>. Toàn bộ offline: HTTP ra cổng đi qua
/// <see cref="FakeGatewayHttpHandler"/>.
/// </summary>
public class PaymentApiTests
{
    // Bộ khoá giả / sandbox công khai — chép từ PaymentSandboxCrossLayerTests (đã nằm trong repo).
    private const string MoMoPartnerCode = "MOMO0T1D20220107";
    private const string MoMoAccessKey = "F8BBA842ECF85";
    private const string MoMoSecretKey = "K951B6PE1waDMi640xX08PD3vg6EkVlz";
    private const string VnPayTmnCode = "CGXZLS0Z";
    private const string VnPayHashSecret = "XNBCJFAKAZQSGTARRLGCHVZWCIOIGSHN";
    private const string ZaloPayAppId = "2553";
    // Bộ khoá GIẢ đã dùng để tính vector MAC ở PaymentSandboxCrossLayerTests — chính bộ này ký ra
    // ZaloPayMac bên dưới, đổi khoá là chữ ký lệch ngay (không phải khoá đối tác thật — luật 2).
    private const string ZaloPayKey1 = "test-key1-khong-phai-khoa-that";
    private const string ZaloPayKey2 = "test-key2-khong-phai-khoa-that";
    private const string TicketQrSigningKey = "khoa-ky-ma-qr-cho-test-dai-hon-32-ky-tu";

    /// <summary>Vector IPN MoMo — chép nguyên văn từ PaymentSandboxCrossLayerTests (chữ ký tính ngoài C#).</summary>
    private const string MoMoThanIpn = """
        {"partnerCode":"MOMO0T1D20220107","orderId":"63e61ad2318d66f22ecad8f7",
         "requestId":"84f36d78-a97e-4bd2-afb4-5f89cbaac15e","amount":175000,
         "orderInfo":"pay with MoMo","orderType":"momo_wallet","transId":2757953842,
         "resultCode":0,"message":"Success","payType":"webApp",
         "responseTime":1700000000000,"extraData":"",
         "signature":"7ad94c925b148569e2195b1b9cc0d802d071f4d222a10a50abcfdd88b2574e93"}
        """;

    /// <summary>
    /// Vector IPN MoMo BÁO THẤT BẠI (resultCode = 1006) — cùng bộ dữ liệu nhưng chữ ký tính lại
    /// bằng Python (sửa body rồi giữ chữ ký cũ là ca "chữ ký sai" chứ không phải ca thất bại).
    /// </summary>
    private const string MoMoIpnThatBai = """
        {"partnerCode":"MOMO0T1D20220107","orderId":"63e61ad2318d66f22ecad8f7",
         "requestId":"84f36d78-a97e-4bd2-afb4-5f89cbaac15e","amount":175000,
         "orderInfo":"pay with MoMo","orderType":"momo_wallet","transId":2757953842,
         "resultCode":1006,"message":"Giao dich bi tu choi","payType":"webApp",
         "responseTime":1700000000000,"extraData":"",
         "signature":"5cd64f97605744c13b855738a25cd02fe00f0b3ba342359a6da7292d54bb298c"}
        """;

    /// <summary>Vector callback ZaloPay — chép từ PaymentSandboxCrossLayerTests.</summary>
    private const string ZaloPayData =
        "{\"app_id\":2553,\"app_trans_id\":\"261010_PM-8f3a2c1d\",\"app_user\":\"smartbus\","
        + "\"amount\":175000,\"zp_trans_id\":2609270000012345,\"server_time\":1791621120000,"
        + "\"channel\":38}";

    private const string ZaloPayMac = "8a6f812c2e8c56212a8402f4ea7ab9626d3be3932908fe69f744113c6377f80c";

    /// <summary>Giá mỗi ghế của test mặc định — 2 ghế = 48.000đ, trên sàn 10.000đ của MoMo.</summary>
    private const decimal GiaVe = 24_000m;

    // -------------------------------------------------------------- POST /payments

    [Fact]
    public async Task Tao_giao_dich_MoMo_thanh_cong_tra_payUrl_va_luu_ban_ghi_Pending()
    {
        var handler = new FakeGatewayHttpHandler(_ => MoMoCreateOk("https://test-payment.momo.vn/v2/gateway/pay?t=abc"));
        using var factory = NhaMayThanhToan(handler);
        var client = await ClientAsPassengerAsync(factory);
        var (trip, _) = await SeedDuLieuBanVeAsync(factory);

        var response = await client.PostAsJsonAsync("/api/payments", new
        {
            methodCode = "MoMo",
            tripId = trip.Id,
            seatNumbers = new[] { "A1", "A2" },
            total = 48_000m,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.NotNull(root.GetProperty("paymentCode").GetString());
        Assert.StartsWith("PM-", root.GetProperty("paymentCode").GetString());
        Assert.Equal(48_000m, root.GetProperty("originalAmount").GetDecimal());
        Assert.Equal(0m, root.GetProperty("discountAmount").GetDecimal());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("voucherCode").ValueKind);
        Assert.Equal(48_000m, root.GetProperty("amount").GetDecimal());
        Assert.Equal("MoMo", root.GetProperty("method").GetString());
        Assert.Equal("Pending", root.GetProperty("status").GetString());
        Assert.Equal("https://test-payment.momo.vn/v2/gateway/pay?t=abc", root.GetProperty("payUrl").GetString());

        // Cổng nhận đúng orderId = paymentCode và đúng số tiền phải trả.
        var sent = JsonDocument.Parse(await handler.ReadLastBodyAsync()).RootElement;
        Assert.Equal(root.GetProperty("paymentCode").GetString(), sent.GetProperty("orderId").GetString());
        Assert.Equal(48_000m, sent.GetProperty("amount").GetDecimal());

        // Bản ghi Pending trong CSDL — trạng thái chỉ lật qua callback / job đối soát.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var payment = await db.Payments.SingleAsync(p => p.PaymentCode == root.GetProperty("paymentCode").GetString());
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal("A1;A2", payment.SeatNumbers);
    }

    [Fact]
    public async Task Voucher_hop_le_tru_gia_va_gui_dung_so_tien_phai_tra_len_cong()
    {
        var handler = new FakeGatewayHttpHandler(_ => MoMoCreateOk("https://test-payment.momo.vn/v2/gateway/pay?t=voucher"));
        using var factory = NhaMayThanhToan(handler);
        var client = await ClientAsPassengerAsync(factory);
        var (trip, _) = await SeedDuLieuBanVeAsync(factory);
        await SeedVoucherAsync(factory, "GIAM10", trip.RouteId, percent: 10);

        var response = await client.PostAsJsonAsync("/api/payments", new
        {
            methodCode = "MoMo",
            tripId = trip.Id,
            seatNumbers = new[] { "A1", "A2" },
            total = 48_000m,
            voucherCode = "GIAM10",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal(4_800m, root.GetProperty("discountAmount").GetDecimal());
        Assert.Equal("GIAM10", root.GetProperty("voucherCode").GetString());
        Assert.Equal(43_200m, root.GetProperty("amount").GetDecimal());

        // Con số gửi lên cổng là số ĐÃ TRỪ — khách trả 43.200, không phải 48.000.
        var sent = JsonDocument.Parse(await handler.ReadLastBodyAsync()).RootElement;
        Assert.Equal(43_200m, sent.GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Voucher_khong_dung_duoc_tra_409_theo_truong_voucherCode()
    {
        var handler = new FakeGatewayHttpHandler(_ => MoMoCreateOk("https://test-payment.momo.vn/v2/gateway/pay?t=abc"));
        using var factory = NhaMayThanhToan(handler);
        var client = await ClientAsPassengerAsync(factory);
        var (trip, _) = await SeedDuLieuBanVeAsync(factory);

        var response = await client.PostAsJsonAsync("/api/payments", new
        {
            methodCode = "MoMo",
            tripId = trip.Id,
            seatNumbers = new[] { "A1", "A2" },
            total = 48_000m,
            voucherCode = "MAKHONGTONTAI",
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("voucherCode", out _));

        // Không có cuộc gọi cổng nào — chặn trước khi chạm tiền.
        Assert.Equal(0, handler.SoLanGoi);
    }

    [Fact]
    public async Task Voucher_keo_so_tien_xuong_duoi_san_MoMo_tra_409_theo_truong_voucherCode()
    {
        // Sàn MoMo 10.000đ: mã giảm làm số tiền còn 500đ — cổng sẽ từ chối với lỗi khó hiểu, nên
        // chặn trước bằng 409 kèm lời rõ (hợp đồng đã chốt).
        var handler = new FakeGatewayHttpHandler(_ => MoMoCreateOk("https://test-payment.momo.vn/v2/gateway/pay?t=abc"));
        using var factory = NhaMayThanhToan(handler);
        var client = await ClientAsPassengerAsync(factory);
        var (trip, _) = await SeedDuLieuBanVeAsync(factory, pricePerSeat: 10_000m);
        await SeedVoucherAsync(factory, "GIAMSAU", trip.RouteId, fixedAmount: 9_500m);

        var response = await client.PostAsJsonAsync("/api/payments", new
        {
            methodCode = "MoMo",
            tripId = trip.Id,
            seatNumbers = new[] { "A1" },
            total = 10_000m,
            voucherCode = "GIAMSAU",
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("voucherCode", out _));
        Assert.Equal(0, handler.SoLanGoi);
    }

    [Fact]
    public async Task Ma_phuong_thuc_chua_mo_tra_400_theo_truong_methodCode()
    {
        var handler = new FakeGatewayHttpHandler(_ => MoMoCreateOk("https://test-payment.momo.vn/v2/gateway/pay?t=abc"));
        using var factory = NhaMayThanhToan(handler);
        var client = await ClientAsPassengerAsync(factory);
        var (trip, _) = await SeedDuLieuBanVeAsync(factory);

        var response = await client.PostAsJsonAsync("/api/payments", new
        {
            methodCode = "Bitcoin",
            tripId = trip.Id,
            seatNumbers = new[] { "A1" },
            total = 24_000m,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("methodCode", out _));
    }

    [Fact]
    public async Task Thieu_truong_bat_buoc_tra_400_theo_truong()
    {
        using var factory = NhaMayThanhToan(new FakeGatewayHttpHandler());
        var client = await ClientAsPassengerAsync(factory);

        var response = await client.PostAsJsonAsync("/api/payments", new { methodCode = "MoMo" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = body.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("tripId", out _));
        Assert.True(errors.TryGetProperty("seatNumbers", out _));
        Assert.True(errors.TryGetProperty("total", out _));
    }

    [Fact]
    public async Task Chuyen_khong_ton_tai_tra_404()
    {
        using var factory = NhaMayThanhToan(new FakeGatewayHttpHandler());
        var client = await ClientAsPassengerAsync(factory);

        var response = await client.PostAsJsonAsync("/api/payments", new
        {
            methodCode = "MoMo",
            tripId = Guid.NewGuid(),
            seatNumbers = new[] { "A1" },
            total = 24_000m,
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Total_lenh_gia_ghe_tra_400_theo_truong_total()
    {
        using var factory = NhaMayThanhToan(new FakeGatewayHttpHandler());
        var client = await ClientAsPassengerAsync(factory);
        var (trip, _) = await SeedDuLieuBanVeAsync(factory);

        var response = await client.PostAsJsonAsync("/api/payments", new
        {
            methodCode = "MoMo",
            tripId = trip.Id,
            seatNumbers = new[] { "A1", "A2" },
            total = 1m,   // giá đúng là 48.000 — FE gửi được gì thì người ngoài cũng gửi được nấy
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("total", out _));
    }

    [Fact]
    public async Task Ghe_khong_thuoc_xe_cua_chuyen_tra_400_theo_truong_seatNumbers()
    {
        using var factory = NhaMayThanhToan(new FakeGatewayHttpHandler());
        var client = await ClientAsPassengerAsync(factory);
        var (trip, _) = await SeedDuLieuBanVeAsync(factory);

        var response = await client.PostAsJsonAsync("/api/payments", new
        {
            methodCode = "MoMo",
            tripId = trip.Id,
            seatNumbers = new[] { "Z9" },   // xe chỉ có A1/A2
            total = 24_000m,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("seatNumbers", out _));
    }

    [Fact]
    public async Task Cong_tu_choi_tao_giao_dich_tra_502_kem_loi_cong()
    {
        var handler = new FakeGatewayHttpHandler(_ => JsonResponse(new
        {
            resultCode = 1006,
            message = "Giao dịch bị từ chối bởi cổng",
        }));
        using var factory = NhaMayThanhToan(handler);
        var client = await ClientAsPassengerAsync(factory);
        var (trip, _) = await SeedDuLieuBanVeAsync(factory);

        var response = await client.PostAsJsonAsync("/api/payments", new
        {
            methodCode = "MoMo",
            tripId = trip.Id,
            seatNumbers = new[] { "A1", "A2" },
            total = 48_000m,
        });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("từ chối", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task VNPay_tao_giao_dich_khong_goi_mang_va_tra_url_da_ky()
    {
        using var factory = NhaMayThanhToan(new FakeGatewayHttpHandler());
        var client = await ClientAsPassengerAsync(factory);
        var (trip, _) = await SeedDuLieuBanVeAsync(factory);

        var response = await client.PostAsJsonAsync("/api/payments", new
        {
            methodCode = "VNPay",
            tripId = trip.Id,
            seatNumbers = new[] { "A1", "A2" },
            total = 48_000m,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var payUrl = body.RootElement.GetProperty("payUrl").GetString();
        Assert.StartsWith("https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?", payUrl);
        Assert.DoesNotContain("vnp_BankCode", payUrl);   // VNPay trần: khách tự chọn kênh trên trang cổng
    }

    [Fact]
    public async Task BankCard_ghim_kenh_the_ngan_hang_trong_url()
    {
        using var factory = NhaMayThanhToan(new FakeGatewayHttpHandler());
        var client = await ClientAsPassengerAsync(factory);
        var (trip, _) = await SeedDuLieuBanVeAsync(factory);

        var response = await client.PostAsJsonAsync("/api/payments", new
        {
            methodCode = "BankCard",
            tripId = trip.Id,
            seatNumbers = new[] { "A1" },
            total = 24_000m,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("vnp_BankCode=VNBANK", body.RootElement.GetProperty("payUrl").GetString());
    }

    [Fact]
    public async Task Khong_gui_token_thi_hai_endpoint_cua_khach_tra_401()
    {
        using var factory = NhaMayThanhToan(new FakeGatewayHttpHandler());
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/payments", new { methodCode = "MoMo" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/payments/{Guid.NewGuid()}")).StatusCode);
    }

    // -------------------------------------------------------------- GET /payments/{paymentCode}

    [Fact]
    public async Task Tra_trang_thai_chi_cho_chu_giao_dich()
    {
        using var factory = NhaMayThanhToan(new FakeGatewayHttpHandler());
        var chu = await SeedUserAsync(factory);
        var nguoiKhac = await SeedUserAsync(factory);
        var client = ClientWith(factory, TaoToken(factory, chu));

        var (trip, _) = await SeedDuLieuBanVeAsync(factory);
        var payment = await SeedPaymentAsync(factory, chu.Id, trip.Id, "MoMo");

        var response = await client.GetAsync($"/api/payments/{payment.PaymentCode}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(payment.PaymentCode, body.RootElement.GetProperty("paymentCode").GetString());
        Assert.Equal(48_000m, body.RootElement.GetProperty("amount").GetDecimal());
        Assert.Equal("MoMo", body.RootElement.GetProperty("method").GetString());
        Assert.Equal("Pending", body.RootElement.GetProperty("status").GetString());

        // Của người khác và không tồn tại cùng một câu 404 — không để lộ giao dịch nào đang tồn tại.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/payments/PM-khong-ton-tai")).StatusCode);

        var nguoiKhacResponse = await ClientWith(factory, TaoToken(factory, nguoiKhac)).GetAsync($"/api/payments/{payment.PaymentCode}");
        Assert.Equal(HttpStatusCode.NotFound, nguoiKhacResponse.StatusCode);
    }

    // -------------------------------------------------------------- POST /payments/momo/callback

    [Fact]
    public async Task Callback_MoMo_thanh_cong_chot_Success_va_phat_hanh_ve()
    {
        using var factory = NhaMayThanhToan(new FakeGatewayHttpHandler());
        var chu = await SeedUserAsync(factory);
        var (trip, seats) = await SeedDuLieuBanVeAsync(factory);
        var payment = await SeedPaymentAsync(factory, chu.Id, trip.Id, "MoMo", amount: 175_000m);   // khớp vector IPN
        await SeedHoldAsync(factory, chu.Id, trip.Id, seats[0].Id, seats[1].Id);

        var client = factory.CreateClient();   // callback CÔNG KHAI — không token
        var response = await client.PostAsync(
            "/api/payments/momo/callback",
            new StringContent(MoMoThanIpn.ReplaceLineEndings(" "), Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var sau = await db.Payments.AsNoTracking().SingleAsync(p => p.Id == payment.Id);
        Assert.Equal(PaymentStatus.Success, sau.Status);
        Assert.Equal("2757953842", sau.GatewayTransactionId);
        Assert.NotNull(sau.PaidAt);

        // Phát hành vé: 2 vé Paid, Payment.TicketId trỏ vé đầu, giữ chỗ chốt Confirmed kèm nhật ký.
        var ve = await db.Tickets.AsNoTracking().Where(t => t.UserId == chu.Id).ToListAsync();
        Assert.Equal(2, ve.Count);
        Assert.All(ve, t => Assert.Equal(TicketStatus.Paid, t.Status));
        Assert.All(ve, t => Assert.StartsWith("SBT1:", t.Code));
        Assert.Contains(ve, t => t.Id == sau.TicketId);

        var holds = await db.SeatHolds.AsNoTracking().Where(h => h.UserId == chu.Id).ToListAsync();
        Assert.All(holds, h => Assert.Equal(SeatHoldStatus.Confirmed, h.Status));
        Assert.Equal(2, await db.SeatHoldLogs.CountAsync(l => l.UserId == chu.Id && l.Action == SeatHoldLogAction.Confirmed));
    }

    [Fact]
    public async Task Callback_MoMo_gui_lai_khong_chot_lai_va_khong_phat_hanh_them_ve()
    {
        using var factory = NhaMayThanhToan(new FakeGatewayHttpHandler());
        var chu = await SeedUserAsync(factory);
        var (trip, seats) = await SeedDuLieuBanVeAsync(factory);
        var payment = await SeedPaymentAsync(factory, chu.Id, trip.Id, "MoMo", amount: 175_000m);   // khớp vector IPN
        await SeedHoldAsync(factory, chu.Id, trip.Id, seats[0].Id, seats[1].Id);

        var client = factory.CreateClient();
        var content = new StringContent(MoMoThanIpn.ReplaceLineEndings(" "), Encoding.UTF8, "application/json");

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/payments/momo/callback", content)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/payments/momo/callback", content)).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, await db.Tickets.CountAsync(t => t.UserId == chu.Id));   // không có vé thứ ba
    }

    [Fact]
    public async Task Callback_MoMo_chu_ky_gia_tra_400_khong_cham_CSDL()
    {
        using var factory = NhaMayThanhToan(new FakeGatewayHttpHandler());
        var chu = await SeedUserAsync(factory);
        var (trip, _) = await SeedDuLieuBanVeAsync(factory);
        var payment = await SeedPaymentAsync(factory, chu.Id, trip.Id, "MoMo");

        // Sửa một ký tự của chữ ký — hình dạng vẫn đúng, chữ ký không còn khớp.
        var giaMao = MoMoThanIpn.ReplaceLineEndings(" ")
            .Replace("7ad94c925b148569e2195b1b9cc0d802d071f4d222a10a50abcfdd88b2574e93", "7ad94c925b148569e2195b1b9cc0d802d071f4d222a10a50abcfdd88b2574e92");

        var response = await factory.CreateClient().PostAsync(
            "/api/payments/momo/callback",
            new StringContent(giaMao, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(PaymentStatus.Pending, (await db.Payments.AsNoTracking().SingleAsync(p => p.Id == payment.Id)).Status);
    }

    [Fact]
    public async Task Callback_MoMo_lenh_so_tien_tra_404()
    {
        using var factory = NhaMayThanhToan(new FakeGatewayHttpHandler());
        var chu = await SeedUserAsync(factory);
        var (trip, _) = await SeedDuLieuBanVeAsync(factory);
        // Bản ghi 48.000 còn IPN báo 175.000 — lệch tiền là callback không thuộc về bản ghi này.
        await SeedPaymentAsync(factory, chu.Id, trip.Id, "MoMo");

        var response = await factory.CreateClient().PostAsync(
            "/api/payments/momo/callback",
            new StringContent(MoMoThanIpn.ReplaceLineEndings(" "), Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Callback_MoMo_bao_that_bai_chot_Failed_kem_loi_cong()
    {
        using var factory = NhaMayThanhToan(new FakeGatewayHttpHandler());
        var chu = await SeedUserAsync(factory);
        var (trip, _) = await SeedDuLieuBanVeAsync(factory);
        var payment = await SeedPaymentAsync(factory, chu.Id, trip.Id, "MoMo", amount: 175_000m);

        var response = await factory.CreateClient().PostAsync(
            "/api/payments/momo/callback",
            new StringContent(MoMoIpnThatBai.ReplaceLineEndings(" "), Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sau = await db.Payments.AsNoTracking().SingleAsync(p => p.Id == payment.Id);
        Assert.Equal(PaymentStatus.Failed, sau.Status);
        Assert.Contains("tu choi", sau.Message);
        Assert.Equal(0, await db.Tickets.CountAsync(t => t.UserId == chu.Id));   // thất bại không phát hành vé
    }

    // -------------------------------------------------------------- POST /payments/zalopay/callback

    [Fact]
    public async Task Callback_ZaloPay_thanh_cong_tra_cau_merchant_va_phat_hanh_ve()
    {
        using var factory = NhaMayThanhToan(new FakeGatewayHttpHandler());
        var chu = await SeedUserAsync(factory);
        var (trip, seats) = await SeedDuLieuBanVeAsync(factory);
        var payment = await SeedPaymentAsync(factory, chu.Id, trip.Id, "ZaloPay", amount: 175_000m);
        await SeedHoldAsync(factory, chu.Id, trip.Id, seats[0].Id, seats[1].Id);

        // data là một CHUỖI JSON trong vỏ callback — phải Serialize để thoát dấu ngoặc kép bên trong.
        var body = $"{{\"data\":{JsonSerializer.Serialize(ZaloPayData)},\"mac\":\"{ZaloPayMac}\",\"type\":1}}";
        var response = await factory.CreateClient().PostAsync(
            "/api/payments/zalopay/callback",
            new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var cauTraLoi = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1, cauTraLoi.RootElement.GetProperty("return_code").GetInt32());
        Assert.Equal("Success", cauTraLoi.RootElement.GetProperty("return_message").GetString());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sau = await db.Payments.AsNoTracking().SingleAsync(p => p.Id == payment.Id);
        Assert.Equal(PaymentStatus.Success, sau.Status);
        Assert.Equal(2, await db.Tickets.CountAsync(t => t.UserId == chu.Id));
    }

    [Fact]
    public async Task Callback_ZaloPay_chu_ky_gia_tra_400()
    {
        using var factory = NhaMayThanhToan(new FakeGatewayHttpHandler());
        var chu = await SeedUserAsync(factory);
        var (trip, _) = await SeedDuLieuBanVeAsync(factory);
        await SeedPaymentAsync(factory, chu.Id, trip.Id, "ZaloPay", amount: 175_000m);

        var body = $"{{\"data\":\"{ZaloPayData}\",\"mac\":\"{ZaloPayMac[..^1]}0\",\"type\":1}}";
        var response = await factory.CreateClient().PostAsync(
            "/api/payments/zalopay/callback",
            new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // -------------------------------------------------------------- Chỗ nối DI

    [Fact]
    public void Job_doi_soat_duoc_dang_ky_dung_mot_lan()
    {
        using var factory = new TestAppFactory();

        var job = factory.Services
            .GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .OfType<PaymentReconciliationBackgroundService>();

        Assert.Single(job);
    }

    // -------------------------------------------------------------- Hạ tầng test

    /// <summary>
    /// App thật (CSDL InMemory, job noop) + bộ khoá giả/sandbox công khai + HTTP giả cho MoMo/ZaloPay —
    /// cùng lối NhaMayTest của PaymentSandboxCrossLayerTests. Trả về lớp BỌC của WithWebHostBuilder
    /// nên gieo dữ liệu qua DI, không qua SeedAsync.
    /// </summary>
    private static WebApplicationFactory<Program> NhaMayThanhToan(FakeGatewayHttpHandler handler)
        => new TestAppFactory().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("MoMo:PartnerCode", MoMoPartnerCode);
            builder.UseSetting("MoMo:AccessKey", MoMoAccessKey);
            builder.UseSetting("MoMo:SecretKey", MoMoSecretKey);

            builder.UseSetting("VnPay:TmnCode", VnPayTmnCode);
            builder.UseSetting("VnPay:HashSecret", VnPayHashSecret);

            builder.UseSetting("ZaloPay:AppId", ZaloPayAppId);
            builder.UseSetting("ZaloPay:Key1", ZaloPayKey1);
            builder.UseSetting("ZaloPay:Key2", ZaloPayKey2);

            builder.UseSetting("TicketQr:SigningKey", TicketQrSigningKey);

            // Toàn bộ offline: HTTP ra MoMo/ZaloPay đi qua handler giả — không test nào chạm mạng.
            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient<MoMoGatewayService>()
                    .ConfigurePrimaryHttpMessageHandler(() => handler);
                services.AddHttpClient<ZaloPayGatewayService>()
                    .ConfigurePrimaryHttpMessageHandler(() => handler);
            });
        });

    private static async Task<HttpClient> ClientAsPassengerAsync(WebApplicationFactory<Program> factory)
    {
        var passenger = await SeedUserAsync(factory);
        return ClientWith(factory, TaoToken(factory, passenger));
    }

    private static HttpClient ClientWith(WebApplicationFactory<Program> factory, string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    /// <summary>
    /// Sinh access token bằng chính ITokenService của app (lớp bọc của WithWebHostBuilder không có
    /// CreateTokenFor của TestAppFactory) — tránh test lệch khỏi code thật.
    /// </summary>
    private static string TaoToken(WebApplicationFactory<Program> factory, UserEntity user)
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ITokenService>().CreateAccessToken(user).Token;
    }

    private static async Task<UserEntity> SeedUserAsync(WebApplicationFactory<Program> factory)
    {
        var role = new Role { Id = RoleIds.Passenger, Code = RoleCodes.Passenger, Name = RoleCodes.Passenger };
        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "09" + Random.Shared.Next(10_000_000, 99_999_999),
            FullName = "Hành Khách Test",
            PasswordHash = PasswordService.HashPassword("matkhau123"),
            IsActive = true,
            RoleId = role.Id,
            UserRoles = [new UserRole { RoleId = role.Id }],
        };

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (!db.Roles.Any(r => r.Id == role.Id || r.Code == role.Code))
        {
            db.Roles.Add(role);
        }

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return user;
    }

    /// <summary>Seed đủ dây chuyền bán vé: tuyến + giá + xe + ghế A1/A2 + chuyến.</summary>
    private static async Task<(Trip Trip, List<Seat> Seats)> SeedDuLieuBanVeAsync(
        WebApplicationFactory<Program> factory,
        decimal pricePerSeat = GiaVe)
    {
        var route = new RouteEntity
        {
            Code = "08",
            Name = "Tuyến 08",
            Origin = "Bến Thành",
            Destination = "Chợ Lớn",
        };
        var bus = new Bus
        {
            LicensePlate = "51B-12345",
            BusType = "Xe 45 chỗ",
            Capacity = 45,
        };
        var trip = new Trip
        {
            RouteId = route.Id,
            BusId = bus.Id,
            DepartureTime = DateTime.UtcNow.AddHours(2),
        };
        var seats = new List<Seat>
        {
            new() { BusId = bus.Id, SeatNumber = "A1", Floor = 1, RowIndex = 1, ColumnIndex = 1, SeatType = SeatType.Standard },
            new() { BusId = bus.Id, SeatNumber = "A2", Floor = 1, RowIndex = 1, ColumnIndex = 2, SeatType = SeatType.Standard },
        };

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.Routes.Add(route);
        db.Buses.Add(bus);
        db.Trips.Add(trip);
        db.Seats.AddRange(seats);
        db.Fares.Add(new Fare { RouteId = route.Id, PassengerType = PassengerType.Standard, Price = pricePerSeat });
        await db.SaveChangesAsync();

        return (trip, seats);
    }

    private static async Task<Payment> SeedPaymentAsync(
        WebApplicationFactory<Program> factory,
        Guid userId,
        Guid tripId,
        string methodCode,
        decimal amount = 48_000m)
    {
        var payment = new Payment
        {
            PaymentCode = methodCode == "ZaloPay" ? "PM-8f3a2c1d" : "63e61ad2318d66f22ecad8f7",
            MethodCode = methodCode,
            Amount = amount,
            Status = PaymentStatus.Pending,
            UserId = userId,
            TripId = tripId,
            SeatNumbers = "A1;A2",
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
        };

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        return payment;
    }

    private static async Task SeedHoldAsync(
        WebApplicationFactory<Program> factory,
        Guid userId,
        Guid tripId,
        params Guid[] seatIds)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        foreach (var seatId in seatIds)
        {
            db.SeatHolds.Add(new SeatHold
            {
                TripId = tripId,
                SeatId = seatId,
                UserId = userId,
                SessionCode = $"SS-{Guid.NewGuid():N}",
                Status = SeatHoldStatus.Holding,
                ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedVoucherAsync(
        WebApplicationFactory<Program> factory,
        string code,
        Guid routeId,
        int? percent = null,
        decimal? fixedAmount = null)
    {
        var voucher = new Voucher
        {
            Code = code,
            Name = $"Voucher {code}",
            DiscountType = percent is not null ? VoucherDiscountType.Percent : VoucherDiscountType.FixedAmount,
            DiscountValue = percent ?? fixedAmount ?? 0m,
            MinOrderValue = 0m,
            RouteId = routeId,
            Quantity = 100,
            UsedCount = 0,
            ValidFrom = DateTime.UtcNow.AddDays(-1),
            ValidUntil = DateTime.UtcNow.AddDays(30),
            Status = VoucherStatus.Active,
        };

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Vouchers.Add(voucher);
        await db.SaveChangesAsync();
    }

    private static HttpResponseMessage MoMoCreateOk(string payUrl) => JsonResponse(new
    {
        resultCode = 0,
        message = "Success.",
        payUrl,
    });

    private static HttpResponseMessage JsonResponse(object body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
    };

    /// <summary>
    /// HTTP giả cho cả MoMo lẫn ZaloPay — mỗi test gài responder riêng; ghi lại request cuối để
    /// assert thân gửi cổng.
    /// </summary>
    private sealed class FakeGatewayHttpHandler(Func<HttpRequestMessage, HttpResponseMessage>? responder = null)
        : HttpMessageHandler
    {
        public int SoLanGoi { get; private set; }

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SoLanGoi++;
            LastRequest = request;

            return Task.FromResult(responder?.Invoke(request)
                ?? throw new InvalidOperationException($"Chưa gài responder cho request {request.RequestUri}"));
        }

        public async Task<string> ReadLastBodyAsync()
        {
            Assert.NotNull(LastRequest);
            return await LastRequest!.Content!.ReadAsStringAsync();
        }
    }
}
