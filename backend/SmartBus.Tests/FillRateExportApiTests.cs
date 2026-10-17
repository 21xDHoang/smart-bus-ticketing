using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using SmartBus.Api.Entities;
using RouteEntity = SmartBus.Api.Entities.Route;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test tích hợp cho API xuất báo cáo tỷ lệ lấp đầy ra file —
/// GET /api/reports/fill-rate/export (US 20 "Tỷ lệ lấp đầy", Sprint 4 dòng 78 — Phùng Duy Hoàng).
/// Hợp đồng đầy đủ ở mục "Xuất báo cáo tỷ lệ lấp đầy" của docs/api-contract.md.
///
/// Bốn nhóm điều kiện được ghim ở đây vì chúng là chỗ dễ hiểu sai nhất của báo cáo:
/// 1. Công thức (Paid + Used) / Buses.Capacity, làm tròn 1 chữ số, KHÔNG kẹp 100%.
/// 2. Ngày lọc theo lịch Việt Nam (UTC+7) và khung giờ có biên dưới tính, biên trên không.
/// 3. Hai ngưỡng 80/50 xét trên tỷ lệ ĐÃ làm tròn — đúng con số in trên dòng.
/// 4. Vượt trần thì 400, không bao giờ cắt bớt file.
///
/// Mỗi test dựng một app riêng với CSDL InMemory riêng; helper seed chép lại trong từng lớp test,
/// không có lớp base chung (đúng lệ đang có của dự án).
/// </summary>
public class FillRateExportApiTests
{
    private const string ExportUrl = "/api/reports/fill-rate/export";

    private const string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private const string PdfContentType = "application/pdf";

    private const int HeaderRow = 4;

    private static readonly string[] ExpectedHeaders =
    [
        "Mã chuyến",
        "Tuyến",
        "Giờ khởi hành (VN)",
        "Xe",
        "Sức chứa",
        "Khách",
        "Tỷ lệ lấp đầy (%)",
    ];

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
        var bus = await SeedBusAsync(factory, 40, "51B-123.45");
        await SeedTripAsync(factory, route.Id, bus.Id, GioVietNam(14, 8, 0));

        var response = await client.GetAsync($"{ExportUrl}?from=2026-10-14&to=2026-10-14");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(XlsxContentType, response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(
            bytes.Take(ZipMagic.Length).SequenceEqual(ZipMagic),
            "Nội dung trả về không phải file .xlsx (thiếu magic ZIP).");

        var fileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
        Assert.Matches(@"^bao-cao-ty-le-lap-day_\d{8}-\d{6}Z\.xlsx$", fileName);
    }

    [Fact]
    public async Task Format_pdf_tra_dung_file_pdf()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var bus = await SeedBusAsync(factory, 40, "51B-123.45");
        await SeedTripAsync(factory, route.Id, bus.Id, GioVietNam(14, 8, 0));

