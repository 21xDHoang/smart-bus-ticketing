using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Chạy trên app THẬT (TestAppFactory) — ghim khối đăng ký DI của task dòng 42 trong Program.cs:
/// xoá <c>Configure&lt;EmailOptions&gt;</c> hay bất kỳ dòng <c>AddScoped</c> nào là test này đỏ
/// ngay, không phải chờ tới lúc có endpoint mới biết.
///
/// Cấu hình Email truyền bằng <c>UseSetting</c> — bắt buộc, vì <see cref="SmtpEmailSender"/> kiểm
/// cấu hình ngay lúc dựng service (fail-fast). Giá trị giả rõ ràng của test — không phải bí mật
/// thật (luật 2).
/// </summary>
public class TicketPdfEmailWiringTests
{
    [Fact]
    public void App_that_resolve_duoc_ITicketPdfService_va_ITicketEmailService()
    {
        using var factory = NewFactory();
        using var scope = factory.Services.CreateScope();

        Assert.IsType<TicketPdfService>(
            scope.ServiceProvider.GetRequiredService<ITicketPdfService>());
        Assert.IsType<TicketEmailService>(
            scope.ServiceProvider.GetRequiredService<ITicketEmailService>());
    }

    [Fact]
    public void App_that_resolve_duoc_IEmailSender_khi_co_cau_hinh_Email()
    {
        using var factory = NewFactory();
        using var scope = factory.Services.CreateScope();

        // Dựng được = cấu hình đủ 5 mảnh; thiếu mảnh nào là ném ngay tại đây (đúng thiết kế
        // fail-fast — lỗi cấu hình nổ lúc khởi động, không phải lúc hành khách bấm "gửi vé").
        Assert.IsType<SmtpEmailSender>(
            scope.ServiceProvider.GetRequiredService<IEmailSender>());
    }

    [Fact]
    public void App_that_sinh_duoc_PDF_ve_that()
    {
        using var factory = NewFactory();
        using var scope = factory.Services.CreateScope();
        var pdfService = scope.ServiceProvider.GetRequiredService<ITicketPdfService>();

        // Đi trọn đường qua cấu hình thật của app: QuestPDF + font Roboto nhúng + ZXing đều phải
        // sống trong tiến trình test, không phụ thuộc file cạnh file chạy.
        var bytes = pdfService.GeneratePdf(SampleModel());

        Assert.True(bytes.Length > 2000, $"PDF nghi ngờ rỗng: {bytes.Length} byte");
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
    }

    private static WebApplicationFactory<Program> NewFactory()
        => new TestAppFactory().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("TicketQr:SigningKey", "khoa-ky-gia-cua-test-dai-hon-32-ky-tu-abc");

            builder.UseSetting("Email:Host", "smtp.example.com");
            builder.UseSetting("Email:Port", "587");
            builder.UseSetting("Email:UserName", "gia-lap@example.com");
            builder.UseSetting("Email:Password", "mat-khau-gia-lap-cua-test");
            builder.UseSetting("Email:FromAddress", "no-reply@example.com");
        });

    private static TicketPdfModel SampleModel() => new()
    {
        TicketId = Guid.Parse("3f8c1d2e-4b5a-4c6d-8e7f-9012345678ab"),
        Code = $"SBT1:{Guid.Parse("3f8c1d2e-4b5a-4c6d-8e7f-9012345678ab"):D}." + new string('A', 64),
        PassengerName = "Nguyễn Văn Hạnh",
        RouteName = "Hà Nội — Đà Nẵng",
        DepartureTime = new DateTime(2026, 10, 15, 1, 30, 0, DateTimeKind.Utc),
        SeatNumber = "A12",
        Price = 350000m,
    };
}
