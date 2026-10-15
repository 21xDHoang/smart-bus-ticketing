using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Chạy trên app THẬT (TestAppFactory) — ghim dòng đăng ký DI của task dòng 77 Sprint 4 trong
/// Program.cs: xoá <c>AddScoped&lt;IReportPdfService, ReportPdfService&gt;</c> là test này đỏ ngay,
/// không phải chờ tới lúc API xuất file (task của Hoàng) nối vào mới biết.
/// </summary>
public class ReportPdfWiringTests
{
    [Fact]
    public void App_that_resolve_duoc_IReportPdfService()
    {
        using var factory = new TestAppFactory();
        using var scope = factory.Services.CreateScope();

        Assert.IsType<ReportPdfService>(
            scope.ServiceProvider.GetRequiredService<IReportPdfService>());
    }

    [Fact]
    public void App_that_sinh_duoc_PDF_bao_cao_that()
    {
        using var factory = new TestAppFactory();
        using var scope = factory.Services.CreateScope();
        var pdfService = scope.ServiceProvider.GetRequiredService<IReportPdfService>();

        // Đi trọn đường qua cấu hình thật của app: QuestPDF + font Roboto nhúng đều phải sống
        // trong tiến trình test, không phụ thuộc file cạnh file chạy.
        var bytes = pdfService.GeneratePdf(new ReportPdfModel
        {
            Title = "Báo cáo doanh thu",
            LogoSvg =
                "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"48\" height=\"46\">" +
                "<rect width=\"48\" height=\"46\" fill=\"#863bff\"/></svg>",
            ExportedAt = new DateTime(2026, 10, 14, 2, 30, 0, DateTimeKind.Utc),
        });

        Assert.True(bytes.Length > 2000, $"PDF nghi ngờ rỗng: {bytes.Length} byte");
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
    }
}
