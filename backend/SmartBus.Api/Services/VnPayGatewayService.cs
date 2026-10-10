using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SmartBus.Api.Dtos.Payments;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IVnPayGatewayService"/> — client cổng VNPay (US 6, task *"Tích hợp VNPay: tạo
/// URL thanh toán + verify chữ ký"* — Nguyễn Duy Kiên).
///
/// VNPay v2.1.0 KHÔNG có API tạo giao dịch kiểu server-to-server như MoMo: ta tự dựng URL
/// <c>vpcpay.html?vnp_Amount=…</c> rồi ký cả query string bằng HMAC-SHA512, chuyển khách sang cổng.
/// Cổng trả kết quả về hai đường — Return URL (khách quay về) và IPN (cổng gọi backend) — cả hai
/// đều là GET kèm tham số <c>vnp_</c> trên query string, và cùng kiểm bằng một phép kiểm chữ ký.
///
/// Ba điểm dễ sai, ghim lại đây:
///   1. Chuỗi ký là các cặp <c>key=value</c> xếp theo bảng chữ cái (ordinal), giá trị mã hoá URL
///      đúng kiểu PHP urlencode của mẫu VNPay: dấu cách -> '+', '~' -> '%7E', hex chữ HOA. Dùng
///      <c>Uri.EscapeDataString</c> trần là sai ngay với chữ có dấu cách (nó ra %20, không ra +).
///   2. vnp_Amount là số tiền nhân 100 (VNPay tính theo đơn vị nhỏ nhất).
///   3. vnp_CreateDate/vnp_ExpireDate ghi theo GIỜ GMT+7 (giờ Việt Nam), định dạng yyyyMMddHHmmss.
///      Việt Nam không có DST từ 1975 nên lệch cố định +07:00, không cần tra TimeZoneInfo.
///
/// Cấu hình thiếu thì ném ngay khi dựng service, nêu đúng tên khoá thiếu (luật 2 — xem
/// <see cref="VnPayOptions"/>). Không gọi mạng nên không cần HttpClient: có bảng Payments hay chưa
/// cũng test được (cùng tinh thần <see cref="MoMoGatewayService"/>).
/// </summary>
public class VnPayGatewayService : IVnPayGatewayService
{
    /// <summary>Phiên bản API của cổng — VNPay 2.1.0, chốt cứng theo tài liệu tích hợp.</summary>
    private const string ApiVersion = "2.1.0";

    /// <summary>Lệnh thanh toán — hằng số của VNPay, không phải tham số nghiệp vụ.</summary>
    private const string PayCommand = "pay";

    /// <summary>Đơn vị tiền — dự án chỉ bán vé VND.</summary>
    private const string CurrencyCode = "VND";

    /// <summary>Ngôn ngữ mặc định của trang cổng khi người gọi không chọn.</summary>
    private const string DefaultLocale = "vn";

    /// <summary>Loại đơn hàng mặc định khi người gọi không chọn ("other" — mục "khác" của VNPay).</summary>
    private const string DefaultOrderType = "other";

    /// <summary>Định dạng thời gian VNPay yêu cầu cho vnp_CreateDate/vnp_ExpireDate.</summary>
    private const string VnPayDateFormat = "yyyyMMddHHmmss";

    /// <summary>
    /// Giờ Việt Nam (GMT+7) — cổng ghi nhận CreateDate/ExpireDate theo giờ này. Lệch cố định vì
    /// Việt Nam không dùng DST từ 1975; tránh hẳn phụ thuộc mã vùng của hệ điều hành.
    /// </summary>
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    private readonly VnPayOptions _options;

    public VnPayGatewayService(IOptions<VnPayOptions> options)
    {
        _options = options.Value;

        if (_options.MissingPiece() is { } missing)
        {
            throw new InvalidOperationException(
                $"Chưa cấu hình cổng thanh toán VNPay: thiếu {missing}. Đặt qua dotnet user-secrets "
                + "hoặc biến môi trường (xem appsettings.Development.json.example) — KHÔNG commit "
                + "giá trị thật lên repo public (luật 2).");
        }
    }

