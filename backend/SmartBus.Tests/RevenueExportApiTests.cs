using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;
using RouteEntity = SmartBus.Api.Entities.Route;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test tích hợp cho API xuất báo cáo doanh thu ra file — GET /api/reports/revenue/export
/// (US 24 "Xuất dữ liệu", Sprint 4 dòng 78 — Phùng Duy Hoàng). Hợp đồng đầy đủ ở mục
/// "Xuất báo cáo doanh thu" của docs/api-contract.md.
///
/// Điểm quan trọng nhất cần ghim: file phải chứa ĐÚNG các bucket của báo cáo JSON (kể cả bucket
/// 0 đồng) vì service xuất cố ý gọi lại <c>IRevenueReportService</c> thay vì tự tính doanh thu.
///
/// Mỗi test dựng một app riêng với CSDL InMemory riêng; helper seed chép lại trong từng lớp test,
/// không có lớp base chung (đúng lệ đang có của dự án).
/// </summary>
public class RevenueExportApiTests
{
    private const string ExportUrl = "/api/reports/revenue/export";

    private const string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private const string PdfContentType = "application/pdf";

    private const int HeaderRow = 4;

    private static readonly string[] ExpectedHeaders = ["Kỳ", "Doanh thu (VND)", "Số giao dịch"];

    private static readonly byte[] ZipMagic = [0x50, 0x4B, 0x03, 0x04];

    // -------------------------------------------------------------- Quyền và xác thực

