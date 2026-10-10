using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="ITicketQrService"/> — sinh mã QR duy nhất + ký HMAC-SHA256 chống làm giả
/// (US 4, Sprint 3, task *"Service sinh mã QR duy nhất + ký số chống làm giả"* — Nguyễn Duy Kiên).
///
/// Định dạng mã (chốt với CSDL — cột <c>Tickets.Code</c> varchar(200) duy nhất toàn hệ thống,
/// docs/28 §2/§6):
///
///     SBT1:{ticketId}.{chuỗiBase64}
///     └─5─┘ └──36──┘└1┘└───64───┘   = 106 ký tự (docs/28 ghi ngân sách 90–130, cột cho 200)
///
///   • <c>SBT1:</c> — tiền tố phiên bản định dạng ("SmartBus Ticket v1"). Mã lạ tiền tố bị từ chối
///     thẳng, không đoán mò; sau này đổi cách ký thì mở <c>SBT2:</c>, mã cũ vẫn phân biệt được.
///   • <c>{ticketId}</c> — Guid định dạng "D", chuẩn hoá BẤT KỂ người kiểm đưa vào kiểu nào. Chữ ký
///     gắn cứng vào id này: đổi id mà giữ nguyên chữ ký là chữ ký sập.
///   • blob — Base64Url (RFC 4648 §5, không padding) của <c>nonce 16 byte ‖ chữ ký 32 byte</c>.
///     Nonce ngẫu nhiên mỗi lượt sinh là thứ làm hai lượt gọi cùng một vé ra hai mã khác nhau —
///     đường lùi cho lượt đâm chỉ mục UNIQUE của cột Code: bên phát hành bắt lỗi rồi gọi lại
///     (docs/28 §6), không phải chế thêm gì. Chữ ký nằm TRONG blob nên người kiểm có đủ nonce để
///     dựng lại đúng chuỗi đã ký.
///
/// Chuỗi đem ký là chính mã bỏ phần blob: <c>SBT1:{ticketId:D}.{Base64Url(nonce)}</c> — cùng lối
/// VNPay ký trên chuỗi hiển nhiên soi được bằng mắt.
///
/// Vì sao HMAC đối xứng chứ không phải chữ ký bất đối xứng: bên kiểm là server của mình (API soát
/// vé dòng 43 / Sprint 4 dòng 15 — docs/29), máy quét chỉ gửi chuỗi lên, khoá không bao giờ rời
/// server. Khi nào có máy quét tự kiểm offline không cần mạng thì mới cần bất đối xứng — lúc đó mở
/// tiền tố SBT2, không phá mã cũ.
///
/// Cấu hình thiếu thì ném ngay khi dựng service, nêu đúng tên khoá thiếu (luật 2 — xem
/// <see cref="TicketQrOptions"/>). Service THUẦN: không CSDL, không mạng, không phụ thuộc thời gian —
/// mọi phép sinh/kiểm là hàm thuần trên chuỗi.
/// </summary>
public class TicketQrService : ITicketQrService
{
    /// <summary>Tiền tố phiên bản định dạng — giải thích đầy đủ ở doc của lớp.</summary>
    public const string PayloadPrefix = "SBT1:";

    /// <summary>Dấu phân cách giữa id vé và blob. Guid ("D") lẫn Base64Url đều không chứa dấu này.</summary>
    private const char BlobSeparator = '.';

    /// <summary>Độ dài nonce ngẫu nhiên — 128 bit, thừa sức chống trùng giữa các vé.</summary>
    private const int NonceLength = 16;

    /// <summary>Độ dài chữ ký HMAC-SHA256, tính bằng byte.</summary>
    private const int SignatureLength = 32;

    private readonly TicketQrOptions _options;

