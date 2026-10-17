using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Payments;
using SmartBus.Api.Dtos.Refunds;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test cho <see cref="RefundGatewayCaller"/> — cài đặt thật của <see cref="IRefundGatewayCaller"/>
/// (US 8, task *"API gọi cổng thanh toán để hoàn tiền"* dòng 29 — Trần Trung Hiếu).
///
/// Dựng DI THẬT chứ không dựng tay: ServiceCollection có đủ ba client cổng + <c>HttpClient</c> trỏ vào
/// <see cref="FakeHandler"/> — lời hứa "tra đúng client theo mã phương thức" chỉ chứng minh được khi
/// chuỗi resolve đi qua đúng bảng đăng ký. Toàn bộ offline, không cuộc gọi mạng nào.
///
/// ⚠️ InMemory KHÔNG cưỡng chế khoá ngoại — ca "giao dịch gốc mất" dựng được bằng cách seed khoản
/// hoàn trỏ vào PaymentId không tồn tại (navigation mồ côi trả null), không phải bằng cách chờ
/// PostgreSQL từ chối.
/// </summary>
public class RefundGatewayCallerTests
{
    // Bộ khoá giả / khoá sandbox công khai — ĐÃ NẰM SẴN trong repo (luật 2, không thêm bí mật mới).
    private const string MoMoPartnerCode = "MOMO0T1D20220107";
    private const string MoMoAccessKey = "F8BBA842ECF85";
    private const string MoMoSecretKey = "K951B6PE1waDMi640xX08PD3vg6EkVlz";
    private const string VnPayTmnCode = "CGXZLS0Z";
    private const string VnPayHashSecret = "XNBCJFAKAZQSGTARRLGCHVZWCIOIGSHN";
    private const string ZaloPayAppId = "2553";
    private const string ZaloPayKey1 = "PcY4iZIKFCIdgZvA6ueMcMHHUbRLYjPL";
    private const string ZaloPayKey2 = "kLtgPl8HHhfvMuDHPwKfgfsY4Ydm9eIz";

    /// <summary>Mốc "bây giờ" cố định của cả file.</summary>
    private static readonly DateTime BayGio = new(2026, 10, 14, 8, 0, 0, DateTimeKind.Utc);

    /// <summary>Tên CSDL InMemory riêng cho từng test — xUnit dựng lớp test mới cho mỗi test.</summary>
    private readonly string _tenCsdl = $"goi-cong-hoan-tien-{Guid.NewGuid():N}";