    [Fact]
    public async Task Khong_gui_token_thi_tra_401()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(ExportUrl)).StatusCode);
    }

    [Fact]
    public async Task Khach_hang_tai_file_tra_403()
    {
        using var factory = new TestAppFactory();
        var passenger = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Hành Khách Test");
        var client = ClientWith(factory, factory.CreateTokenFor(passenger));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(ExportUrl)).StatusCode);
    }

    // -------------------------------------------------------------- Định dạng file

    [Fact]
    public async Task Mac_dinh_la_file_xlsx()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var trip = await SeedTripAsync(factory, route.Id);
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 10_000m, paidAt: new DateTime(2026, 10, 14, 1, 0, 0, DateTimeKind.Utc));

        var response = await client.GetAsync($"{ExportUrl}?granularity=day&from=2026-10-14&to=2026-10-14");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(XlsxContentType, response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Take(ZipMagic.Length).SequenceEqual(ZipMagic), "Nội dung trả về không phải file .xlsx (thiếu magic ZIP).");

        // Tên file ASCII không dấu và có mốc thời gian UTC — hai lần xuất không trùng tên.
        var fileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
        Assert.Matches(@"^bao-cao-doanh-thu_\d{8}-\d{6}Z\.xlsx$", fileName);
    }

    [Fact]
    public async Task Format_pdf_tra_dung_file_pdf()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var trip = await SeedTripAsync(factory, route.Id);
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 25_000m, paidAt: new DateTime(2026, 10, 14, 3, 0, 0, DateTimeKind.Utc));

        var response = await client.GetAsync(
            $"{ExportUrl}?granularity=day&from=2026-10-14&to=2026-10-14&format=pdf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(PdfContentType, response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));

        var fileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
        Assert.Matches(@"^bao-cao-doanh-thu_\d{8}-\d{6}Z\.pdf$", fileName);
    }

    [Fact]
    public async Task Format_la_tra_400_theo_truong_format()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var response = await client.GetAsync($"{ExportUrl}?format=docx");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("format", out _));
    }

    [Fact]
    public async Task Pdf_van_xuat_duoc_khi_ky_khong_co_du_lieu()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        // Kỳ hợp lệ nhưng rỗng vẫn phải ra FILE — bảng rỗng có đầu cột, không phải 404.
        var response = await client.GetAsync(
            $"{ExportUrl}?granularity=day&from=2026-10-14&to=2026-10-14&format=pdf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(PdfContentType, response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
    }

    // -------------------------------------------------------------- Nội dung file

    [Fact]
    public async Task Noi_dung_xlsx_dung_cot_thu_tu_va_kieu_du_lieu()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var trip = await SeedTripAsync(factory, route.Id);
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 10_000m, paidAt: new DateTime(2026, 10, 14, 1, 0, 0, DateTimeKind.Utc));

        var response = await client.GetAsync($"{ExportUrl}?granularity=day&from=2026-10-14&to=2026-10-14");

        using var workbook = await WorkbookAsync(response);
        var sheet = workbook.Worksheet(1);

        // Khối tự mô tả: tiêu đề hàng 1, dòng ngữ cảnh hàng 2, đầu cột hàng 4, dữ liệu từ hàng 5.
        Assert.Equal("BÁO CÁO DOANH THU", sheet.Cell(1, 1).GetString());

        var metadata = sheet.Cell(2, 1).GetString();
        Assert.Contains("Kỳ báo cáo: 2026-10-14 → 2026-10-14", metadata);
        Assert.Contains("Tổng doanh thu: 10.000 VND", metadata);
        Assert.Contains("Người xuất: Quản Lý Test", metadata);

        for (var i = 0; i < ExpectedHeaders.Length; i++)
        {
            Assert.Equal(ExpectedHeaders[i], sheet.Cell(HeaderRow, i + 1).GetString());
        }

        Assert.Equal("2026-10-14", sheet.Cell(HeaderRow + 1, 1).GetString());

        // Doanh thu và số giao dịch là SỐ THẬT để Excel cộng/lọc/sort được — không phải chuỗi.
        var revenueCell = sheet.Cell(HeaderRow + 1, 2);
        Assert.Equal(XLDataType.Number, revenueCell.DataType);
        Assert.Equal(10_000d, revenueCell.GetDouble());
        Assert.Equal("#,##0", revenueCell.Style.NumberFormat.Format);

        var paymentsCell = sheet.Cell(HeaderRow + 1, 3);
        Assert.Equal(XLDataType.Number, paymentsCell.DataType);
        Assert.Equal(1d, paymentsCell.GetDouble());
        Assert.Equal("#,##0", paymentsCell.Style.NumberFormat.Format);
    }

    [Fact]
    public async Task Bucket_khong_phat_sinh_van_duoc_ghi_voi_so_0()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var trip = await SeedTripAsync(factory, route.Id);

        // Giao dịch 08:00 ngày 14 VN — hai ngày rìa phải có mặt với 0 đồng, không bị bỏ khỏi file.
        await SeedPaymentAsync(factory, manager.Id, trip.Id, 30_000m, paidAt: new DateTime(2026, 10, 14, 1, 0, 0, DateTimeKind.Utc));

        var response = await client.GetAsync($"{ExportUrl}?granularity=day&from=2026-10-13&to=2026-10-15");

        using var workbook = await WorkbookAsync(response);
        var sheet = workbook.Worksheet(1);

        Assert.Equal(HeaderRow + 3, sheet.LastRowUsed()!.RowNumber());

        Assert.Equal("2026-10-13", sheet.Cell(5, 1).GetString());
        Assert.Equal(0d, sheet.Cell(5, 2).GetDouble());
        Assert.Equal(0d, sheet.Cell(5, 3).GetDouble());

        Assert.Equal("2026-10-14", sheet.Cell(6, 1).GetString());
        Assert.Equal(30_000d, sheet.Cell(6, 2).GetDouble());
        Assert.Equal(1d, sheet.Cell(6, 3).GetDouble());

        Assert.Equal("2026-10-15", sheet.Cell(7, 1).GetString());
        Assert.Equal(0d, sheet.Cell(7, 2).GetDouble());
    }

    [Fact]
    public async Task Bo_trong_from_to_thi_lay_30_ngay_gan_nhat_nhu_bao_cao_goc()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var response = await client.GetAsync(ExportUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var workbook = await WorkbookAsync(response);
        var sheet = workbook.Worksheet(1);

        // 30 bucket ngày, kể cả ngày không phát sinh — đúng mặc định của GET /reports/revenue.
        Assert.Equal(HeaderRow + 30, sheet.LastRowUsed()!.RowNumber());

        var metadata = sheet.Cell(2, 1).GetString();
        Assert.Contains("Độ gộp: Ngày", metadata);

        var firstBucket = sheet.Cell(HeaderRow + 1, 1).GetString();
        var lastBucket = sheet.Cell(HeaderRow + 30, 1).GetString();
        // Bucket đầu là ngày cũ nhất, bucket cuối là hôm nay → chênh đúng 29 ngày.
        Assert.Equal(29, DateOnly.Parse(lastBucket).DayNumber - DateOnly.Parse(firstBucket).DayNumber);
    }

    // -------------------------------------------------------------- Lỗi từ báo cáo gốc giữ nguyên

    [Fact]
    public async Task Granularity_la_tra_400_theo_truong_granularity()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var response = await client.GetAsync($"{ExportUrl}?granularity=nam");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("granularity", out _));
    }

    [Fact]
    public async Task To_truoc_from_tra_400_o_ca_hai_o_ngay()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var response = await client.GetAsync($"{ExportUrl}?from=2026-10-15&to=2026-10-14");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = body.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("from", out _));
        Assert.True(errors.TryGetProperty("to", out _));
    }

    [Fact]
    public async Task Loc_theo_tuyen_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var response = await client.GetAsync(
            $"{ExportUrl}?granularity=day&from=2026-10-14&to=2026-10-14&routeId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    // =======================================================================================
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // =======================================================================================

    private static HttpClient ClientWith(TestAppFactory factory, string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    private static async Task<XLWorkbook> WorkbookAsync(HttpResponseMessage response)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync();

        return new XLWorkbook(new MemoryStream(bytes));
    }

    private static async Task<UserEntity> SeedUserAsync(
        TestAppFactory factory,
        Guid roleId,
        string roleCode,
        string fullName)
    {
        var role = new Role { Id = roleId, Code = roleCode, Name = roleCode };

        await factory.SeedAsync(db =>
        {
            if (!db.Roles.Any(r => r.Id == role.Id || r.Code == role.Code))
            {
                db.Roles.Add(role);
            }
        });

        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "09" + Random.Shared.Next(10_000_000, 99_999_999),
            FullName = fullName,
            PasswordHash = "hash-khong-dung-toi-trong-test",
            IsActive = true,
            RoleId = role.Id,
            UserRoles = [new UserRole { RoleId = role.Id }],
        };

        await factory.SeedAsync(db => db.Users.Add(user));

        return user;
    }

    private static async Task<RouteEntity> SeedRouteAsync(TestAppFactory factory, string code)
    {
        var route = new RouteEntity
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = $"Tuyến {code}",
            Origin = "Bến Thành",
            Destination = "Chợ Lớn",
        };

        await factory.SeedAsync(db => db.Routes.Add(route));

        return route;
    }

    /// <summary>Chuyến chỉ có khoá ngoại, không gán navigation (đúng lệ "seed dòng mồ côi").</summary>
    private static async Task<Trip> SeedTripAsync(TestAppFactory factory, Guid routeId)
    {
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            BusId = Guid.NewGuid(),
            DepartureTime = Ngay(1),
        };

        await factory.SeedAsync(db => db.Trips.Add(trip));

        return trip;
    }

    private static async Task<Payment> SeedPaymentAsync(
        TestAppFactory factory,
        Guid userId,
        Guid tripId,
        decimal amount,
        PaymentStatus status = PaymentStatus.Success,
        DateTime? paidAt = null,
        DateTime? createdAt = null)
    {
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            PaymentCode = "PM-" + Guid.NewGuid().ToString("N")[..8],
            MethodCode = "MoMo",
            Amount = amount,
            Status = status,
            PaidAt = paidAt,
            CreatedAt = createdAt ?? Ngay(1),
            UserId = userId,
            TripId = tripId,
        };

        await factory.SeedAsync(db => db.Payments.Add(payment));

        return payment;
    }

    /// <summary>Mốc UTC trong quá khứ xa — `day` càng lớn càng gần hiện tại.</summary>
    private static DateTime Ngay(int day)
        => new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc).AddDays(day);
}
