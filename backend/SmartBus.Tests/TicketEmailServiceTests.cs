using System.Text;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test cho <see cref="TicketEmailService"/> — soạn thư vé + đính kèm PDF (US 4, Sprint 3, task
/// *"Sinh file PDF vé có mã QR + gửi kèm qua email"* — Phùng Duy Hoàng).
///
/// Hai đồ giả thay hai tầng ngoài: <see cref="ITicketPdfService"/> (đồ giả trả mảng byte đánh dấu)
/// và <see cref="IEmailSender"/> (đồ giả bắt lại <see cref="EmailMessage"/> đã soạn). Nhờ vậy kiểm
/// được nội dung thư mà không cần CSDL, không vẽ PDF thật, không mở SMTP.
/// </summary>
public class TicketEmailServiceTests
{
    [Fact]
    public async Task Gui_ve_dung_tieu_de_tep_dinh_kem_va_dia_chi()
    {
        var pdf = new FakeTicketPdfService();
        var email = new FakeEmailSender();
        var service = new TicketEmailService(pdf, email);
        var model = SampleModel();

        await service.SendTicketAsync(model, "hanhkhach@example.com", "Nguyễn Văn Hạnh");

        var sent = Assert.IsType<EmailMessage>(email.Sent);
        Assert.Equal("hanhkhach@example.com", sent.ToAddress);
        Assert.Equal("Nguyễn Văn Hạnh", sent.ToName);
        Assert.Equal("Vé điện tử — Hà Nội — Đà Nẵng", sent.Subject);

        // Tên tệp ASCII không dấu, 8 ký tự đầu của Guid — và đúng nội dung PDF do tầng vẽ trả về.
        var attachment = Assert.Single(sent.Attachments);
        Assert.Matches(@"^ve-dien-tu-[0-9a-f]{8}\.pdf$", attachment.FileName);
        Assert.Equal(FakeTicketPdfService.PdfBytes, attachment.Content);
        Assert.Equal("application/pdf", attachment.ContentType);
    }

    [Fact]
    public async Task In_dung_ma_QR_da_luu_khong_sinh_lai()
    {
        var pdf = new FakeTicketPdfService();
        var service = new TicketEmailService(pdf, new FakeEmailSender());
        var model = SampleModel();

        await service.SendTicketAsync(model, "hanhkhach@example.com");

        // Service đưa NGUYÊN model xuống tầng vẽ — mã in ra là mã đã lưu trong Tickets.Code
        // (docs/28 §6). Test đỏ nghĩa là ai đó lỡ sinh mã mới ở giữa đường.
        Assert.Same(model, pdf.LastModel);
    }

    [Fact]
    public async Task The_HTML_ma_hoa_moi_gia_tri_nguoi_dung_nhap()
    {
        var model = SampleModel() with
        {
            PassengerName = "<script>alert('x')</script> & \"A\"",
            RouteName = "Tuyến <b>đậm</b>",
        };

        var email = new FakeEmailSender();
        await new TicketEmailService(new FakeTicketPdfService(), email)
            .SendTicketAsync(model, "hanhkhach@example.com");

        var sent = Assert.IsType<EmailMessage>(email.Sent);

        // Không thẻ nào sống sót vào HTML — tên hành khách là chuỗi tự do, dán thẳng là mở đường
        // chèn script vào email gửi cho người khác.
        Assert.DoesNotContain("<script", sent.HtmlBody);
        Assert.Contains("&lt;script&gt;", sent.HtmlBody);
        Assert.Contains("Tuyến &lt;b&gt;đậm&lt;/b&gt;", sent.HtmlBody);
        Assert.Contains("&amp;", sent.HtmlBody);
    }

    [Fact]
    public async Task PDF_hong_thi_khong_mo_phien_gui_thu()
    {
        var email = new FakeEmailSender();
        var service = new TicketEmailService(new FakeTicketPdfService { ThrowOnGenerate = true }, email);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SendTicketAsync(SampleModel(), "hanhkhach@example.com"));

        // Sinh PDF phải chạy TRƯỚC khi mở phiên SMTP — PDF hỏng thì chưa gửi gì cả.
        Assert.Null(email.Sent);
    }

    [Fact]
    public async Task Thieu_du_lieu_phu_thi_bo_dong_trong_ca_hai_than_thu()
    {
        var model = SampleModel() with
        {
            LicensePlate = null,
            BoardingStopName = null,
            AlightingStopName = null,
        };

        var email = new FakeEmailSender();
        await new TicketEmailService(new FakeTicketPdfService(), email)
            .SendTicketAsync(model, "hanhkhach@example.com");

        var sent = Assert.IsType<EmailMessage>(email.Sent);

        Assert.DoesNotContain("Biển số xe", sent.HtmlBody);
        Assert.DoesNotContain("Biển số xe", sent.PlainTextBody!);
        Assert.DoesNotContain("Điểm lên", sent.HtmlBody);

        // Dòng bắt buộc vẫn còn, ở cả hai thân.
        Assert.Contains("Ghế", sent.HtmlBody);
        Assert.Contains("A12", sent.PlainTextBody);
        Assert.Contains(model.RouteName, sent.PlainTextBody);
        Assert.Contains(model.DepartureText, sent.PlainTextBody);
        Assert.Contains(model.TicketId.ToString("D"), sent.HtmlBody);
        Assert.Contains(model.TicketId.ToString("D"), sent.PlainTextBody);
    }

    [Fact]
    public async Task Thieu_dia_chi_nguoi_nhan_thi_nem()
    {
        var email = new FakeEmailSender();
        var service = new TicketEmailService(new FakeTicketPdfService(), email);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.SendTicketAsync(SampleModel(), "   "));

        Assert.Null(email.Sent);
    }

    [Fact]
    public async Task Ve_null_thi_nem()
    {
        var service = new TicketEmailService(new FakeTicketPdfService(), new FakeEmailSender());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.SendTicketAsync(null!, "hanhkhach@example.com"));
    }

    // ---- Đồ giả -------------------------------------------------------------

    private sealed class FakeTicketPdfService : ITicketPdfService
    {
        public static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.7 gia lap cho test");

        public TicketPdfModel? LastModel { get; private set; }

        public bool ThrowOnGenerate { get; init; }

        public byte[] GeneratePdf(TicketPdfModel model)
        {
            if (ThrowOnGenerate)
            {
                throw new InvalidOperationException("PDF hỏng theo kịch bản test");
            }

            LastModel = model;
            return PdfBytes;
        }
    }

    private sealed class FakeEmailSender : IEmailSender
    {
        public EmailMessage? Sent { get; private set; }

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Sent = message;
            return Task.CompletedTask;
        }
    }

    // ---- Dựng dữ liệu -------------------------------------------------------

    private static TicketPdfModel SampleModel() => new()
    {
        TicketId = Guid.Parse("3f8c1d2e-4b5a-4c6d-8e7f-9012345678ab"),
        Code = $"SBT1:{Guid.Parse("3f8c1d2e-4b5a-4c6d-8e7f-9012345678ab"):D}." + new string('A', 64),
        PassengerName = "Nguyễn Văn Hạnh",
        RouteName = "Hà Nội — Đà Nẵng",
        DepartureTime = new DateTime(2026, 10, 15, 1, 30, 0, DateTimeKind.Utc),
        SeatNumber = "A12",
        Price = 350000m,
        LicensePlate = "29B-12345",
        BoardingStopName = "Bến xe Mỹ Đình",
        AlightingStopName = "Bến xe Đà Nẵng",
    };
}
