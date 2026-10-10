using System.Net;
using System.Text;

namespace SmartBus.Api.Services;

/// <summary>
/// Soạn thư "vé điện tử" và gửi kèm PDF (task dòng 42 — *"Sinh file PDF vé có mã QR + gửi kèm qua
/// email"*, US 4, Sprint 3).
///
/// Ghép hai mảnh: <see cref="ITicketPdfService"/> (vẽ vé) + <see cref="IEmailSender"/> (gửi thư).
/// Chỉ có logic SOẠN nội dung ở đây — không SMTP, không vẽ PDF, nên test được bằng hai đồ giả.
///
/// Mọi giá trị lấy từ dữ liệu người dùng nhập (tên hành khách, tên tuyến, biển số…) đều đi qua
/// <see cref="WebUtility.HtmlEncode"/> trước khi vào thân HTML: tên hành khách là chuỗi tự do, dán
/// thẳng vào HTML là mở đường chèn thẻ/chèn script vào email gửi cho người khác.
///
/// Hỏng thì NÉM RA (không nuốt): bên gọi — luồng phát hành vé — bọc try/catch để email hỏng không
/// làm hỏng việc phát hành (docs/31 §5).
/// </summary>
public class TicketEmailService : ITicketEmailService
{
    private readonly ITicketPdfService _pdfService;
    private readonly IEmailSender _emailSender;

    public TicketEmailService(ITicketPdfService pdfService, IEmailSender emailSender)
    {
        _pdfService = pdfService;
        _emailSender = emailSender;
    }

    public async Task SendTicketAsync(
        TicketPdfModel ticket,
        string toAddress,
        string? toName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentException.ThrowIfNullOrWhiteSpace(toAddress);

        // Sinh PDF trước khi dựng thư: PDF hỏng thì ném ngay, không mở phiên SMTP rồi mới vỡ.
        var pdf = _pdfService.GeneratePdf(ticket);

        var message = new EmailMessage
        {
            ToAddress = toAddress,
            ToName = toName,
            Subject = $"Vé điện tử — {ticket.RouteName}",
            HtmlBody = BuildHtmlBody(ticket),
            PlainTextBody = BuildPlainTextBody(ticket),
            Attachments =
            [
                new EmailAttachment
                {
                    FileName = BuildAttachmentFileName(ticket.TicketId),
                    Content = pdf,
                },
            ],
        };

        await _emailSender.SendAsync(message, cancellationToken);
    }

    /// <summary>
    /// Tên tệp đính kèm CỐ Ý không dấu: "ve-dien-tu-a1b2c3d4.pdf". Tên tệp có dấu bị một số ứng
    /// dụng thư và hệ điều hành cũ hiển thị/lưu sai; 8 ký tự đầu của Guid đủ phân biệt vé trong
    /// cùng một hộp thư.
    /// </summary>
    private static string BuildAttachmentFileName(Guid ticketId)
        => $"ve-dien-tu-{ticketId.ToString("N")[..8]}.pdf";

    private static string BuildHtmlBody(TicketPdfModel ticket)
    {
        var rows = new StringBuilder();
        AppendHtmlRow(rows, "Tuyến", ticket.RouteName);
        AppendHtmlRow(rows, "Khởi hành", ticket.DepartureText);
        AppendHtmlRow(rows, "Ghế", ticket.SeatNumber);

        if (!string.IsNullOrWhiteSpace(ticket.LicensePlate))
        {
            AppendHtmlRow(rows, "Biển số xe", ticket.LicensePlate);
        }

        if (!string.IsNullOrWhiteSpace(ticket.BoardingStopName))
        {
            AppendHtmlRow(rows, "Điểm lên", ticket.BoardingStopName);
        }

        if (!string.IsNullOrWhiteSpace(ticket.AlightingStopName))
        {
            AppendHtmlRow(rows, "Điểm xuống", ticket.AlightingStopName);
        }

        AppendHtmlRow(rows, "Giá vé", ticket.PriceText);

        return new StringBuilder()
            .Append("<div style=\"font-family: Arial, Helvetica, sans-serif; font-size: 14px; color: #222222;\">")
            .Append("<p>Xin chào <b>").Append(Html(ticket.PassengerName)).Append("</b>,</p>")
            .Append("<p>Cảm ơn bạn đã đặt vé tại <b>Smart Bus Ticketing</b>. Vé điện tử của bạn đã được phát hành ")
            .Append("và đính kèm trong email này dưới dạng tệp PDF.</p>")
            .Append("<table cellpadding=\"6\" style=\"border-collapse: collapse;\">").Append(rows).Append("</table>")
            .Append("<p><b>Xuất trình mã QR trong tệp PDF khi lên xe.</b></p>")
            .Append("<p style=\"color: #666666; font-size: 12px;\">Mã vé tra cứu: ").Append(ticket.TicketId.ToString("D")).Append("</p>")
            .Append("</div>")
            .ToString();
    }

    private static string BuildPlainTextBody(TicketPdfModel ticket)
    {
        var text = new StringBuilder()
            .Append("Xin chào ").Append(ticket.PassengerName).AppendLine(",")
            .AppendLine()
            .AppendLine("Cảm ơn bạn đã đặt vé tại Smart Bus Ticketing. Vé điện tử (PDF, kèm mã QR) được đính kèm trong email này.")
            .AppendLine()
            .Append("Tuyến: ").AppendLine(ticket.RouteName)
            .Append("Khởi hành: ").AppendLine(ticket.DepartureText)
            .Append("Ghế: ").AppendLine(ticket.SeatNumber);

        if (!string.IsNullOrWhiteSpace(ticket.LicensePlate))
        {
            text.Append("Biển số xe: ").AppendLine(ticket.LicensePlate);
        }

        if (!string.IsNullOrWhiteSpace(ticket.BoardingStopName))
        {
            text.Append("Điểm lên: ").AppendLine(ticket.BoardingStopName);
        }

        if (!string.IsNullOrWhiteSpace(ticket.AlightingStopName))
        {
            text.Append("Điểm xuống: ").AppendLine(ticket.AlightingStopName);
        }

        return text
            .Append("Giá vé: ").AppendLine(ticket.PriceText)
            .AppendLine()
            .AppendLine("Xuất trình mã QR trong tệp PDF khi lên xe.")
            .Append("Mã vé tra cứu: ").Append(ticket.TicketId.ToString("D"))
            .ToString();
    }

    private static void AppendHtmlRow(StringBuilder sb, string label, string value)
        => sb.Append("<tr><td style=\"color: #666666;\">").Append(Html(label))
            .Append("</td><td><b>").Append(Html(value)).Append("</b></td></tr>");

    /// <summary>Bọc mọi giá trị chèn vào HTML — xem doc lớp.</summary>
    private static string Html(string value) => WebUtility.HtmlEncode(value);
}
