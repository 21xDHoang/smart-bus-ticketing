using Microsoft.Extensions.Options;
using SmartBus.Api.Dtos.Payments;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test cho <see cref="BankCardGatewayAdapter"/> — mã phương thức <c>BankCard</c> ("thẻ ngân hàng")
/// chạy qua kênh thẻ của VNPay (US 6, task *"Tích hợp ZaloPay và thẻ ngân hàng"* — Nguyễn Duy Kiên).
///
/// Dùng client VNPay THẬT với bộ khoá sandbox công khai (in nguyên văn trong tài liệu VNPay, cùng bộ
/// với VnPayGatewayServiceTests/VnPayGatewayAdapterTests): kênh thẻ là một THAM SỐ của URL ký sẵn,
/// nên phải chứng minh chữ ký vẫn đúng sau khi adapter ghim thêm tham số đó.
///
/// Hai chốt: <c>BankCard</c> và <c>VNPay</c> phải KHÁC NHAU thật (khác đúng ở vnp_BankCode), và phần
/// chuẩn hoá callback phải là CÙNG một kết luận với adapter VNPay — vì nó uỷ thác nguyên cho adapter
/// kia chứ không chép lại.
/// </summary>
public class BankCardGatewayAdapterTests
{
    // Bộ khoá SANDBOX CÔNG KHAI — cùng bộ với VnPayGatewayServiceTests (xem chú thích ở đó).
    private const string TmnCode = "CGXZLS0Z";
    private const string HashSecret = "XNBCJFAKAZQSGTARRLGCHVZWCIOIGSHN";

    private const string PaymentCode = "PM-8f3a2c1d";
    private const long Amount = 175_000;

    /// <summary>Vector C của VnPayGatewayAdapterTests — thanh toán thành công, tính bằng Python.</summary>
    private const string SuccessHash =
        "7345312C162CEA5FBE6DD3A90B1BE25028B92712A81768D2ACDA8810DBA4FC2BB8C12E07BBC1ADF31B35A82A107F33C9BB1D4BFEBACB1398CEB89013F2A6B976";

    private static VnPayGatewayService ClientWithDefaults()
        => new(Options.Create(new VnPayOptions { TmnCode = TmnCode, HashSecret = HashSecret }));

    private static PaymentInitiationRequest InitiationRequest() => new()
    {
        PaymentCode = PaymentCode,
        Amount = Amount,
        OrderInfo = "Thanh toan ve xe SmartBus",
        ReturnUrl = "http://localhost:5173/payment-waiting",
        IpAddress = "203.113.131.1",
    };

    [Fact]
    public async Task Initiate_ghim_kenh_the_va_chu_ky_van_dung()
    {
        var client = ClientWithDefaults();
        var bankCard = new BankCardGatewayAdapter(client, new VnPayGatewayAdapter(client));

        var result = await bankCard.InitiateAsync(InitiationRequest());

        Assert.True(result.Success); // URL ký sẵn tại chỗ — không chạm mạng, thành/hỏng thật chỉ biết qua callback

        var parsed = ParseQuery(result.RedirectUrl);

        // VNBANK = thẻ ATM nội địa / tài khoản ngân hàng — chốt ở docs/api-contract.md.
        Assert.Equal("VNBANK", parsed["vnp_BankCode"]);
        // Chữ ký phải phủ CẢ tham số vừa ghim: thêm tham số sau khi ký là cổng báo sai chữ ký.
        Assert.True(client.IsValidSignature(parsed));
        Assert.Equal(PaymentCode, parsed["vnp_TxnRef"]);
        Assert.Equal("17500000", parsed["vnp_Amount"]); // 175.000đ ×100 theo quy ước cổng
    }

    [Fact]
    public async Task BankCard_khac_VNPay_o_dung_tham_so_kenh()
    {
        var client = ClientWithDefaults();
        var request = InitiationRequest();

        var urlVnPay = (await new VnPayGatewayAdapter(client).InitiateAsync(request)).RedirectUrl;
        var urlBankCard = (await new BankCardGatewayAdapter(client, new VnPayGatewayAdapter(client))
            .InitiateAsync(request)).RedirectUrl;

        // VNPay để khách tự chọn phương thức trên trang cổng; BankCard vào thẳng kênh thẻ. Cùng một
        // cổng bên dưới nhưng hai mã phương thức phải có hành vi KHÁC nhau, nếu không thì mã thứ tư
        // của hợp đồng chẳng có lý do tồn tại.
        Assert.False(ParseQuery(urlVnPay).ContainsKey("vnp_BankCode"));
        Assert.Equal("VNBANK", ParseQuery(urlBankCard)["vnp_BankCode"]);
    }

    [Fact]
    public void VerifyCallback_uy_thac_cho_adapter_VNPay_ket_luan_giong_het()
    {
        var client = ClientWithDefaults();
        var input = new PaymentCallbackInput { Query = SuccessQuery() };

        var quaBankCard = new BankCardGatewayAdapter(client, new VnPayGatewayAdapter(client)).VerifyCallback(input);
        var quaVnPay = new VnPayGatewayAdapter(client).VerifyCallback(input);

        Assert.True(quaBankCard.IsValid);
        Assert.True(quaBankCard.Succeeded);
        Assert.Equal(PaymentCode, quaBankCard.PaymentCode);
        Assert.Equal(175_000m, quaBankCard.Amount);
        Assert.Equal("14000001", quaBankCard.GatewayTransactionId);

        // Kênh thẻ gọi về CHÍNH endpoint callback của VNPay (cùng cổng, cùng tham số vnp_, cùng chữ
        // ký SHA512) — nên kết luận của hai adapter phải trùng nhau từng trường một.
        Assert.Equal(quaVnPay.IsValid, quaBankCard.IsValid);
        Assert.Equal(quaVnPay.Succeeded, quaBankCard.Succeeded);
        Assert.Equal(quaVnPay.PaymentCode, quaBankCard.PaymentCode);
        Assert.Equal(quaVnPay.Amount, quaBankCard.Amount);
        Assert.Equal(quaVnPay.GatewayTransactionId, quaBankCard.GatewayTransactionId);
        Assert.Equal(quaVnPay.ProviderResponseCode, quaBankCard.ProviderResponseCode);
    }

    /// <summary>Tham số callback vector C — đúng bộ giá trị của VnPayGatewayAdapterTests.</summary>
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

    /// <summary>Tách query string thành tham số ĐÃ GIẢI MÃ, đúng hình dạng endpoint đưa vào adapter.</summary>
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
