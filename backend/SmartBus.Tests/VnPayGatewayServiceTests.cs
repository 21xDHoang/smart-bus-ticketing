using System.Globalization;
using Microsoft.Extensions.Options;
using SmartBus.Api.Dtos.Payments;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test cho <see cref="VnPayGatewayService"/> — client cổng thanh toán VNPay (US 6, task
/// *"Tích hợp VNPay: tạo URL thanh toán + verify chữ ký"* — Nguyễn Duy Kiên).
///
/// Đơn giản hơn bộ test MoMo một bậc: client này KHÔNG gọi mạng (VNPay không có API server-to-server
/// để tạo giao dịch — ta ký sẵn URL rồi chuyển khách sang cổng), nên không cần HTTP giả lẫn CSDL:
/// mọi phép ký/kiểm là hàm thuần.
///
/// Hai chữ ký dùng vector tính SẴN bằng Python (độc lập với code C#) để ghim đúng chuỗi chuẩn
/// VNPay: thứ tự trường xếp theo bảng chữ cái, giá trị mã hoá kiểu PHP urlencode
/// (<c>quote_plus</c> rồi <c>~</c> -> <c>%7E</c>: dấu cách ra <c>+</c>, "://" ra <c>%3A%2F%2F</c>),
/// hex chữ HOA. Vector là chốt chống "sửa cho đẹp rồi hỏng chữ ký" — đổi thứ tự trường, đổi kiểu
/// mã hoá, hay quên nhân 100 là test đỏ ngay. Xác nhận cuối với cổng thật làm trên sandbox VNPay.
/// </summary>
public class VnPayGatewayServiceTests
{
    // Bộ khoá SANDBOX CÔNG KHAI của VNPay — in nguyên văn trong tài liệu tích hợp, dùng chung cho
    // mọi người thử nghiệm, KHÔNG phải khoá đối tác thật của nhóm (khoá thật đặt qua user-secrets —
    // luật 2, xem VnPayOptions).
    private const string TmnCode = "CGXZLS0Z";
    private const string HashSecret = "XNBCJFAKAZQSGTARRLGCHVZWCIOIGSHN";

    // Bộ dữ liệu giao dịch giả — cùng bộ giá trị đưa vào script Python tính vector.
    private const string TxnRef = "PM-8f3a2c1d";
    private const string OrderInfo = "Thanh toan ve xe SmartBus";
    private const string ReturnUrl = "http://localhost:5173/payment-waiting";

    /// <summary>
    /// Chuỗi ký của giao dịch tối thiểu (không BankCode/ExpireDate) — cũng chính là query string
    /// của URL tạo ra. 12 tham số xếp theo bảng chữ cái, tính bằng Python trên bộ dữ liệu trên.
    /// </summary>
    private const string SigningDataVectorA =
        "vnp_Amount=17500000&vnp_Command=pay&vnp_CreateDate=20261010153000&vnp_CurrCode=VND"
        + "&vnp_IpAddr=203.113.131.1&vnp_Locale=vn&vnp_OrderInfo=Thanh+toan+ve+xe+SmartBus"
        + "&vnp_OrderType=other&vnp_ReturnUrl=http%3A%2F%2Flocalhost%3A5173%2Fpayment-waiting"
        + "&vnp_TmnCode=CGXZLS0Z&vnp_TxnRef=PM-8f3a2c1d&vnp_Version=2.1.0";

    /// <summary>Chữ ký HMAC-SHA512 (hex HOA) của <see cref="SigningDataVectorA"/> — Python.</summary>
    private const string SecureHashVectorA = "5643716FC858289A432FC6C74C9281C8B9D32A13600525FDA24A5E7227BEBC68ABDBB03894747633C93A32E01B4D25962EEDE0F8D8FD123B2BE4309992EDCBDC";

    /// <summary>
    /// Chuỗi ký khi có thêm <c>vnp_BankCode=NCB</c> (đứng sau Amount) và <c>vnp_ExpireDate</c>
    /// (đứng sau CurrCode) — chốt luôn vị trí hai tham số tuỳ chọn trong chuỗi xếp chữ cái.
    /// </summary>
    private const string SigningDataVectorB =
        "vnp_Amount=17500000&vnp_BankCode=NCB&vnp_Command=pay&vnp_CreateDate=20261010153000"
        + "&vnp_CurrCode=VND&vnp_ExpireDate=20261010160000&vnp_IpAddr=203.113.131.1&vnp_Locale=vn"
        + "&vnp_OrderInfo=Thanh+toan+ve+xe+SmartBus&vnp_OrderType=other"
        + "&vnp_ReturnUrl=http%3A%2F%2Flocalhost%3A5173%2Fpayment-waiting&vnp_TmnCode=CGXZLS0Z"
        + "&vnp_TxnRef=PM-8f3a2c1d&vnp_Version=2.1.0";

    /// <summary>Chữ ký HMAC-SHA512 (hex HOA) của <see cref="SigningDataVectorB"/> — Python.</summary>
    private const string SecureHashVectorB = "C76E9AA40BA2B1DC61914000D4D986BAB93D2AA3746A13ED153B55463EBA2A5D59BCF7AD20446BF7BF5ACA4CFFBD1E274303EEB5F1FFB7F61C76485273B7C72C";

    // ---- Tạo URL thanh toán -------------------------------------------------

    [Fact]
    public void Tao_url_ky_dung_chuoi_chuan_VNPay()
    {
        var url = ServiceWithDefaults().CreatePaymentUrl(CreateRequest());

        // So nguyên URL: ghim cùng lúc thứ tự tham số, kiểu mã hoá, và chữ ký — lệch một ký tự là đỏ.
        Assert.Equal(
            $"{VnPayOptions.DefaultPaymentUrl}?{SigningDataVectorA}&vnp_SecureHash={SecureHashVectorA}",
            url);

        // Hai phép kiểm điểm danh để lúc đỏ biết ngay bệnh hay gặp:
        // số tiền gửi cổng là amount × 100, không phải amount trần.
        Assert.Contains("vnp_Amount=17500000", url);
        // Bản 2.1.0 bỏ vnp_SecureHashType — thêm vào là cổng tính chuỗi ký khác.
        Assert.DoesNotContain("vnp_SecureHashType", url);
    }

    [Fact]
    public void Tao_url_co_BankCode_va_ExpireDate_thi_vao_chuoi_ky()
    {
        var request = CreateRequest();
        request.BankCode = "NCB";
        request.ExpireDate = new DateTimeOffset(2026, 10, 10, 16, 0, 0, TimeSpan.FromHours(7));

        var url = ServiceWithDefaults().CreatePaymentUrl(request);

        Assert.Equal(
            $"{VnPayOptions.DefaultPaymentUrl}?{SigningDataVectorB}&vnp_SecureHash={SecureHashVectorB}",
            url);

        // Không truyền hai tham số đó thì chúng KHÔNG được xuất hiện trong URL (không phải chuỗi rỗng).
        var defaultUrl = ServiceWithDefaults().CreatePaymentUrl(CreateRequest());
        Assert.DoesNotContain("vnp_BankCode", defaultUrl);
        Assert.DoesNotContain("vnp_ExpireDate", defaultUrl);
    }

    [Fact]
    public void Tao_url_ghi_gio_GMT7_tu_moc_UTC()
    {
        // 08:30 giờ UTC = 15:30 giờ Việt Nam — cùng mốc với vector A, nên URL phải trùng khít.
        // Sai lệch múi giờ là ca hỏng thầm lặng: chữ ký vẫn "đúng" nhưng cổng từ chối giao dịch.
        var request = CreateRequest();
        request.CreateDate = new DateTimeOffset(2026, 10, 10, 8, 30, 0, TimeSpan.Zero);

        var url = ServiceWithDefaults().CreatePaymentUrl(request);

        Assert.Equal(
            $"{VnPayOptions.DefaultPaymentUrl}?{SigningDataVectorA}&vnp_SecureHash={SecureHashVectorA}",
            url);
        Assert.Contains("vnp_CreateDate=20261010153000", url);
    }

    [Fact]
    public void Tao_url_khong_truyen_CreateDate_thi_lay_gio_he_thong()
    {
        var request = CreateRequest();
        request.CreateDate = null;

        var url = ServiceWithDefaults().CreatePaymentUrl(request);
        var value = ParamValue(url, "vnp_CreateDate");

        Assert.True(
            DateTime.TryParseExact(
                value, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            $"vnp_CreateDate phải đúng định dạng VNPay yyyyMMddHHmmss, nhận được: {value}");
    }

    // ---- Kiểm chữ ký cổng trả về --------------------------------------------

    [Fact]
    public void Verify_chu_ky_dung_tra_true()
    {
        // Bộ tham số thật của một lượt cổng trả về: có vnp_SecureHashType di sản của bản 2.0 và
        // một tham số lạ ngoài vnp_ — cả hai phải bị bỏ qua khi dựng lại chuỗi ký.
        Assert.True(ServiceWithDefaults().IsValidSignature(ValidReturnParams()));
    }

    [Fact]
    public void Verify_sua_amount_thi_chu_ky_sai()
    {
        var parameters = ValidReturnParams();
        parameters["vnp_Amount"] = "17500100"; // sửa tiền sau khi ký — gian lận kinh điển

        Assert.False(ServiceWithDefaults().IsValidSignature(parameters));
    }

    [Fact]
    public void Verify_ky_bang_khoa_khac_tra_false()
    {
        // Bộ tham số y nguyên nhưng chữ ký tính bằng khoá khác — ví dụ khoá thật của nhóm gửi lộn
        // cho người ngoài dựng lại.
        var service = new VnPayGatewayService(Options.Create(new VnPayOptions
        {
            TmnCode = TmnCode,
            HashSecret = "MOT_KHOA_HOAN_TOAN_KHAC_KHONG_PHAI_CUA_CONG",
        }));

        Assert.False(service.IsValidSignature(ValidReturnParams()));
    }

    [Fact]
    public void Verify_chu_ky_khong_phai_hex_tra_false()
    {
        var parameters = ValidReturnParams();
        parameters["vnp_SecureHash"] = "day-khong-phai-chuoi-hex";

        Assert.False(ServiceWithDefaults().IsValidSignature(parameters));
    }

    [Fact]
    public void Verify_thieu_SecureHash_tra_false()
    {
        var parameters = ValidReturnParams();
        parameters.Remove("vnp_SecureHash");

        Assert.False(ServiceWithDefaults().IsValidSignature(parameters));
    }

    // ---- Cấu hình thiếu (luật 2) --------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Thieu_TmnCode_thi_bao_dung_ten_khoa_thieu(string tmnCode)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new VnPayGatewayService(
            Options.Create(new VnPayOptions { TmnCode = tmnCode, HashSecret = HashSecret })));

        Assert.Contains("VnPay:TmnCode", ex.Message);
        // Câu lỗi phải chỉ luôn đường điền khoá — người dựng máy mới không phải mò.
        Assert.Contains("user-secrets", ex.Message);
    }

    [Fact]
    public void Thieu_HashSecret_thi_bao_dung_ten_khoa_thieu()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new VnPayGatewayService(
            Options.Create(new VnPayOptions { TmnCode = TmnCode, HashSecret = "" })));

        Assert.Contains("VnPay:HashSecret", ex.Message);
        Assert.Contains("user-secrets", ex.Message);
    }

    // ---- Dựng dữ liệu -------------------------------------------------------

    /// <summary>Service với cấu hình đủ — PaymentUrl để trống lấy mặc định sandbox của VnPayOptions.</summary>
    private static VnPayGatewayService ServiceWithDefaults()
        => new(Options.Create(new VnPayOptions { TmnCode = TmnCode, HashSecret = HashSecret }));

    /// <summary>
    /// Request chuẩn của bộ vector: 175.000đ (thành 17500000 khi gửi cổng), CreateDate ghim đúng
    /// 15:30 giờ Việt Nam ngày 10/10/2026 — mọi test muốn ra chữ ký vector thì dùng bộ này rồi
    /// chỉnh đúng một trường cần thử.
    /// </summary>
    private static VnPayCreatePaymentRequest CreateRequest() => new()
    {
        TxnRef = TxnRef,
        Amount = 175000,
        OrderInfo = OrderInfo,
        ReturnUrl = ReturnUrl,
        IpAddress = "203.113.131.1",
        CreateDate = new DateTimeOffset(2026, 10, 10, 15, 30, 0, TimeSpan.FromHours(7)),
    };

    /// <summary>
    /// Bộ tham số một lượt cổng trả về khớp <see cref="SigningDataVectorA"/>: chữ ký đúng, kèm
    /// vnp_SecureHashType (di sản bản 2.0) và một tham số lạ ngoài vnp_ — hai thứ phải bị bỏ qua.
    /// </summary>
    private static Dictionary<string, string> ValidReturnParams() => new()
    {
        ["vnp_Amount"] = "17500000",
        ["vnp_Command"] = "pay",
        ["vnp_CreateDate"] = "20261010153000",
        ["vnp_CurrCode"] = "VND",
        ["vnp_IpAddr"] = "203.113.131.1",
        ["vnp_Locale"] = "vn",
        ["vnp_OrderInfo"] = OrderInfo,
        ["vnp_OrderType"] = "other",
        ["vnp_ReturnUrl"] = ReturnUrl,
        ["vnp_TmnCode"] = TmnCode,
        ["vnp_TxnRef"] = TxnRef,
        ["vnp_Version"] = "2.1.0",
        ["vnp_SecureHash"] = SecureHashVectorA,
        ["vnp_SecureHashType"] = "SHA512",
        ["foo"] = "bar",
    };

    /// <summary>Lấy giá trị một tham số trên query string của URL (đã mã hoá URL như lúc gửi).</summary>
    private static string ParamValue(string url, string name)
    {
        var query = url[(url.IndexOf('?') + 1)..];
        var part = query
            .Split('&')
            .Single(p => p.StartsWith(name + "=", StringComparison.Ordinal));

        return part[(name.Length + 1)..];
    }
}