        var response = await client.GetAsync(
            $"{ExportUrl}?from=2026-10-14&to=2026-10-14&format=pdf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(PdfContentType, response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));

        var fileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
        Assert.Matches(@"^bao-cao-ty-le-lap-day_\d{8}-\d{6}Z\.pdf$", fileName);
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

    // -------------------------------------------------------------- Tham số lọc

    [Fact]
    public async Task Khung_gio_la_tra_400_theo_truong_timeSlot()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var response = await client.GetAsync($"{ExportUrl}?timeSlot=dem");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("timeSlot", out _));
    }

    [Fact]
    public async Task Khung_gio_dang_so_tra_400_khong_nhan_ten_enum_bang_so()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        // "1" không phải tên khung giờ — không được lọt qua như Enum.TryParse vẫn làm.
        var response = await client.GetAsync($"{ExportUrl}?timeSlot=1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("timeSlot", out _));
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
    public async Task Khoang_thoi_gian_qua_366_ngay_tra_400()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        // 2025-01-01 → 2026-10-14 là 652 ngày, vượt trần 366.
        var response = await client.GetAsync($"{ExportUrl}?from=2025-01-01&to=2026-10-14");

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

        var response = await client.GetAsync($"{ExportUrl}?routeId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    // -------------------------------------------------------------- Nội dung file

    [Fact]
    public async Task Noi_dung_xlsx_dung_cot_thu_tu_va_kieu_du_lieu()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var bus = await SeedBusAsync(factory, 40, "51B-123.45");
        var trip = await SeedTripAsync(factory, route.Id, bus.Id, GioVietNam(14, 8, 15));

        // 2 Paid + 1 Used là khách có chỗ; 1 Cancelled thì không.
        await SeedTicketsAsync(factory, trip.Id, TicketStatus.Paid, TicketStatus.Paid, TicketStatus.Used, TicketStatus.Cancelled);

        var response = await client.GetAsync($"{ExportUrl}?from=2026-10-14&to=2026-10-14");

        using var workbook = await WorkbookAsync(response);
        var sheet = workbook.Worksheet(1);

        Assert.Equal("BÁO CÁO TỶ LỆ LẤP ĐẦY", sheet.Cell(1, 1).GetString());

        var metadata = sheet.Cell(2, 1).GetString();
        Assert.Contains("Kỳ báo cáo: 2026-10-14 → 2026-10-14 (lịch Việt Nam)", metadata);
        Assert.Contains("Tuyến: Tất cả", metadata);
        Assert.Contains("Khung giờ: Tất cả", metadata);
        Assert.Contains("Tổng số chuyến: 1", metadata);
        Assert.Contains("Tỷ lệ trung bình: 7,5%", metadata);
        Assert.Contains("Chuyến đầy (≥ 80%): 0", metadata);
        Assert.Contains("Chuyến trống (< 50%): 1", metadata);
        Assert.Contains("Người xuất: Quản Lý Test", metadata);

        for (var i = 0; i < ExpectedHeaders.Length; i++)
        {
            Assert.Equal(ExpectedHeaders[i], sheet.Cell(HeaderRow, i + 1).GetString());
        }

        var row = HeaderRow + 1;
        Assert.Equal("C19-0815", sheet.Cell(row, 1).GetString());
        Assert.Equal("19 · Tuyến 19", sheet.Cell(row, 2).GetString());

        // Giờ khởi hành là CHUỖI giờ Việt Nam — gán DateTime thì Excel đổi theo múi giờ máy mở file.
        var departureCell = sheet.Cell(row, 3);
        Assert.Equal(XLDataType.Text, departureCell.DataType);
        Assert.Equal("2026-10-14 08:15", departureCell.GetString());

        Assert.Equal("51B-123.45", sheet.Cell(row, 4).GetString());

        // Ba cột số là SỐ THẬT để Excel cộng/lọc/sort được — không phải chuỗi.
        var capacityCell = sheet.Cell(row, 5);
        Assert.Equal(XLDataType.Number, capacityCell.DataType);
        Assert.Equal(40d, capacityCell.GetDouble());
        Assert.Equal("#,##0", capacityCell.Style.NumberFormat.Format);

        var passengersCell = sheet.Cell(row, 6);
        Assert.Equal(XLDataType.Number, passengersCell.DataType);
        Assert.Equal(3d, passengersCell.GetDouble());
        Assert.Equal("#,##0", passengersCell.Style.NumberFormat.Format);

        var fillRateCell = sheet.Cell(row, 7);
        Assert.Equal(XLDataType.Number, fillRateCell.DataType);
        Assert.Equal(7.5d, fillRateCell.GetDouble());
        Assert.Equal("0.0", fillRateCell.Style.NumberFormat.Format);
    }

    [Fact]
    public async Task Ky_rong_van_xuat_file_xlsx_chi_co_dau_cot()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var response = await client.GetAsync($"{ExportUrl}?from=2026-10-14&to=2026-10-14");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var workbook = await WorkbookAsync(response);
        var sheet = workbook.Worksheet(1);

        // Không có chuyến nào vẫn là FILE hợp lệ: đầu cột ở hàng 4, không có dòng dữ liệu.
        Assert.Equal(HeaderRow, sheet.LastRowUsed()!.RowNumber());

        for (var i = 0; i < ExpectedHeaders.Length; i++)
        {
            Assert.Equal(ExpectedHeaders[i], sheet.Cell(HeaderRow, i + 1).GetString());
        }

        var metadata = sheet.Cell(2, 1).GetString();
        Assert.Contains("Tổng số chuyến: 0", metadata);
        Assert.Contains("Tỷ lệ trung bình: 0,0%", metadata);
        Assert.Contains("Chuyến đầy (≥ 80%): 0", metadata);
        Assert.Contains("Chuyến trống (< 50%): 0", metadata);
    }

    [Fact]
    public async Task Pdf_van_xuat_duoc_khi_ky_khong_co_du_lieu()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        // Kỳ hợp lệ nhưng rỗng vẫn phải ra FILE — bảng rỗng có đầu cột, không phải 404.
        var response = await client.GetAsync(
            $"{ExportUrl}?from=2026-10-14&to=2026-10-14&format=pdf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(PdfContentType, response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
    }

    [Fact]
    public async Task Bo_loc_ngay_dung_lich_viet_nam_bien_duoi_tinh_bien_tren_khong()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var bus = await SeedBusAsync(factory, 40, "51B-123.45");

        // Bốn mốc UTC quanh ngày Việt Nam 2026-10-14. Chỉ hai mốc GIỮA được tính.
        await SeedTripAsync(factory, route.Id, bus.Id, GioVietNam(13, 23, 59, 59));
        await SeedTripAsync(factory, route.Id, bus.Id, GioVietNam(14, 0, 0));
        await SeedTripAsync(factory, route.Id, bus.Id, GioVietNam(14, 23, 59, 59));
        await SeedTripAsync(factory, route.Id, bus.Id, GioVietNam(15, 0, 0));

        var response = await client.GetAsync($"{ExportUrl}?from=2026-10-14&to=2026-10-14");

        using var workbook = await WorkbookAsync(response);
        var sheet = workbook.Worksheet(1);

        Assert.Equal(HeaderRow + 2, sheet.LastRowUsed()!.RowNumber());
        Assert.Equal("C19-0000", sheet.Cell(HeaderRow + 1, 1).GetString());
        Assert.Equal("C19-2359", sheet.Cell(HeaderRow + 2, 1).GetString());
        Assert.Contains("Tổng số chuyến: 2", sheet.Cell(2, 1).GetString());
    }

    [Fact]
    public async Task Loc_khung_gio_dung_bien_duoi_tinh_bien_tren_khong()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var bus = await SeedBusAsync(factory, 40, "51B-123.45");

        // early = [05:00, 09:00) giờ Việt Nam.
        await SeedTripAsync(factory, route.Id, bus.Id, GioVietNam(14, 4, 59));
        await SeedTripAsync(factory, route.Id, bus.Id, GioVietNam(14, 5, 0));
        await SeedTripAsync(factory, route.Id, bus.Id, GioVietNam(14, 8, 59));
        await SeedTripAsync(factory, route.Id, bus.Id, GioVietNam(14, 9, 0));

        var response = await client.GetAsync(
            $"{ExportUrl}?from=2026-10-14&to=2026-10-14&timeSlot=early");

        using var workbook = await WorkbookAsync(response);
        var sheet = workbook.Worksheet(1);

        Assert.Equal(HeaderRow + 2, sheet.LastRowUsed()!.RowNumber());
        Assert.Equal("C19-0500", sheet.Cell(HeaderRow + 1, 1).GetString());
        Assert.Equal("C19-0859", sheet.Cell(HeaderRow + 2, 1).GetString());
        Assert.Contains("Khung giờ: early (05:00–09:00)", sheet.Cell(2, 1).GetString());
    }

    [Fact]
    public async Task Loc_theo_tuyen_chi_tra_chuyen_cua_tuyen_do()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route19 = await SeedRouteAsync(factory, "19");
        var route21 = await SeedRouteAsync(factory, "21");
        var bus = await SeedBusAsync(factory, 40, "51B-123.45");

        await SeedTripAsync(factory, route19.Id, bus.Id, GioVietNam(14, 8, 0));
        await SeedTripAsync(factory, route21.Id, bus.Id, GioVietNam(14, 9, 0));

        var response = await client.GetAsync(
            $"{ExportUrl}?from=2026-10-14&to=2026-10-14&routeId={route19.Id}");

        using var workbook = await WorkbookAsync(response);
        var sheet = workbook.Worksheet(1);

        Assert.Equal(HeaderRow + 1, sheet.LastRowUsed()!.RowNumber());
        Assert.Equal("C19-0800", sheet.Cell(HeaderRow + 1, 1).GetString());
        Assert.Equal("19 · Tuyến 19", sheet.Cell(HeaderRow + 1, 2).GetString());
        Assert.Contains("Tuyến: 19 · Tuyến 19", sheet.Cell(2, 1).GetString());
    }

    [Fact]
    public async Task Sap_xep_gio_khoi_hanh_tang_dan_va_trung_gio_theo_Id()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var bus33 = await SeedBusAsync(factory, 33, "51B-000.33");
        var bus11 = await SeedBusAsync(factory, 11, "51B-000.11");
        var bus22 = await SeedBusAsync(factory, 22, "51B-000.22");

        // Cố ý seed lộn xộn. Hai chuyến 09:00 trùng giờ, phân định bằng Id tăng dần.
        await SeedTripAsync(
            factory, route.Id, bus22.Id, GioVietNam(14, 9, 0),
            id: Guid.Parse("00000000-0000-0000-0000-000000000002"));
        await SeedTripAsync(factory, route.Id, bus33.Id, GioVietNam(14, 8, 0));
        await SeedTripAsync(
            factory, route.Id, bus11.Id, GioVietNam(14, 9, 0),
            id: Guid.Parse("00000000-0000-0000-0000-000000000001"));

        var response = await client.GetAsync($"{ExportUrl}?from=2026-10-14&to=2026-10-14");

        using var workbook = await WorkbookAsync(response);
        var sheet = workbook.Worksheet(1);

        Assert.Equal(HeaderRow + 3, sheet.LastRowUsed()!.RowNumber());
        Assert.Equal("C19-0800", sheet.Cell(HeaderRow + 1, 1).GetString());
        Assert.Equal(33d, sheet.Cell(HeaderRow + 1, 5).GetDouble());
        Assert.Equal(11d, sheet.Cell(HeaderRow + 2, 5).GetDouble());
        Assert.Equal(22d, sheet.Cell(HeaderRow + 3, 5).GetDouble());
    }

    [Fact]
    public async Task Bo_trong_from_to_thi_lay_30_ngay_gan_nhat()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var bus = await SeedBusAsync(factory, 40, "51B-123.45");
        await SeedTripAsync(factory, route.Id, bus.Id, DateTime.UtcNow.AddMinutes(-5));

        var response = await client.GetAsync(ExportUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var workbook = await WorkbookAsync(response);
        var sheet = workbook.Worksheet(1);

        Assert.Equal(HeaderRow + 1, sheet.LastRowUsed()!.RowNumber());

        var metadata = sheet.Cell(2, 1).GetString();
        Assert.Contains("Khung giờ: Tất cả", metadata);
        Assert.Contains("Tuyến: Tất cả", metadata);
        Assert.Contains("Tổng số chuyến: 1", metadata);

        // Mặc định 30 ngày TÍNH CẢ hôm nay theo lịch Việt Nam — so khoảng cách thay vì so
        // ngày tuyệt đối để test không đỏ khi chạy qua nửa đêm.
        var range = Regex.Match(
            metadata,
            @"Kỳ báo cáo: (\d{4}-\d{2}-\d{2}) → (\d{4}-\d{2}-\d{2})");

        Assert.True(range.Success, $"Không đọc được khoảng ngày trong dòng mô tả: {metadata}");

        var from = DateOnly.Parse(range.Groups[1].Value);
        var to = DateOnly.Parse(range.Groups[2].Value);
        Assert.Equal(29, to.DayNumber - from.DayNumber);
    }

    // -------------------------------------------------------------- Công thức và tóm tắt

    [Fact]
    public async Task Chi_tinh_ve_Paid_va_Used_chuyen_huy_va_xe_suc_chua_khong_duong_bi_loai()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var normalBus = await SeedBusAsync(factory, 40, "51B-123.45");
        var zeroBus = await SeedBusAsync(factory, 0, "51B-000.00");
        var negativeBus = await SeedBusAsync(factory, -5, "51B-999.99");

        var normalTrip = await SeedTripAsync(factory, route.Id, normalBus.Id, GioVietNam(14, 8, 0));
        await SeedTicketsAsync(factory, normalTrip.Id, TicketStatus.Paid, TicketStatus.Used, TicketStatus.Cancelled);

        await SeedTripAsync(
            factory, route.Id, normalBus.Id, GioVietNam(14, 9, 0), TripStatus.Cancelled);
        await SeedTripAsync(factory, route.Id, zeroBus.Id, GioVietNam(14, 10, 0));
        await SeedTripAsync(factory, route.Id, negativeBus.Id, GioVietNam(14, 11, 0));

        var response = await client.GetAsync($"{ExportUrl}?from=2026-10-14&to=2026-10-14");

        using var workbook = await WorkbookAsync(response);
        var sheet = workbook.Worksheet(1);

        // Chỉ chuyến Scheduled của xe có sức chứa dương còn lại.
        Assert.Equal(HeaderRow + 1, sheet.LastRowUsed()!.RowNumber());
        Assert.Equal("C19-0800", sheet.Cell(HeaderRow + 1, 1).GetString());
        Assert.Equal(2d, sheet.Cell(HeaderRow + 1, 6).GetDouble());
        Assert.Contains("Tổng số chuyến: 1", sheet.Cell(2, 1).GetString());
    }

    [Fact]
    public async Task Ty_le_lam_tron_1_chu_so_va_khong_kep_ve_100()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var smallBus = await SeedBusAsync(factory, 3, "51B-000.03");
        var bigBus = await SeedBusAsync(factory, 10, "51B-000.10");

        // 2/3 = 66,666…% → 66,7 (làm tròn AwayFromZero).
        var smallTrip = await SeedTripAsync(factory, route.Id, smallBus.Id, GioVietNam(14, 8, 0));
        await SeedTicketsAsync(factory, smallTrip.Id, TicketStatus.Paid, TicketStatus.Paid);

        // 15/10 = 150% — bán vượt sức chứa phải LỘ RA, không bị kẹp về 100.
        var bigTrip = await SeedTripAsync(factory, route.Id, bigBus.Id, GioVietNam(14, 9, 0));
        await SeedTicketsAsync(factory, bigTrip.Id, Enumerable.Repeat(TicketStatus.Paid, 15).ToArray());

        var response = await client.GetAsync($"{ExportUrl}?from=2026-10-14&to=2026-10-14");

        using var workbook = await WorkbookAsync(response);
        var sheet = workbook.Worksheet(1);

        Assert.Equal(66.7d, sheet.Cell(HeaderRow + 1, 7).GetDouble());
        Assert.Equal(150d, sheet.Cell(HeaderRow + 2, 7).GetDouble());
    }

    [Fact]
    public async Task Tom_tat_dung_nguong_80_50_tren_ty_le_da_lam_tron()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var busA = await SeedBusAsync(factory, 2_000, "51B-020.00");
        var busB = await SeedBusAsync(factory, 2_000, "51B-020.01");

        // 1599/2000 = 79,95% → làm tròn 80,0 → tính là ĐẦY dù số thô dưới 80.
        var fullTrip = await SeedTripAsync(factory, route.Id, busA.Id, GioVietNam(14, 8, 0));
        await SeedTicketsAsync(factory, fullTrip.Id, Enumerable.Repeat(TicketStatus.Paid, 1_599).ToArray());

        // 999/2000 = 49,95% → làm tròn 50,0 → KHÔNG còn là trống dù số thô dưới 50.
        var emptyTrip = await SeedTripAsync(factory, route.Id, busB.Id, GioVietNam(14, 9, 0));
        await SeedTicketsAsync(factory, emptyTrip.Id, Enumerable.Repeat(TicketStatus.Paid, 999).ToArray());

        var response = await client.GetAsync($"{ExportUrl}?from=2026-10-14&to=2026-10-14");

        using var workbook = await WorkbookAsync(response);
        var sheet = workbook.Worksheet(1);

        Assert.Equal(80d, sheet.Cell(HeaderRow + 1, 7).GetDouble());
        Assert.Equal(50d, sheet.Cell(HeaderRow + 2, 7).GetDouble());

        var metadata = sheet.Cell(2, 1).GetString();
        Assert.Contains("Tỷ lệ trung bình: 65,0%", metadata);
        Assert.Contains("Chuyến đầy (≥ 80%): 1", metadata);
        Assert.Contains("Chuyến trống (< 50%): 0", metadata);
    }

    [Fact]
    public async Task Ty_le_trung_binh_khong_trong_so()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var tinyBus = await SeedBusAsync(factory, 10, "51B-000.10");
        var bigBus = await SeedBusAsync(factory, 100, "51B-001.00");

        // Trung bình CỘNG ĐƠN GIẢN: (100% + 10%) / 2 = 55%, không phải (10+10)/(10+100) = 18,2%.
        var tinyTrip = await SeedTripAsync(factory, route.Id, tinyBus.Id, GioVietNam(14, 8, 0));
        await SeedTicketsAsync(factory, tinyTrip.Id, Enumerable.Repeat(TicketStatus.Paid, 10).ToArray());

        var bigTrip = await SeedTripAsync(factory, route.Id, bigBus.Id, GioVietNam(14, 9, 0));
        await SeedTicketsAsync(factory, bigTrip.Id, Enumerable.Repeat(TicketStatus.Paid, 10).ToArray());

        var response = await client.GetAsync($"{ExportUrl}?from=2026-10-14&to=2026-10-14");

        using var workbook = await WorkbookAsync(response);
        var sheet = workbook.Worksheet(1);

        var metadata = sheet.Cell(2, 1).GetString();
        Assert.Contains("Tỷ lệ trung bình: 55,0%", metadata);
        Assert.Contains("Chuyến đầy (≥ 80%): 1", metadata);
        Assert.Contains("Chuyến trống (< 50%): 1", metadata);
    }

    // -------------------------------------------------------------- Trần số dòng

    [Fact]
    public async Task Vuot_tran_excel_tra_400_o_ca_hai_o_ngay()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var bus = await SeedBusAsync(factory, 40, "51B-123.45");
        await SeedTripsAsync(factory, route.Id, bus.Id, 10_001, GioVietNam(14, 8, 0));

        var response = await client.GetAsync($"{ExportUrl}?from=2026-10-14&to=2026-10-14");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = body.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("from", out _));
        Assert.True(errors.TryGetProperty("to", out _));

        var message = body.RootElement.GetProperty("message").GetString();
        Assert.Contains("vượt trần 10.000", message);
        Assert.Contains("Excel", message);
    }

    [Fact]
    public async Task Vuot_tran_pdf_tra_400_o_ca_hai_o_ngay()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var bus = await SeedBusAsync(factory, 40, "51B-123.45");
        await SeedTripsAsync(factory, route.Id, bus.Id, 2_001, GioVietNam(14, 8, 0));

        var response = await client.GetAsync(
            $"{ExportUrl}?from=2026-10-14&to=2026-10-14&format=pdf");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = body.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("from", out _));
        Assert.True(errors.TryGetProperty("to", out _));

        var message = body.RootElement.GetProperty("message").GetString();
        Assert.Contains("vượt trần 2.000", message);
        Assert.Contains("PDF", message);
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

    private static async Task<Bus> SeedBusAsync(TestAppFactory factory, int capacity, string licensePlate)
    {
        var bus = new Bus
        {
            Id = Guid.NewGuid(),
            LicensePlate = licensePlate,
            BusType = "Xe buýt test",
            Capacity = capacity,
        };

        await factory.SeedAsync(db => db.Buses.Add(bus));

        return bus;
    }

    private static async Task<Trip> SeedTripAsync(
        TestAppFactory factory,
        Guid routeId,
        Guid busId,
        DateTime departureUtc,
        TripStatus status = TripStatus.Scheduled,
        Guid? id = null)
    {
        var trip = new Trip
        {
            Id = id ?? Guid.NewGuid(),
            RouteId = routeId,
            BusId = busId,
            DepartureTime = departureUtc,
            Status = status,
        };

        await factory.SeedAsync(db => db.Trips.Add(trip));

        return trip;
    }

    private static async Task SeedTripsAsync(
        TestAppFactory factory,
        Guid routeId,
        Guid busId,
        int count,
        DateTime departureUtc)
    {
        var trips = Enumerable.Range(0, count)
            .Select(_ => new Trip
            {
                Id = Guid.NewGuid(),
                RouteId = routeId,
                BusId = busId,
                DepartureTime = departureUtc,
                Status = TripStatus.Scheduled,
            })
            .ToList();

        await factory.SeedAsync(db => db.Trips.AddRange(trips));
    }

    private static async Task SeedTicketsAsync(
        TestAppFactory factory,
        Guid tripId,
        params TicketStatus[] statuses)
    {
        var tickets = statuses
            .Select(status => new Ticket
            {
                Id = Guid.NewGuid(),
                Code = "TK-" + Guid.NewGuid().ToString("N")[..8],
                TripId = tripId,
                SeatId = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                Price = 10_000m,
                Status = status,
            })
            .ToList();

        await factory.SeedAsync(db => db.Tickets.AddRange(tickets));
    }

    /// <summary>Mốc giờ Việt Nam (UTC+7) trên ngày 2026-10-<paramref name="day"/> → mốc UTC tương ứng.</summary>
    private static DateTime GioVietNam(int day, int hour, int minute, int second = 0)
        => new DateTime(2026, 10, day, hour, minute, second, DateTimeKind.Utc)
            .AddHours(-7);
}
