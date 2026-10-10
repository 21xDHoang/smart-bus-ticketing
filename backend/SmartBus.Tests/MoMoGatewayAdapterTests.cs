using SmartBus.Api.Dtos.Payments;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test cho <see cref="MoMoGatewayAdapter"/> — adapter thống nhất cổng thanh toán (US 6, task
/// *"Adapter pattern thống nhất cổng thanh toán (dễ thêm cổng mới)"* — Phùng Duy Hoàng).
///
/// Adapter chỉ DỊCH và CHUẨN HOÁ KẾT LUẬN, nên ở đây dùng client MoMo GIẢ (không HTTP): cần ghim
/// đúng các phép dịch, cộng hai chốt an toàn — chữ ký chưa hợp lệ thì KHÔNG BAO GIỜ báo thành
/// công, và dữ liệu ngoài hỏng (JSON rác, thân rỗng) thì trả "không hợp lệ" chứ không ném. Client
/// thật đã có bộ test riêng của nó — không lặp lại ở đây.
/// </summary>
public class MoMoGatewayAdapterTests
{
    private const string OrderId = "PM-8f3a2c1d";
    private const long Amount = 250_000;

    private static MoMoGatewayAdapter AdapterWith(FakeMoMoGatewayService fake) => new(fake);

    [Fact]
    public async Task Initiate_dich_dung_sang_MoMo_va_dich_ket_qua_ve()
    {
        var fake = new FakeMoMoGatewayService
        {
            CreateResult = new MoMoCreatePaymentResult
            {
                Success = true,
                PayUrl = "https://test-payment.momo.vn/pay/abc",
                Deeplink = "momo://app?action=pay",
                QrCodeUrl = "https://test-payment.momo.vn/qr/abc",
                ResultCode = 0,
                Message = "Thành công",
            },
        };

        var result = await AdapterWith(fake).InitiateAsync(new PaymentInitiationRequest
        {
            PaymentCode = OrderId,
            Amount = Amount,
            OrderInfo = "Thanh toan ve xe SmartBus",
            ReturnUrl = "http://localhost:5173/payment-waiting",
            IpnUrl = "https://api.example.com/api/payments/momo/ipn",
        });

        var sent = fake.LastCreateRequest!;
        Assert.Equal(OrderId, sent.OrderId);
        Assert.Equal(Amount, sent.Amount);
        Assert.Equal("Thanh toan ve xe SmartBus", sent.OrderInfo);
        Assert.Equal("http://localhost:5173/payment-waiting", sent.RedirectUrl);
        Assert.Equal("https://api.example.com/api/payments/momo/ipn", sent.IpnUrl);

        Assert.True(result.Success);
        Assert.Equal("https://test-payment.momo.vn/pay/abc", result.RedirectUrl);
        Assert.Equal("momo://app?action=pay", result.Deeplink);
        Assert.Equal("https://test-payment.momo.vn/qr/abc", result.QrCodeUrl);
        Assert.Equal("Thành công", result.Message);
    }

    [Fact]
    public async Task Initiate_de_RequestId_null_cho_client_tu_sinh()
    {
        var fake = new FakeMoMoGatewayService();

        await AdapterWith(fake).InitiateAsync(new PaymentInitiationRequest
        {
            PaymentCode = OrderId,
            Amount = Amount,
        });

        // Client MoMo tự sinh GUID khi RequestId null — adapter không được tự bịa mã riêng.
        Assert.Null(fake.LastCreateRequest!.RequestId);
    }

    [Fact]
    public void Verify_chu_ky_hop_le_ma_0_thi_Succeeded()
    {
        var fake = new FakeMoMoGatewayService { ValidCallback = true };

        var result = AdapterWith(fake).VerifyCallback(new PaymentCallbackInput { Body = CallbackJson(0) });

        Assert.True(result.IsValid);
        Assert.True(result.Succeeded);
        Assert.Equal(OrderId, result.PaymentCode);
        Assert.Equal(Amount, result.Amount);
        Assert.Equal("3456789", result.GatewayTransactionId);
        Assert.Equal("0", result.ProviderResponseCode);
        Assert.Equal("Thành công", result.Message);

        // responseTime 1750000000000 ms = 2025-06-15T15:06:40Z — mốc cổng ghi nhận thu tiền, để
        // nguyên dạng UTC (Kind) vì cột PaidAt là timestamptz: Npgsql ném lỗi nếu Kind khác Utc.
        Assert.Equal(new DateTime(2025, 6, 15, 15, 6, 40, DateTimeKind.Utc), result.PaidAt);
        Assert.Equal(DateTimeKind.Utc, result.PaidAt!.Value.Kind);
    }

    [Fact]
    public void Verify_cong_khong_kem_thoi_diem_thi_PaidAt_null()
    {
        var fake = new FakeMoMoGatewayService { ValidCallback = true };

        var result = AdapterWith(fake).VerifyCallback(
            new PaymentCallbackInput { Body = CallbackJson(0, responseTime: 0) });

        Assert.True(result.IsValid);
        Assert.True(result.Succeeded);
        // Cổng không kèm mốc thu tiền — adapter không được tự bịa; tầng chốt sẽ lấy giờ hệ thống.
        Assert.Null(result.PaidAt);
    }

    [Fact]
    public void Verify_ma_9000_cho_xac_thuc_thi_chua_Succeeded()
    {
        var fake = new FakeMoMoGatewayService { ValidCallback = true };

        var result = AdapterWith(fake).VerifyCallback(new PaymentCallbackInput { Body = CallbackJson(9000) });

        // 9000 = khách đã xác thực, tiền CHƯA chắc chắn vào — chưa được lật trạng thái vé.
        Assert.True(result.IsValid);
        Assert.False(result.Succeeded);
        Assert.Equal("9000", result.ProviderResponseCode);
    }

    [Fact]
    public void Verify_chu_ky_sai_thi_khong_bao_gio_Succeeded()
    {
        var fake = new FakeMoMoGatewayService { ValidCallback = false };

        var result = AdapterWith(fake).VerifyCallback(new PaymentCallbackInput { Body = CallbackJson(0) });

        Assert.False(result.IsValid);
        Assert.False(result.Succeeded);
        // Trường đã tách vẫn giữ nguyên cho tầng trên ghi log — chỉ KẾT LUẬN bị khoá.
        Assert.Equal(OrderId, result.PaymentCode);
    }

    [Fact]
    public void Verify_JSON_rac_tra_khong_hop_le_va_khong_cham_client()
    {
        var fake = new FakeMoMoGatewayService { ValidCallback = true };

        var result = AdapterWith(fake).VerifyCallback(new PaymentCallbackInput { Body = "khong-phai-json{{" });

        Assert.False(result.IsValid);
        Assert.False(result.Succeeded);
        Assert.Equal(0, fake.IsValidCallbackCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("null")]
    public void Verify_than_rong_hoac_null_tra_khong_hop_le(string? body)
    {
        var fake = new FakeMoMoGatewayService { ValidCallback = true };

        var result = AdapterWith(fake).VerifyCallback(new PaymentCallbackInput { Body = body });

        Assert.False(result.IsValid);
        Assert.False(result.Succeeded);
        Assert.Equal(0, fake.IsValidCallbackCalls);
    }

    /// <summary>IPN đúng hình dạng MoMo gửi (tên trường camelCase như cổng).</summary>
    private static string CallbackJson(
        int resultCode, long transId = 3456789, string message = "Thành công",
        long responseTime = 1750000000000) => $$"""
        {
          "partnerCode": "MOMO_TEST",
          "orderId": "PM-8f3a2c1d",
          "requestId": "req-1",
          "amount": 250000,
          "orderInfo": "Thanh toan ve xe SmartBus",
          "orderType": "momo_wallet",
          "transId": {{transId}},
          "resultCode": {{resultCode}},
          "message": "{{message}}",
          "payType": "qr",
          "responseTime": {{responseTime}},
          "extraData": "",
          "signature": "chu-ky-gia"
        }
        """;

    /// <summary>
    /// Client MoMo giả — không HTTP. Đếm số lần bị hỏi chữ ký để ghim "dữ liệu hỏng thì không chạm
    /// client", và truy vấn thì ném: interface thống nhất CỐ Ý không có truy vấn (mới MoMo có;
    /// job đối soát gọi thẳng client MoMo — xem ghi chú hợp đồng).
    /// </summary>
    private sealed class FakeMoMoGatewayService : IMoMoGatewayService
    {
        public MoMoCreatePaymentResult CreateResult { get; set; } = new();

        public bool ValidCallback { get; set; } = true;

        public MoMoCreatePaymentRequest? LastCreateRequest { get; private set; }

        public int IsValidCallbackCalls { get; private set; }

        public Task<MoMoCreatePaymentResult> CreatePaymentAsync(
            MoMoCreatePaymentRequest request, CancellationToken cancellationToken = default)
        {
            LastCreateRequest = request;
            return Task.FromResult(CreateResult);
        }

        public bool IsValidCallback(MoMoCallback callback)
        {
            IsValidCallbackCalls++;
            return ValidCallback;
        }

        public Task<MoMoQueryTransactionResult> QueryTransactionAsync(
            string orderId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException(
                "Adapter thống nhất không dùng truy vấn — job đối soát gọi thẳng client MoMo.");
    }
}