    public TicketQrService(IOptions<TicketQrOptions> options)
    {
        _options = options.Value;

        if (_options.MissingPiece() is { } missing)
        {
            throw new InvalidOperationException(
                $"Chưa cấu hình mã QR vé điện tử: thiếu {missing}. Đặt qua dotnet user-secrets "
                + "hoặc biến môi trường (xem appsettings.Development.json.example) — KHÔNG commit "
                + "giá trị thật lên repo public (luật 2).");
        }
    }

    public string GenerateCode(Guid ticketId)
    {
        // Nonce mới mỗi lượt gọi — CHỦ ĐÍCH: hai lượt cho cùng một vé phải ra hai mã khác nhau, để
        // lượt đâm unique index có đường sinh lại (docs/28 §6).
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);

        // Chữ ký trên chuỗi tất định dựng từ id + nonce — bên kiểm dựng lại y nguyên.
        var signature = Sign(BuildSignedPart(ticketId, nonce));

        var blob = new byte[NonceLength + SignatureLength];
        nonce.CopyTo(blob, 0);
        signature.CopyTo(blob, NonceLength);

        // Mã trên đường dây CHỈ có blob: SBT1:{id}.{Base64Url(nonce ‖ chữ ký)} = 106 ký tự.
        // Nonce không hiện riêng ra ngoài — bên kiểm lấy nó từ 16 byte đầu của blob.
        return $"{PayloadPrefix}{ticketId:D}{BlobSeparator}{Base64Url.EncodeToString(blob)}";
    }

    public bool TryVerify(string? code, out Guid ticketId)
    {
        ticketId = Guid.Empty;

        // Máy quét dán kèm khoảng trắng/xuống dòng là chuyện thường — cắt trước khi xét.
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var trimmed = code.Trim();

        if (!trimmed.StartsWith(PayloadPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        // Đúng hai phần: Guid "D" và Base64Url đều KHÔNG chứa dấu '.', nên số phần khác 2 là mã hỏng.
        var parts = trimmed[PayloadPrefix.Length..].Split(BlobSeparator);
        if (parts.Length != 2 || !Guid.TryParse(parts[0], out var parsedId))
        {
            return false;
        }

        byte[] blob;
        try
        {
            blob = Base64Url.DecodeFromChars(parts[1]);
        }
        catch (FormatException)
        {
            // Ký tự ngoài bảng Base64Url — mã rác, không phải lỗi lập trình.
            return false;
        }

        if (blob.Length != NonceLength + SignatureLength)
        {
            return false;
        }

        var signedPart = BuildSignedPart(parsedId, blob.AsSpan(0, NonceLength));
        var expectedSignature = Sign(signedPart);

        // So sánh thời gian hằng: chữ ký là bí mật so từng byte, so sánh thường để lộ dần độ dài
        // tiền tố đúng qua thời gian phản hồi.
        if (!CryptographicOperations.FixedTimeEquals(blob.AsSpan(NonceLength), expectedSignature))
        {
            return false;
        }

        ticketId = parsedId;
        return true;
    }

    /// <summary>
    /// Dựng CHUỖI ĐEM KÝ <c>SBT1:{ticketId:D}.{Base64Url(nonce)}</c> — tất định: cùng vé + cùng
    /// nonce luôn ra cùng chuỗi. Đây KHÔNG phải mã trên đường dây (mã chỉ có blob); nó là mảnh
    /// giữa để bên ký và bên kiểm khớp nhau, và bên kiểm dựng lại được y nguyên từ nonce nằm
    /// trong 16 byte đầu của blob.
    /// </summary>
    private static string BuildSignedPart(Guid ticketId, ReadOnlySpan<byte> nonce)
        => $"{PayloadPrefix}{ticketId:D}{BlobSeparator}{Base64Url.EncodeToString(nonce)}";

    /// <summary>
    /// HMAC-SHA256 trên UTF-8 của chuỗi ký, khoá lấy từ cấu hình — tạo mới mỗi lượt, cùng lối
    /// <see cref="VnPayGatewayService.Sign"/>.
    /// </summary>
    private byte[] Sign(string signingData)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.SigningKey));
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(signingData));
    }
}
