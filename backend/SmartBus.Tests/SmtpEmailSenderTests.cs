using System.Text;
using Microsoft.Extensions.Options;
using MimeKit;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test cho <see cref="SmtpEmailSender"/> — tầng gửi thư SMTP (US 4, Sprint 3, task *"Sinh file PDF
/// vé có mã QR + gửi kèm qua email"* — Phùng Duy Hoàng).
///
/// KHÔNG mở kết nối mạng: chỉ kiểm phần SOẠN thư MIME (<see cref="SmtpEmailSender.BuildMessage"/>)
/// — chỗ dễ sai (mã hoá tiêu đề tiếng Việt, tên/dạng tệp đính kèm, thân HTML + chữ thuần) — và
/// đường fail-fast khi thiếu cấu hình. Phần bắt tay SMTP thật thuộc về môi trường deploy, không
/// phải unit test.
/// </summary>
public class SmtpEmailSenderTests
{
    [Fact]
    public void BuildMessage_dung_nguoi_gui_nguoi_nhan_tieu_de_va_hai_than_thu()
    {
        using var mime = SenderWithDefaults().BuildMessage(SampleMessage());

        var from = Assert.IsType<MailboxAddress>(mime.From[0]);
        Assert.Equal("Smart Bus Ticketing", from.Name);
        Assert.Equal("no-reply@example.com", from.Address);

        var to = Assert.IsType<MailboxAddress>(mime.To[0]);
        Assert.Equal("Nguyễn Văn Hạnh", to.Name);
        Assert.Equal("hanhkhach@example.com", to.Address);

        Assert.Equal("Vé điện tử — Hà Nội — Đà Nẵng", mime.Subject);

        // Vé là thư giao dịch: giữ CẢ hai thân (HTML cho người, chữ thuần cho máy lọc thư rác).
        Assert.Equal("<p>Xin chào</p>", mime.HtmlBody);
        Assert.Equal("Xin chào", mime.TextBody);
    }

    [Fact]
    public void BuildMessage_khong_co_ten_nguoi_nhan_van_ra_dung_dia_chi()
    {
        var message = SampleMessage() with { ToName = null };

        using var mime = SenderWithDefaults().BuildMessage(message);

        var to = Assert.IsType<MailboxAddress>(mime.To[0]);
        Assert.Equal("hanhkhach@example.com", to.Address);
        Assert.True(string.IsNullOrEmpty(to.Name));
    }

    [Fact]
    public void BuildMessage_giu_nguyen_tep_dinh_kem_PDF()
    {
        var content = Encoding.ASCII.GetBytes("%PDF-1.7 gia lap cho test");

        var message = SampleMessage() with
        {
            Attachments =
            [
                new EmailAttachment { FileName = "ve-dien-tu-a1b2c3d4.pdf", Content = content },
            ],
        };

        using var mime = SenderWithDefaults().BuildMessage(message);

        var part = Assert.IsType<MimePart>(Assert.Single(mime.Attachments));
        Assert.Equal("ve-dien-tu-a1b2c3d4.pdf", part.FileName);
        Assert.Equal("application/pdf", part.ContentType.MimeType);

        // Giải mã ngược tệp đính kèm ra đúng từng byte — chuỗi base64 của MIME phải khứ hồi trọn vẹn.
        using var decoded = new MemoryStream();
        part.Content!.DecodeTo(decoded);
        Assert.Equal(content, decoded.ToArray());
    }

    [Fact]
    public void BuildMessage_tieu_de_tieng_Viet_qua_duoc_vong_ghi_doc_MIME()
    {
        // Tiêu đề có dấu phải được mã hoá RFC 2047 khi ghi ra đường dây, và đọc lại ra y nguyên —
        // ghim đúng chỗ hay vỡ nhất khi đổi thư viện.
        using var mime = SenderWithDefaults().BuildMessage(SampleMessage());

        using var stream = new MemoryStream();
        mime.WriteTo(stream);
        stream.Position = 0;

        using var reloaded = MimeMessage.Load(stream);
        Assert.Equal("Vé điện tử — Hà Nội — Đà Nẵng", reloaded.Subject);
        Assert.Equal("Xin chào", reloaded.TextBody);
    }

    // ---- Cấu hình thiếu (luật 2) --------------------------------------------

    [Fact]
    public void Thieu_cau_hinh_thi_nem_ngay_luc_dung_service_va_neu_dung_ten_manh()
    {
        var cases = new (Action<EmailOptions> Xoa, string TenManh)[]
        {
            (o => o.Host = "", "Email:Host"),
            (o => o.Port = 0, "Email:Port"),
            (o => o.Port = 70000, "Email:Port"),
            (o => o.UserName = "", "Email:UserName"),
            (o => o.Password = "", "Email:Password"),
            (o => o.FromAddress = "", "Email:FromAddress"),
        };

        foreach (var (xoa, tenManh) in cases)
        {
            var options = FullOptions();
            xoa(options);

            var ex = Assert.Throws<InvalidOperationException>(
                () => new SmtpEmailSender(Options.Create(options)));

            Assert.Contains(tenManh, ex.Message);
            Assert.Contains("user-secrets", ex.Message);
        }
    }

    [Fact]
    public void Du_cau_hinh_thi_dung_service_khong_nem()
    {
        // Dựng service KHÔNG mở kết nối — kết nối chỉ mở khi gửi thật.
        _ = new SmtpEmailSender(Options.Create(FullOptions()));
    }

    // ---- Dựng dữ liệu -------------------------------------------------------

    private static SmtpEmailSender SenderWithDefaults() => new(Options.Create(FullOptions()));

    private static EmailOptions FullOptions() => new()
    {
        Host = "smtp.example.com",
        Port = 587,
        UserName = "gia-lap@example.com",
        Password = "mat-khau-gia-lap-cua-test",   // giá trị giả — không phải bí mật thật (luật 2)
        FromAddress = "no-reply@example.com",
        FromName = "Smart Bus Ticketing",
    };

    private static EmailMessage SampleMessage() => new()
    {
        ToAddress = "hanhkhach@example.com",
        ToName = "Nguyễn Văn Hạnh",
        Subject = "Vé điện tử — Hà Nội — Đà Nẵng",
        HtmlBody = "<p>Xin chào</p>",
        PlainTextBody = "Xin chào",
    };
}
