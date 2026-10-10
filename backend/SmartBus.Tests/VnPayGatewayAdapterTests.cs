using Microsoft.Extensions.Options;
using SmartBus.Api.Dtos.Payments;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test cho <see cref="VnPayGatewayAdapter"/> — adapter thống nhất cổng thanh toán (US 6, task
/// *"Adapter pattern thống nhất cổng thanh toán (dễ thêm cổng mới)"* — Phùng Duy Hoàng).
///
/// Dùng client VNPay THẬT với bộ khoá sandbox công khai (in nguyên văn trong tài liệu VNPay, dùng
/// chung cho mọi người thử — không phải khoá nhóm): phép dịch của adapter phải khớp chữ ký THẬT
/// mới chứng minh được là không làm hỏng chuỗi ký. Tham số callback dùng vector C/D/E tính độc lập
/// bằng Python trên bộ giá trị này — không lấy chữ ký do chính client C# sinh ra để khỏi "tự
/// nghiệm tự đúng".
/// </summary>
public class VnPayGatewayAdapterTests
{
    // Bộ khoá SANDBOX CÔNG KHAI — cùng bộ với VnPayGatewayServiceTests (xem chú thích ở đó).
    private const string TmnCode = "CGXZLS0Z";
    private const string HashSecret = "XNBCJFAKAZQSGTARRLGCHVZWCIOIGSHN";

    // Bộ dữ liệu giao dịch giả — cùng bộ giá trị đưa vào script Python tính vector.
    private const string PaymentCode = "PM-8f3a2c1d";
    private const long Amount = 175_000;

    private const string SuccessHash =
        "7345312C162CEA5FBE6DD3A90B1BE25028B92712A81768D2ACDA8810DBA4FC2BB8C12E07BBC1ADF31B35A82A107F33C9BB1D4BFEBACB1398CEB89013F2A6B976";

    private const string CancelledHash =
        "01E7C1AC4C1FAADA322907AD0EB939145B66BA7C32639AFAC458D74132BA0AEE911012212BD97BBC23E1F1DDF529DE7898A2FAB54999A64939869AEFB10E350F";

    private const string FractionalHash =
        "3710D763163DDC2DCD112B4E65A7EEA73ABE0622679CF76D295B6AC72056F9359CA205A091C389CD74FA7F0A90B19D823A0962E5DA0BBF510C1381EC318E8CC4";

    private static VnPayGatewayService ClientWithDefaults()
        => new(Options.Create(new VnPayOptions { TmnCode = TmnCode, HashSecret = HashSecret }));

    [Fact]
    public async Task Initiate_dung_url_ma_chinh_client_VNPay_ky_lai_duoc()
    {
        var client = ClientWithDefaults();

        var result = await new VnPayGatewayAdapter(client).InitiateAsync(new PaymentInitiationRequest
        {
            PaymentCode = PaymentCode,
            Amount = Amount,
            OrderInfo = "Thanh toan ve xe SmartBus",
            ReturnUrl = "http://localhost:5173/payment-waiting",
            IpAddress = "203.113.131.1",
        });

        Assert.True(result.Success);

        // Chính client VNPay phải verify được URL adapter trả về — phép thử tròn nhất cho một lớp
        // chỉ dịch tham số.
        var parsed = ParseQuery(result.RedirectUrl);
        Assert.True(client.IsValidSignature(parsed));
        Assert.Equal(PaymentCode, parsed["vnp_TxnRef"]);
        Assert.Equal("17500000", parsed["vnp_Amount"]); // 175.000đ ×100 theo quy ước cổng
        Assert.Equal(TmnCode, parsed["vnp_TmnCode"]);
    }

    [Fact]
    public void Verify_vector_C_thanh_cong_thi_Succeeded()
    {
        var result = AdapterWithDefaults().VerifyCallback(new PaymentCallbackInput { Query = SuccessQuery() });

        Assert.True(result.IsValid);
        Assert.True(result.Succeeded);
        Assert.Equal(PaymentCode, result.PaymentCode);
        Assert.Equal(175_000m, result.Amount);
        Assert.Equal("14000001", result.GatewayTransactionId);
        Assert.Equal("00", result.ProviderResponseCode);

        // vnp_PayDate "20261010153200" là giờ GMT+7 — adapter phải quy về UTC (08:32:00) cho cột
        // PaidAt (timestamptz); giữ nguyên Kind = Utc để Npgsql ghi được.
        Assert.Equal(new DateTime(2026, 10, 10, 8, 32, 0, DateTimeKind.Utc), result.PaidAt);
        Assert.Equal(DateTimeKind.Utc, result.PaidAt!.Value.Kind);
    }

    [Fact]
    public void Verify_vector_D_khach_huy_thi_IsValid_nhung_khong_Succeeded()
    {
        var query = SuccessQuery();
        query["vnp_ResponseCode"] = "24";
        query["vnp_TransactionStatus"] = "02";
        query["vnp_SecureHash"] = CancelledHash;

        var result = AdapterWithDefaults().VerifyCallback(new PaymentCallbackInput { Query = query });

        // Chữ ký vẫn đúng (cổng ký thật) nhưng tiền chưa vào — hai chuyện phải tách bạch.
        Assert.True(result.IsValid);
        Assert.False(result.Succeeded);
        Assert.Equal("24", result.ProviderResponseCode);
    }

    [Fact]
    public void Verify_vector_E_so_tien_le_khong_bi_cat_thanh_so_tron()
    {
        var query = SuccessQuery();
        query["vnp_Amount"] = "17500050";
        query["vnp_SecureHash"] = FractionalHash;

        var result = AdapterWithDefaults().VerifyCallback(new PaymentCallbackInput { Query = query });

        Assert.True(result.IsValid);
        // 0,5 đồng lẻ phải LỘ ra cho bước đối chiếu — cắt thành số tròn là khớp nhầm tiền.
        Assert.Equal(175_000.50m, result.Amount);
    }

    [Fact]
    public void Verify_sua_so_tien_thi_chu_ky_sai()
    {
        var query = SuccessQuery();
        query["vnp_Amount"] = "1";

        var result = AdapterWithDefaults().VerifyCallback(new PaymentCallbackInput { Query = query });

        Assert.False(result.IsValid);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Verify_SecureHash_khong_phai_hex_tra_false_khong_nem()
    {
        var query = SuccessQuery();
        query["vnp_SecureHash"] = "khong-phai-hex";

        var result = AdapterWithDefaults().VerifyCallback(new PaymentCallbackInput { Query = query });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Verify_khong_co_tham_so_tra_false_khong_nem()
    {
        var result = AdapterWithDefaults().VerifyCallback(new PaymentCallbackInput());

        Assert.False(result.IsValid);
        Assert.False(result.Succeeded);
        Assert.Equal(string.Empty, result.PaymentCode); // DTO trung tính cam kết không null
    }

    /// <summary>
    /// Tham số callback vector C (thanh toán thành công) — đúng bộ giá trị đưa vào script Python.
    /// Giá trị ở dạng ĐÃ GIẢI MÃ, đúng hình dạng endpoint đưa vào <c>IsValidSignature</c>
    /// (ASP.NET bind Request.Query thành giá trị giải mã, dấu cách đã hết dấu <c>+</c>).
    /// </summary>
    private static Dictionary<string, string> SuccessQuery() => new()
    {
        ["vnp_Amount"] = "17500000",
        ["vnp_BankCode"] = "NCB",
        ["vnp_BankTranNo"] = "NCB20261010153200",
        ["vnp_CardType"] = "ATM",
        ["vnp_OrderInfo"] = "Thanh toan ve xe SmartBus",
        ["vnp_PayDate"] = "20261010153200",
        ["vnp_ResponseCode"] = "00",
        ["vnp_TmnCode"] = TmnCode,
        ["vnp_TransactionNo"] = "14000001",
        ["vnp_TransactionStatus"] = "00",
        ["vnp_TxnRef"] = PaymentCode,
        ["vnp_SecureHash"] = SuccessHash,
    };

    private static VnPayGatewayAdapter AdapterWithDefaults() => new(ClientWithDefaults());

    /// <summary>
    /// Tách query string thành tham số ĐÃ GIẢI MÃ — đúng hình dạng endpoint sẽ đưa vào
    /// <c>IsValidSignature</c> ('+' là dấu cách, phần trăm mã hoá giải ngược lại).
    /// </summary>
    private static Dictionary<string, string> ParseQuery(string url)
    {
        var result = new Dictionary<string, string>();

        foreach (var pair in url[(url.IndexOf('?') + 1)..].Split('&'))
        {
            var equals = pair.IndexOf('=');
            var name = Uri.UnescapeDataString(pair[..equals]);
            var value = Uri.UnescapeDataString(pair[(equals + 1)..].Replace("+", " "));
            result[name] = value;
        }

        return result;
    }
}