    public string CreatePaymentUrl(VnPayCreatePaymentRequest request)
    {
        var createDate = FormatVnPayTime(request.CreateDate ?? DateTimeOffset.UtcNow);

        // Tham số gửi cổng — đúng bộ của tài liệu VNPay 2.1.0. Tham số tuỳ chọn không có thì KHÔNG
        // đưa vào: cổng nhận chuỗi rỗng là hiểu sai ý (khác hẳn "không chọn").
        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["vnp_Version"] = ApiVersion,
            ["vnp_Command"] = PayCommand,
            ["vnp_TmnCode"] = _options.TmnCode,
            // VNPay tính theo đơn vị nhỏ nhất: số tiền × 100 (175.000đ -> 17500000).
            ["vnp_Amount"] = (request.Amount * 100).ToString(CultureInfo.InvariantCulture),
            ["vnp_CurrCode"] = CurrencyCode,
            ["vnp_TxnRef"] = request.TxnRef,
            ["vnp_OrderInfo"] = request.OrderInfo,
            ["vnp_OrderType"] = string.IsNullOrWhiteSpace(request.OrderType) ? DefaultOrderType : request.OrderType,
            ["vnp_Locale"] = string.IsNullOrWhiteSpace(request.Locale) ? DefaultLocale : request.Locale,
            ["vnp_ReturnUrl"] = request.ReturnUrl,
            ["vnp_IpAddr"] = request.IpAddress,
            ["vnp_CreateDate"] = createDate,
        };

        if (!string.IsNullOrWhiteSpace(request.BankCode))
        {
            parameters["vnp_BankCode"] = request.BankCode;
        }

        if (request.ExpireDate is { } expireDate)
        {
            parameters["vnp_ExpireDate"] = FormatVnPayTime(expireDate);
        }

        // Chuỗi ký = chuỗi query (khác MoMo: MoMo ký rồi nhét chữ ký vào JSON body). Nhờ vậy URL
        // dựng ra soi được bằng mắt: phần trước vnp_SecureHash chính là dữ liệu đã ký.
        var signingData = BuildSigningData(parameters);

        return $"{_options.PaymentUrl}?{signingData}&vnp_SecureHash={Sign(signingData)}";
    }

    public bool IsValidSignature(IReadOnlyDictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("vnp_SecureHash", out var received) || string.IsNullOrWhiteSpace(received))
        {
            return false;
        }

        // Lọc đúng các tham số cổng (vnp_*), bỏ hai trường chữ ký — tài liệu VNPay: vnp_SecureHashType
        // là di sản của bản 2.0, vẫn có thể xuất hiện trong IPN nên phải bỏ khỏi chuỗi ký.
        var signable = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in parameters)
        {
            if (key.StartsWith("vnp_", StringComparison.Ordinal)
                && !key.Equals("vnp_SecureHash", StringComparison.Ordinal)
                && !key.Equals("vnp_SecureHashType", StringComparison.Ordinal))
            {
                signable[key] = value;
            }
        }

        var expected = Sign(BuildSigningData(signable));

        try
        {
            // So sánh hằng thời gian trên bytes như MoMo — chữ ký là bí mật, so sánh thường để lộ
            // dần qua thời gian phản hồi. Hex chữ HOA/thường đều nhận (Convert.FromHexString).
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expected),
                Convert.FromHexString(received));
        }
        catch (FormatException)
        {
            // Chữ ký không phải chuỗi hex hợp lệ — dữ liệu của người ngoài, không phải lỗi của ta.
            return false;
        }
    }

    /// <summary>
    /// Chuỗi dữ liệu đưa vào HMAC: các cặp <c>key=value</c> đã mã hoá URL, nối bằng '&amp;'. Vì
    /// dictionary là SortedDictionary ordinal nên thứ tự đã đúng chuẩn "xếp theo bảng chữ cái" của
    /// VNPay; đây cũng chính là phần query string của URL trả về.
    /// </summary>
    private static string BuildSigningData(SortedDictionary<string, string> parameters)
    {
        return string.Join(
            "&",
            parameters.Select(parameter => $"{UrlEncode(parameter.Key)}={UrlEncode(parameter.Value)}"));
    }

    /// <summary>HMAC-SHA512 trên chuỗi ký, hex chữ HOA — quy ước chữ ký của VNPay.</summary>
    private string Sign(string signingData)
    {
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(_options.HashSecret));

        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(signingData)));
    }

    /// <summary>Đổi một mốc thời gian về giờ GMT+7 đúng định dạng cổng yêu cầu.</summary>
    private static string FormatVnPayTime(DateTimeOffset moment)
    {
        return moment.ToOffset(VietnamOffset).ToString(VnPayDateFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Mã hoá URL đúng kiểu <c>urlencode()</c> của PHP mà mẫu VNPay dùng — KHÁC
    /// <c>Uri.EscapeDataString</c> ở đúng hai chỗ: dấu cách thành '+' (không phải %20) và '~' thành
    /// %7E (EscapeDataString để nguyên). Sai một trong hai là chữ ký lệch với chuỗi cổng tính lại.
    /// </summary>
    private static string UrlEncode(string value)
    {
        return Uri.EscapeDataString(value).Replace("%20", "+").Replace("~", "%7E");
    }
}