    // ------------------------------------------------------------------
    // Tra đúng client theo MethodCode
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(PaymentProviderCodes.MoMo, "/v2/gateway/api/refund", "999888777")]
    [InlineData(PaymentProviderCodes.VnPay, "merchant_webapi/api/transaction", "999888")]
    [InlineData(PaymentProviderCodes.ZaloPay, "/v2/refund", "1905861234567890")]
    [InlineData(PaymentProviderCodes.BankCard, "merchant_webapi/api/transaction", "999888")] // kênh thẻ đi qua API hoàn của VNPay
    public async Task Tra_dung_client_theo_MethodCode_cua_giao_dich_goc(
        string methodCode,
        string duongDanMongDoi,
        string maGiaoDichHoanMongDoi)
    {
        var handler = new FakeHandler(TraLoiTheoDuongDan);
        using var db = TaoDb();
        var khoan = await SeedRefundAsync(db, methodCode);
        using var dichVu = TaoDichVu(handler, db);
        var caller = dichVu.GetRequiredService<IRefundGatewayCaller>();

        var ketQua = await caller.CallAsync(new RefundGatewayCallRequest
        {
            RefundId = khoan.Id,
            RefundCode = khoan.RefundCode,
            MethodCode = methodCode,
            Amount = khoan.Amount,
        });

        Assert.Equal(RefundLogResult.Succeeded, ketQua.Result);
        Assert.Contains(duongDanMongDoi, handler.LastRequest?.RequestUri?.AbsolutePath);
        Assert.Equal(maGiaoDichHoanMongDoi, ketQua.GatewayTransactionId);
    }

    [Fact]
    public async Task Phuong_thuc_khong_ho_tro_tra_Failed_khong_goi_HTTP()
    {
        var handler = new FakeHandler(TraLoiTheoDuongDan);
        using var db = TaoDb();
        var khoan = await SeedRefundAsync(db, PaymentProviderCodes.MoMo);
        using var dichVu = TaoDichVu(handler, db);
        var caller = dichVu.GetRequiredService<IRefundGatewayCaller>();

        var ketQua = await caller.CallAsync(new RefundGatewayCallRequest
        {
            RefundId = khoan.Id,
            RefundCode = khoan.RefundCode,
            MethodCode = "Bitcoin",
            Amount = khoan.Amount,
        });

        // Phương thức lạ là câu trả lời chắc chắn (thử lại cũng không đổi) → Failed, và không có cuộc
        // gọi cổng nào.
        Assert.Equal(RefundLogResult.Failed, ketQua.Result);
        Assert.Contains("Bitcoin", ketQua.Message);
        Assert.Equal(0, handler.SoLanGoi);
    }

    // ------------------------------------------------------------------
    // Chặn các ca không gọi cổng được
    // ------------------------------------------------------------------

    [Fact]
    public async Task Khoan_hoan_khong_ton_tai_tra_Failed_khong_goi_HTTP()
    {
        var handler = new FakeHandler(TraLoiTheoDuongDan);
        using var db = TaoDb();
        using var dichVu = TaoDichVu(handler, db);
        var caller = dichVu.GetRequiredService<IRefundGatewayCaller>();

        var ketQua = await caller.CallAsync(new RefundGatewayCallRequest
        {
            RefundId = Guid.NewGuid(),
            RefundCode = "RF-khong-ton-tai",
            MethodCode = PaymentProviderCodes.MoMo,
            Amount = 175_000m,
        });

        Assert.Equal(RefundLogResult.Failed, ketQua.Result);
        Assert.Equal(0, handler.SoLanGoi);
    }

    [Fact]
    public async Task Giao_dich_goc_mat_tra_Failed_khong_goi_HTTP()
    {
        // Navigation mồ côi: khoản hoàn trỏ PaymentId không tồn tại — InMemory trả Payment null
        // (PostgreSQL chặn từ trước bằng FK Restrict, ca này chỉ có nghĩa với dữ liệu hỏng).
        var handler = new FakeHandler(TraLoiTheoDuongDan);
        using var db = TaoDb();
        var khoan = new Refund
        {
            RefundCode = "RF-mo-coi",
            Source = RefundSource.PaymentFailure,
            PaymentId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Amount = 175_000m,
            CreatedAt = BayGio.AddHours(-1),
        };
        db.Refunds.Add(khoan);
        await db.SaveChangesAsync();

        using var dichVu = TaoDichVu(handler, db);
        var caller = dichVu.GetRequiredService<IRefundGatewayCaller>();

        var ketQua = await caller.CallAsync(new RefundGatewayCallRequest
        {
            RefundId = khoan.Id,   // khoản có thật, nhưng Payment trỏ vào dòng không tồn tại
            RefundCode = "RF-mo-coi",
            MethodCode = PaymentProviderCodes.MoMo,
            Amount = 175_000m,
        });

        Assert.Equal(RefundLogResult.Failed, ketQua.Result);
        Assert.Equal(0, handler.SoLanGoi);
    }

    [Fact]
    public async Task So_tien_le_tra_Failed_khong_goi_HTTP()
    {
        // Cả ba cổng chỉ nhận VND nguyên — phần lẻ nghĩa là dữ liệu khoản hoàn sai, thử lại cũng
        // không đổi → Failed.
        var handler = new FakeHandler(TraLoiTheoDuongDan);
        using var db = TaoDb();
        var khoan = await SeedRefundAsync(db, PaymentProviderCodes.MoMo);
        using var dichVu = TaoDichVu(handler, db);
        var caller = dichVu.GetRequiredService<IRefundGatewayCaller>();

        var ketQua = await caller.CallAsync(new RefundGatewayCallRequest
        {
            RefundId = khoan.Id,
            RefundCode = khoan.RefundCode,
            MethodCode = PaymentProviderCodes.MoMo,
            Amount = 175_000.50m,
        });

        Assert.Equal(RefundLogResult.Failed, ketQua.Result);
        Assert.Equal(0, handler.SoLanGoi);
    }

    // ------------------------------------------------------------------
    // Lỗi vận chuyển — không bao giờ đoán
    // ------------------------------------------------------------------

    [Fact]
    public async Task Loi_van_chuyen_tra_Unresolved_va_khong_cham_CSDL()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("mạng chết"));
        using var db = TaoDb();
        var khoan = await SeedRefundAsync(db, PaymentProviderCodes.MoMo);
        using var dichVu = TaoDichVu(handler, db);
        var caller = dichVu.GetRequiredService<IRefundGatewayCaller>();

        var ketQua = await caller.CallAsync(new RefundGatewayCallRequest
        {
            RefundId = khoan.Id,
            RefundCode = khoan.RefundCode,
            MethodCode = PaymentProviderCodes.MoMo,
            Amount = khoan.Amount,
        });

        // Cổng không trả lời → Unresolved: KHÔNG được đoán thành thất bại (mở đường hoàn hai lần khi
        // lượt trước đã tới nơi). Và hợp đồng seam: caller CHỈ ĐỌC CSDL, không tự ghi gì.
        Assert.Equal(RefundLogResult.Unresolved, ketQua.Result);

        using var dbDoc = TaoDb();
        var sau = await dbDoc.Refunds.SingleAsync(r => r.Id == khoan.Id);
        Assert.Equal(RefundStatus.Pending, sau.Status);
        Assert.Equal(0, await dbDoc.RefundLogs.CountAsync(l => l.RefundId == khoan.Id));
    }

    [Fact]
    public async Task Timeout_noi_bo_tra_Unresolved()
    {
        // Client hết thời gian chờ nội bộ (token ngoài KHÔNG bị huỷ) → TaskCanceledException → caller
        // dịch thành Unresolved chứ không đoán.
        var handler = new FakeHandler(_ => throw new TaskCanceledException("hết thời gian chờ"));
        using var db = TaoDb();
        var khoan = await SeedRefundAsync(db, PaymentProviderCodes.MoMo);
        using var dichVu = TaoDichVu(handler, db);
        var caller = dichVu.GetRequiredService<IRefundGatewayCaller>();

        var ketQua = await caller.CallAsync(new RefundGatewayCallRequest
        {
            RefundId = khoan.Id,
            RefundCode = khoan.RefundCode,
            MethodCode = PaymentProviderCodes.MoMo,
            Amount = khoan.Amount,
        });

        Assert.Equal(RefundLogResult.Unresolved, ketQua.Result);
    }

    [Fact]
    public async Task App_dang_tat_giua_luot_goi_nem_tiep()
    {
        // Token ngoài bị huỷ (app tắt) → ném TIẾP để tầng trên dừng đúng cách, không nuốt thành
        // Unresolved (hợp đồng đã ghi ở IRefundGatewayCaller).
        var handler = new FakeHandler(_ => throw new OperationCanceledException());
        using var db = TaoDb();
        var khoan = await SeedRefundAsync(db, PaymentProviderCodes.MoMo);
        using var dichVu = TaoDichVu(handler, db);
        var caller = dichVu.GetRequiredService<IRefundGatewayCaller>();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => caller.CallAsync(
            new RefundGatewayCallRequest
            {
                RefundId = khoan.Id,
                RefundCode = khoan.RefundCode,
                MethodCode = PaymentProviderCodes.MoMo,
                Amount = khoan.Amount,
            },
            cts.Token));
    }

    // ------------------------------------------------------------------
    // Chạy trọn luồng qua RefundAttemptService
    // ------------------------------------------------------------------

    [Fact]
    public async Task Mot_khoan_hoan_di_tron_luong_thanh_cong()
    {
        // Chuỗi thật từ đầu tới cuối: RefundAttemptService giành dòng → gọi RefundGatewayCaller →
        // MoMoRefundClient qua HTTP giả → kết quả ghi ngược vào Refunds + RefundLogs. Đây là lời hứa
        // "khoản Pending chuyển Succeeded khi cổng xác nhận" chạy xuyên hai tầng, không dựng tay kết
        // quả nào (cùng tinh thần PaymentSandboxCrossLayerTests).
        var handler = new FakeHandler(TraLoiTheoDuongDan);
        using var db = TaoDb();
        var khoan = await SeedRefundAsync(db, PaymentProviderCodes.MoMo);
        using var dichVu = TaoDichVu(handler, db);
        var caller = dichVu.GetRequiredService<IRefundGatewayCaller>();
        var service = new RefundAttemptService(db, caller, NullLogger<RefundAttemptService>.Instance);

        var ketQua = await service.ExecuteAsync(khoan.Id, RefundLogTrigger.Initial, BayGio);

        Assert.True(ketQua.Claimed);
        Assert.Equal(RefundLogResult.Succeeded, ketQua.Result);

        using var dbDoc = TaoDb();
        var sau = await dbDoc.Refunds.SingleAsync(r => r.Id == khoan.Id);
        Assert.Equal(RefundStatus.Succeeded, sau.Status);
        Assert.Equal("999888777", sau.GatewayTransactionId);
        Assert.Null(sau.FailReason);
        Assert.Null(sau.NextRetryAt);

        var nhatKy = Assert.Single(await dbDoc.RefundLogs.Where(l => l.RefundId == khoan.Id).ToListAsync());
        Assert.Equal(RefundLogResult.Succeeded, nhatKy.Result);
        Assert.Equal("999888777", nhatKy.GatewayTransactionId);
    }

    // ------------------------------------------------------------------
    // Chỗ nối DI của app thật
    // ------------------------------------------------------------------

    [Fact]
    public void DI_cua_app_giai_IRefundGatewayCaller_ra_dung_client_that()
    {
        using var factory = new TestAppFactory();
        using var scope = factory.Services.CreateScope();

        // Dòng đăng ký trong Program.cs phải trỏ vào client THẬT — còn trỏ vào Placeholder là mọi
        // lượt gọi trả Unresolved và tiền của khách nằm chờ vĩnh viễn ở bước đối soát (hợp đồng đã
        // ghi sẵn ở IRefundGatewayCaller).
        Assert.IsType<RefundGatewayCaller>(
            scope.ServiceProvider.GetRequiredService<IRefundGatewayCaller>());
    }

    // ------------------------------------------------------------------
    // Hạ tầng test
    // ------------------------------------------------------------------

    private AppDbContext TaoDb() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_tenCsdl).Options);

    /// <summary>
    /// DI thật của chuỗi hoàn tiền: ba client cổng + HttpClient trỏ vào handler giả. Cấu hình đối tác
    /// là bộ khoá giả / sandbox công khai đã nằm sẵn trong repo — client nào bị gọi mới được dựng
    /// (đúng cơ chế ResolveLate của RefundGatewayCaller).
    /// </summary>
    private static ServiceProvider TaoDichVu(FakeHandler handler, AppDbContext db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton(Options.Create(new MoMoOptions
        {
            PartnerCode = MoMoPartnerCode,
            AccessKey = MoMoAccessKey,
            SecretKey = MoMoSecretKey,
            Endpoint = "https://test-payment.momo.vn",
        }));
        services.AddSingleton(Options.Create(new VnPayRefundOptions
        {
            TmnCode = VnPayTmnCode,
            HashSecret = VnPayHashSecret,
            RefundEndpoint = "https://sandbox.vnpayment.vn/merchant_webapi/api/transaction",
        }));
        services.AddSingleton(Options.Create(new ZaloPayOptions
        {
            AppId = ZaloPayAppId,
            Key1 = ZaloPayKey1,
            Key2 = ZaloPayKey2,
            Endpoint = "https://sb-openapi.zalopay.vn",
        }));
        services.AddSingleton<HttpClient>(new HttpClient(handler));
        services.AddTransient<MoMoRefundClient>();
        services.AddTransient<VnPayRefundClient>();
        services.AddTransient<ZaloPayRefundClient>();
        services.AddTransient<IRefundGatewayCaller, RefundGatewayCaller>();
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Seed một giao dịch gốc (Success) + một khoản hoàn trỏ vào nó. Dữ liệu cổng gốc chọn theo
    /// phương thức: mỗi cổng cần một hình dạng GatewayTransactionId (và VNPay cần cả PaidAt).
    /// </summary>
    private static async Task<Refund> SeedRefundAsync(AppDbContext db, string methodCode)
    {
        var (maCong, paidAt) = methodCode switch
        {
            PaymentProviderCodes.VnPay or PaymentProviderCodes.BankCard =>
                ("14408096", BayGio.AddHours(-2)),
            PaymentProviderCodes.ZaloPay =>
                ("2609270000012345", BayGio.AddHours(-2)),
            _ => ("2757953842", BayGio.AddHours(-2)),
        };

        var giaoDich = new Payment
        {
            PaymentCode = "PM-8f3a2c1d",
            MethodCode = methodCode,
            Amount = 175_000m,
            Status = PaymentStatus.Success,
            UserId = Guid.NewGuid(),
            TripId = Guid.NewGuid(),
            SeatNumbers = "A1;A2",
            GatewayTransactionId = maCong,
            PaidAt = paidAt,
            CreatedAt = BayGio.AddHours(-2),
        };
        db.Payments.Add(giaoDich);

        var khoanHoan = new Refund
        {
            RefundCode = "RF-8f3a2c1d",
            Source = RefundSource.PaymentFailure,
            PaymentId = giaoDich.Id,
            UserId = giaoDich.UserId,
            Amount = 175_000m,
            Status = RefundStatus.Pending,
            CreatedAt = BayGio.AddHours(-1),
        };
        db.Refunds.Add(khoanHoan);
        await db.SaveChangesAsync();
        return khoanHoan;
    }

    /// <summary>Trả lời thành công theo đường dẫn cổng — mỗi client nhận đúng hình dạng của mình.</summary>
    private static HttpResponseMessage TraLoiTheoDuongDan(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;

        if (path.EndsWith("/v2/gateway/api/refund", StringComparison.Ordinal))
        {
            return JsonResponse(new { resultCode = 0, message = "Success.", transId = 999888777 });
        }

        if (path.Contains("merchant_webapi", StringComparison.Ordinal))
        {
            return JsonResponse(new
            {
                vnp_ResponseCode = "00",
                vnp_Message = "Confirm Success",
                vnp_TransactionNo = "999888",
                vnp_TransactionStatus = "05",
            });
        }

        if (path.EndsWith("/v2/refund", StringComparison.Ordinal))
        {
            return JsonResponse(new
            {
                return_code = 1,
                return_message = "Giao dịch thành công",
                sub_return_code = 1,
                sub_return_message = "Success",
                refund_id = 1905861234567890,
            });
        }

        throw new InvalidOperationException($"Đường dẫn không mong đợi trong test: {path}");
    }

    private static HttpResponseMessage JsonResponse(object body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
    };

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int SoLanGoi { get; private set; }

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SoLanGoi++;
            LastRequest = request;
            return Task.FromResult(responder(request));
        }
    }
}
