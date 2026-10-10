using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace SmartBus.Api.Services;

/// <summary>
/// Gửi email qua SMTP bằng MailKit (task dòng 42 — *"Sinh file PDF vé có mã QR + gửi kèm qua
/// email"*, US 4, Sprint 3). Đây là chỗ DUY NHẤT trong dự án biết MailKit tồn tại — tầng soạn thư
/// chỉ làm việc với <see cref="EmailMessage"/> thuần.
///
/// Cấu hình thiếu thì ném NGAY khi dựng service, nêu đúng tên mảnh thiếu (luật 2 — xem
/// <see cref="EmailOptions"/>): lỗi cấu hình phải nổ lúc khởi động chứ không phải lúc hành khách
/// bấm "gửi vé" rồi chờ.
///
/// Kết nối LUÔN mã hoá: STARTTLS (cổng 587) hoặc SSL ngay từ đầu (cổng 465) — không có đường gửi
/// trần, email vé là chứng từ.
/// </summary>
public class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;

    public SmtpEmailSender(IOptions<EmailOptions> options)
    {
        _options = options.Value;

        if (_options.MissingPiece() is { } missing)
        {
            throw new InvalidOperationException(
                $"Chưa cấu hình gửi email: thiếu {missing}. Đặt qua dotnet user-secrets "
                + "hoặc biến môi trường (xem appsettings.Development.json.example) — KHÔNG commit "
                + "giá trị thật lên repo public (luật 2).");
        }
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        // BuildMessage tách riêng (public) để test kiểm được phần "soạn thư MIME" — phần dễ sai
        // (encoding tiêu đề, tên tệp đính kèm, HTML/plain) — mà không cần SMTP thật.
        using var mime = BuildMessage(message);
        using var client = new SmtpClient();

        var socketOptions = _options.UseStartTls
            ? SecureSocketOptions.StartTls
            : SecureSocketOptions.SslOnConnect;

        await client.ConnectAsync(_options.Host, _options.Port, socketOptions, cancellationToken);
        await client.AuthenticateAsync(_options.UserName, _options.Password, cancellationToken);
        await client.SendAsync(mime, cancellationToken);

        // quit: true — chào tạm biệt tử tế để máy chủ đóng phiên sạch, không để lại kết nối treo.
        await client.DisconnectAsync(true, cancellationToken);
    }

    /// <summary>
    /// Dựng thư MIME từ <see cref="EmailMessage"/> — public để test gọi thẳng.
    /// Người gọi sở hữu <see cref="MimeMessage"/> trả về (dùng trong <c>using</c>).
    /// </summary>
    public MimeMessage BuildMessage(EmailMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));

        // Có tên thì gắn tên, không thì để MimeKit tự phân tích địa chỉ trần.
        mime.To.Add(string.IsNullOrWhiteSpace(message.ToName)
            ? MailboxAddress.Parse(message.ToAddress)
            : new MailboxAddress(message.ToName, message.ToAddress));

        mime.Subject = message.Subject;

        var body = new BodyBuilder
        {
            HtmlBody = message.HtmlBody,
            TextBody = message.PlainTextBody,
        };

        foreach (var attachment in message.Attachments)
        {
            body.Attachments.Add(attachment.FileName, attachment.Content, ContentType.Parse(attachment.ContentType));
        }

        mime.Body = body.ToMessageBody();
        return mime;
    }
}
