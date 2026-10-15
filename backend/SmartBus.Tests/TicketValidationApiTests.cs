using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;
using RouteEntity = SmartBus.Api.Entities.Route;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test tích hợp cho API soát vé — POST /api/tickets/validate
/// (US 15 "Soát vé QR", Sprint 4 — Trần Trung Hiếu). Hợp đồng đầy đủ ở mục "Soát vé QR" của
/// docs/api-contract.md; luồng ba bước ở docs/41-api-soat-ve-qr.md.
///
/// Mỗi test dựng một app riêng với CSDL InMemory riêng để không lẫn dữ liệu sang nhau.
/// Helper seed chép lại trong từng lớp test, không có lớp base chung (đúng lệ đang có của dự án).
/// Factory phải đặt thêm <c>TicketQr:SigningKey</c> — service ký số chết ngay khi dựng nếu thiếu
/// khoá (docs/30 §3.3), cùng lối TicketQrWiringTests.
///
/// ⚠️ InMemory KHÔNG cưỡng chế unique index — test gửi lại cùng clientScanId ở đây chỉ ghìm được
/// đường đọc-trước của recorder; nhánh đua nằm ở TicketValidationRecorderRaceTests (SQLite).
/// </summary>
public class TicketValidationApiTests
{
    // -------------------------------------------------------------- Quyền và xác thực

    [Fact]
    public async Task Khong_gui_token_tra_401()
    {
        using var factory = NewFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/tickets/validate", new { code = "SBT1:xyz" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Vai_tro_hang_khach_tra_403()
    {
        using var factory = NewFactory();
        var passenger = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Hành Khách Test");
        var client = ClientWith(factory, factory.CreateTokenFor(passenger));

        var response = await client.PostAsJsonAsync("/api/tickets/validate", new { code = "SBT1:xyz" });

        // [Authorize(Roles = Driver,Manager)] — hành khách bị chặn trước khi vào service.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Vai_tro_Manager_duoc_phep_soat_ve()
    {
        using var factory = NewFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var code = await SeedTicketAndSignAsync(factory, TuongLai(1));

        var response = await client.PostAsJsonAsync("/api/tickets/validate", new { code });

        // Dòng 19 chốt "chỉ tài xế/phụ xe" — bốn vai trò không có Phụ xe (A8.4) nên Manager là
        // người soát thứ hai, đúng docs/34 §6.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // -------------------------------------------------------------- Request sai khuôn

    [Fact]
    public async Task Thieu_code_tra_400_kem_errors_code()
    {
        using var factory = NewFactory();
        var driver = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế Test");
        var client = ClientWith(factory, factory.CreateTokenFor(driver));

        var response = await client.PostAsJsonAsync("/api/tickets/validate", new { tripId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("code", out _));
    }

    [Fact]
    public async Task TripId_khong_ton_tai_tra_404()
    {
        using var factory = NewFactory();
        var driver = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế Test");
        var client = ClientWith(factory, factory.CreateTokenFor(driver));
        var code = await SignCodeAsync(factory, Guid.NewGuid());

        var response = await client.PostAsJsonAsync(
            "/api/tickets/validate", new { code, tripId = Guid.NewGuid() });

        // 404 chứ không phải một kết quả soát: chuyến không có thật là lỗi phía gọi
        // (cùng lối POST /vouchers/validate).
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Code_rong_tra_400_kem_errors_code()
    {
        using var factory = NewFactory();
        var driver = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế Test");
        var client = ClientWith(factory, factory.CreateTokenFor(driver));

        var response = await client.PostAsJsonAsync("/api/tickets/validate", new { code = "" });

        // [Required] bắt CẢ chuỗi rỗng/toàn khoảng trắng (RequiredAttribute loại bỏ string trống) —
        // lỗi theo trường "code", payload vĩnh viễn hỏng chứ không phải kết quả soát.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("code", out _));
    }

    [Fact]
    public async Task Code_qua_dai_tra_400_kem_errors_scannedCode()
    {
        using var factory = NewFactory();
        var driver = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế Test");
        var client = ClientWith(factory, factory.CreateTokenFor(driver));

        // 201 ký tự — trần cột ScannedCode varchar(200). [Required] không chặn độ dài nên lượt rơi
        // xuống recorder, nơi chặn với lỗi theo trường "scannedCode" (docs/39 §3.5).
        var response = await client.PostAsJsonAsync(
            "/api/tickets/validate", new { code = new string('A', 201) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("scannedCode", out _));
    }

    [Fact]
    public async Task ScannedAt_o_tuong_lai_tra_400_kem_errors_scannedAt()
    {
        using var factory = NewFactory();
        var driver = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế Test");
        var client = ClientWith(factory, factory.CreateTokenFor(driver));
        var code = await SignCodeAsync(factory, Guid.NewGuid());

        var response = await client.PostAsJsonAsync("/api/tickets/validate", new
        {
            code,
            scannedAt = DateTime.UtcNow.AddMinutes(10),
        });

        // Đồng hồ máy quét nhanh hơn server quá 5 phút — recorder từ chối (docs/34 §4.2).
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("scannedAt", out _));
    }

    // -------------------------------------------------------------- Chữ ký

    [Fact]
    public async Task Ma_rac_tra_NotFound_va_ghi_nhat_ky_nguyen_van()
    {
        using var factory = NewFactory();
        var driver = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế Test");
        var client = ClientWith(factory, factory.CreateTokenFor(driver));

        var response = await client.PostAsJsonAsync("/api/tickets/validate", new { code = "chuoi-rac" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("valid").GetBoolean());
        Assert.Equal("NotFound", body.RootElement.GetProperty("reason").GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("ticket").ValueKind);

        // Mã rác vẫn được ghi nguyên văn để điều tra sau (docs/34 §2.2) — TicketId null cho khớp
        // bất biến "TicketId IS NULL ⟺ NotFound".
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.TicketValidations.SingleAsync();

        Assert.Equal("chuoi-rac", row.ScannedCode);
        Assert.Null(row.TicketId);
        Assert.Equal(TicketValidationResult.NotFound, row.Result);
        Assert.Equal(driver.Id, row.ValidatedByUserId);
    }

    [Fact]
    public async Task Ma_bi_sua_tra_NotFound_khong_phan_biet_voi_ma_rac()
    {
        using var factory = NewFactory();
        var driver = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế Test");
        var client = ClientWith(factory, factory.CreateTokenFor(driver));

        var code = await SeedTicketAndSignAsync(factory, TuongLai(1));

        // Đổi một ký tự trong blob chữ ký — chữ ký lệch ngay dù ticketId còn nguyên vẹn.
        var forged = code[..^1] + (code[^1] == 'A' ? 'B' : 'A');

        var response = await client.PostAsJsonAsync("/api/tickets/validate", new { code = forged });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("valid").GetBoolean());

        // docs/30 §3.2: KHÔNG phân biệt "mã rác" với "vé bị làm giả" ra ngoài — cùng một NotFound,
        // chuỗi làm giả chỉ nằm trong nhật ký.
        Assert.Equal("NotFound", body.RootElement.GetProperty("reason").GetString());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.TicketValidations.SingleAsync();

        Assert.Equal(forged, row.ScannedCode);
        Assert.Null(row.TicketId);
    }

    [Fact]
    public async Task Ma_ky_cho_ve_khong_ton_tai_tra_NotFound()
    {
        using var factory = NewFactory();
        var driver = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế Test");
        var client = ClientWith(factory, factory.CreateTokenFor(driver));

        // Chữ ký KHỚP (do chính server ký) nhưng không có dòng vé nào mang id đó.
        var code = await SignCodeAsync(factory, Guid.NewGuid());

        var response = await client.PostAsJsonAsync("/api/tickets/validate", new { code });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("valid").GetBoolean());
        Assert.Equal("NotFound", body.RootElement.GetProperty("reason").GetString());
    }

    // -------------------------------------------------------------- Kết quả soát theo trạng thái vé

    [Fact]
    public async Task Ve_Paid_dung_chuyen_tra_Valid_kem_day_du_thong_tin_ve()
    {
        using var factory = NewFactory();
        var driver = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế Test");
        var client = ClientWith(factory, factory.CreateTokenFor(driver));

        var route = await SeedRouteAsync(factory, "01");
        var boarding = await SeedStopAsync(factory, "Bến Thành");
        var alighting = await SeedStopAsync(factory, "Chợ Lớn");
        var seat = await SeedSeatAsync(factory, "A12");
        var passenger = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn Khách");
        var departureTime = TuongLai(1);
        var trip = await SeedTripAsync(factory, route.Id, driver.Id, departureTime);
        var ticket = await SeedTicketAsync(
            factory, trip.Id, seat.Id, passenger.Id, TicketStatus.Paid,
            boardingStopId: boarding.Id, alightingStopId: alighting.Id);

        var code = ticket.Code;

        var response = await client.PostAsJsonAsync(
            "/api/tickets/validate", new { code, tripId = trip.Id });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("valid").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("reason").ValueKind);

        // Phần vé khớp đúng bảng trường "ValidatedTicket" của màn quét QR (ticketValidationApi.ts).
        var ticketJson = body.RootElement.GetProperty("ticket");
        Assert.Equal(ticket.Id, ticketJson.GetProperty("id").GetGuid());
        Assert.Equal(code, ticketJson.GetProperty("code").GetString());
        Assert.Equal("01", ticketJson.GetProperty("routeCode").GetString());
        Assert.Equal("Tuyến 01", ticketJson.GetProperty("routeName").GetString());
        Assert.Equal(departureTime, ticketJson.GetProperty("departureTime").GetDateTime());
        Assert.Equal("A12", ticketJson.GetProperty("seatNumber").GetString());
        Assert.Equal("Bến Thành", ticketJson.GetProperty("boardingStopName").GetString());
        Assert.Equal("Chợ Lớn", ticketJson.GetProperty("alightingStopName").GetString());
        Assert.Equal("Nguyễn Văn Khách", ticketJson.GetProperty("passengerName").GetString());
        Assert.Equal("Paid", ticketJson.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, ticketJson.GetProperty("usedAt").ValueKind);

        // Nhật ký ghi đúng một dòng: Valid, đúng vé, đúng chuyến phụ xe đang chạy, đúng người quét.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.TicketValidations.SingleAsync();

        Assert.Equal(TicketValidationResult.Valid, row.Result);
        Assert.Equal(ticket.Id, row.TicketId);
        Assert.Equal(trip.Id, row.TripId);
        Assert.Equal(driver.Id, row.ValidatedByUserId);
        Assert.Equal(code, row.ScannedCode);
    }

    [Fact]
    public async Task Ve_da_huy_tra_Cancelled()
    {
        using var factory = NewFactory();
        var driver = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế Test");
        var client = ClientWith(factory, factory.CreateTokenFor(driver));

        var trip = await SeedTripAsync(factory, routeId: Guid.NewGuid(), driver.Id, TuongLai(1));
        var ticket = await SeedTicketAsync(factory, trip.Id, status: TicketStatus.Cancelled);
        var code = ticket.Code;

        var response = await client.PostAsJsonAsync("/api/tickets/validate", new { code });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("valid").GetBoolean());
        Assert.Equal("Cancelled", body.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Ve_da_dung_tra_AlreadyUsed_kem_usedAt()
    {
        using var factory = NewFactory();
        var driver = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế Test");
        var client = ClientWith(factory, factory.CreateTokenFor(driver));

        var trip = await SeedTripAsync(factory, routeId: Guid.NewGuid(), driver.Id, TuongLai(1));
        var usedAt = new DateTime(2026, 10, 14, 3, 20, 0, DateTimeKind.Utc);
        var ticket = await SeedTicketAsync(factory, trip.Id, status: TicketStatus.Used, usedAt: usedAt);
        var code = ticket.Code;

        var response = await client.PostAsJsonAsync("/api/tickets/validate", new { code });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("valid").GetBoolean());
        Assert.Equal("AlreadyUsed", body.RootElement.GetProperty("reason").GetString());

        // usedAt kèm theo để phụ xe đối chiếu (docs/29 §3).
        var ticketJson = body.RootElement.GetProperty("ticket");
        Assert.Equal(usedAt, ticketJson.GetProperty("usedAt").GetDateTime());
    }

    // -------------------------------------------------------------- Đối chiếu chuyến và hết hạn

    [Fact]
    public async Task Ve_cua_chuyen_khac_tra_WrongTrip()
    {
        using var factory = NewFactory();
        var driver = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế Test");
        var client = ClientWith(factory, factory.CreateTokenFor(driver));

        var tripDangChay = await SeedTripAsync(factory, routeId: Guid.NewGuid(), driver.Id, TuongLai(1));
        var tripKhac = await SeedTripAsync(factory, routeId: Guid.NewGuid(), driver.Id, TuongLai(1));
        var ticket = await SeedTicketAsync(factory, tripKhac.Id);
        var code = ticket.Code;

        var response = await client.PostAsJsonAsync(
            "/api/tickets/validate", new { code, tripId = tripDangChay.Id });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("valid").GetBoolean());
        Assert.Equal("WrongTrip", body.RootElement.GetProperty("reason").GetString());
        Assert.Equal(ticket.Id, body.RootElement.GetProperty("ticket").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Khong_gui_tripId_thi_khong_ket_luan_WrongTrip()
    {
        using var factory = NewFactory();
        var driver = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế Test");
        var client = ClientWith(factory, factory.CreateTokenFor(driver));

        var trip = await SeedTripAsync(factory, routeId: Guid.NewGuid(), driver.Id, TuongLai(1));
        var ticket = await SeedTicketAsync(factory, trip.Id);
        var code = ticket.Code;

        // Không có tripId thì không có gì để đối chiếu — docs/34 §2.5.
        var response = await client.PostAsJsonAsync("/api/tickets/validate", new { code });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("valid").GetBoolean());
    }

    [Fact]
    public async Task Chuyen_da_khoi_hanh_tra_Expired()
    {
        using var factory = NewFactory();
        var driver = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế Test");
        var client = ClientWith(factory, factory.CreateTokenFor(driver));

        var trip = await SeedTripAsync(
            factory, routeId: Guid.NewGuid(), driver.Id, DateTime.UtcNow.AddHours(-1));
        var ticket = await SeedTicketAsync(factory, trip.Id);
        var code = ticket.Code;

        var response = await client.PostAsJsonAsync("/api/tickets/validate", new { code });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("valid").GetBoolean());
        Assert.Equal("Expired", body.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Chuyen_Completed_tra_Expired_du_chua_toi_gio_khoi_hanh()
    {
        using var factory = NewFactory();
        var driver = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế Test");
        var client = ClientWith(factory, factory.CreateTokenFor(driver));

        var trip = await SeedTripAsync(
            factory, routeId: Guid.NewGuid(), driver.Id, TuongLai(1), status: TripStatus.Completed);
        var ticket = await SeedTicketAsync(factory, trip.Id);
        var code = ticket.Code;

        var response = await client.PostAsJsonAsync("/api/tickets/validate", new { code });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("valid").GetBoolean());
        Assert.Equal("Expired", body.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Chuyen_da_huy_tra_Expired_khi_ve_con_Paid()
    {
        using var factory = NewFactory();
        var driver = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế Test");
        var client = ClientWith(factory, factory.CreateTokenFor(driver));

        // Chuyến huỷ mà vé chưa kịp hoàn tiền/huỷ — vé không còn hiệu lực; lý do gần nhất trong
        // tập năm lý do đã chốt là Expired (docs/41 §3.4).
        var trip = await SeedTripAsync(
            factory, routeId: Guid.NewGuid(), driver.Id, TuongLai(1), status: TripStatus.Cancelled);
        var ticket = await SeedTicketAsync(factory, trip.Id);
        var code = ticket.Code;

        var response = await client.PostAsJsonAsync("/api/tickets/validate", new { code });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("valid").GetBoolean());
        Assert.Equal("Expired", body.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Chuyen_chua_khoi_hanh_tra_Valid()
    {
        using var factory = NewFactory();
        var driver = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế Test");
        var client = ClientWith(factory, factory.CreateTokenFor(driver));

        var trip = await SeedTripAsync(factory, routeId: Guid.NewGuid(), driver.Id, TuongLai(1));
        var ticket = await SeedTicketAsync(factory, trip.Id);
        var code = ticket.Code;

        var response = await client.PostAsJsonAsync("/api/tickets/validate", new { code });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("valid").GetBoolean());
    }

    // -------------------------------------------------------------- Hàng đợi ngoại tuyến

    [Fact]
    public async Task Gui_lai_cung_ClientScanId_tra_ket_qua_da_ghi_khong_tinh_lai()
    {
        using var factory = NewFactory();
        var driver = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế Test");
        var client = ClientWith(factory, factory.CreateTokenFor(driver));

        var trip = await SeedTripAsync(factory, routeId: Guid.NewGuid(), driver.Id, TuongLai(1));
        var ticket = await SeedTicketAsync(factory, trip.Id);
        var code = ticket.Code;
        var clientScanId = Guid.NewGuid();

        var payload = new { code, tripId = trip.Id, clientScanId };

        var first = await client.PostAsJsonAsync("/api/tickets/validate", payload);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Sau lượt ghi đầu, vé bị lật sang Used (mô phỏng dòng 16 của Kiên chạy giữa hai lần gửi).
        await factory.SeedAsync(db =>
        {
            var saved = db.Tickets.Single(t => t.Id == ticket.Id);
            saved.Status = TicketStatus.Used;
            saved.UsedAt = DateTime.UtcNow;
        });

        var second = await client.PostAsJsonAsync("/api/tickets/validate", payload);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        // docs/39 §4.2: câu trả lời phải lấy từ dòng ĐÃ GHI (Valid), không phải kết quả tính lại
        // (lúc này sẽ là AlreadyUsed) — hai lần gửi cùng một lượt phải trả về cùng một câu.
        using var body = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("valid").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("reason").ValueKind);

        // Và không đẻ dòng nhật ký thứ hai.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.TicketValidations.ToListAsync();

        Assert.Single(rows);
        Assert.Equal(TicketValidationResult.Valid, rows[0].Result);
    }

    [Fact]
    public async Task Luot_online_khong_ClientScanId_ghi_moi_lan_mot_dong()
    {
        using var factory = NewFactory();
        var driver = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế Test");
        var client = ClientWith(factory, factory.CreateTokenFor(driver));

        var trip = await SeedTripAsync(factory, routeId: Guid.NewGuid(), driver.Id, TuongLai(1));
        var ticket = await SeedTicketAsync(factory, trip.Id);
        var code = ticket.Code;

        await client.PostAsJsonAsync("/api/tickets/validate", new { code });
        await client.PostAsJsonAsync("/api/tickets/validate", new { code });

        // Không có clientScanId thì không có khoá để chống trùng — hai lượt quét thật là hai dòng
        // (docs/39 §4.1). Cả hai đều Valid vì endpoint không lật trạng thái vé — việc của dòng 16.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.TicketValidations.ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal(TicketValidationResult.Valid, row.Result));
    }

    // -------------------------------------------------------------- Nhật ký kiểm toán US 23

    [Fact]
    public async Task Post_tickets_validate_ghi_Create_voi_Target_Validate()
    {
        using var factory = NewFactory();
        var driver = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, "Tài Xế Test");
        var client = ClientWith(factory, factory.CreateTokenFor(driver));

        var code = await SeedTicketAndSignAsync(factory, TuongLai(1));

        var response = await client.PostAsJsonAsync("/api/tickets/validate", new { code });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // ⚠️ GHI NHẬN — Target chỉ là "Validate", KHÔNG phải "TicketValidations:<id>": route không có
        // tham số {id} nên AuditLogMiddleware rơi về đoạn tĩnh cuối (ResolveTableName); response của
        // hợp đồng không có trường "id" ở nóc nên không có id nào để ghép. Giới hạn đã được nhắc
        // trước ở AuditLogMiddleware.ResolveTableName và bảng phủ sóng của AuditLogCoverageTests —
        // nhật ký thật của lượt soát nằm ở bảng TicketValidations, dòng AuditLogs này chỉ chứng
        // minh "có người đã gọi endpoint soát vé lúc nào". Khi nhóm chốt cách xử lý chung cho các
        // endpoint hành động con (extend/release của /seat-holds cũng dính), đổi ở đây và ở
        // AuditLogMiddleware cùng một lúc.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audit = await db.AuditLogs.SingleAsync();

        Assert.Equal(AuditAction.Create, audit.Action);
        Assert.Equal("Validate", audit.Target);
        Assert.Equal(driver.Id, audit.UserId);
    }

    // =======================================================================================
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // =======================================================================================

    /// <summary>
    /// Factory có thêm <c>TicketQr:SigningKey</c> + CSDL InMemory — xem
    /// <see cref="TicketValidationTestFactory"/> giải thích vì sao KHÔNG bọc
    /// <c>TestAppFactory().WithWebHostBuilder(...)</c>: bản bọc không áp dụng ConfigureTestServices
    /// của TestAppFactory, mọi lần seed sẽ đi thẳng vào PostgreSQL thật.
    /// </summary>
    private static TicketValidationTestFactory NewFactory() => new();

    private static HttpClient ClientWith(TicketValidationTestFactory factory, string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    /// <summary>
    /// Tài khoản kèm vai trò. Cố ý KHÔNG gán <c>Role</c> navigation: vai trò lúc phân quyền do
    /// JwtMiddleware nạp lại từ CSDL chứ không đọc từ token; gán navigation còn khiến EF thêm Role
    /// lần nữa vào InMemory (key cố định RoleIds trùng) — cùng lối TripLocationApiTests.
    /// </summary>
    private static async Task<UserEntity> SeedUserAsync(
        TicketValidationTestFactory factory,
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

    private static async Task<RouteEntity> SeedRouteAsync(TicketValidationTestFactory factory, string code)
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

    private static async Task<Stop> SeedStopAsync(TicketValidationTestFactory factory, string name)
    {
        var stop = new Stop
        {
            Id = Guid.NewGuid(),
            Name = name,
            Address = $"Địa chỉ {name}",
            Latitude = 21.0307,
            Longitude = 105.8034,
        };

        await factory.SeedAsync(db => db.Stops.Add(stop));

        return stop;
    }

    private static async Task<Seat> SeedSeatAsync(TicketValidationTestFactory factory, string seatNumber)
    {
        var seat = new Seat
        {
            Id = Guid.NewGuid(),
            BusId = Guid.NewGuid(),
            SeatLayoutId = Guid.NewGuid(),
            SeatNumber = seatNumber,
        };

        await factory.SeedAsync(db => db.Seats.Add(seat));

        return seat;
    }

    /// <summary>
    /// Chuyến chỉ có khoá ngoại, không gán navigation — InMemory không cưỡng chế khoá ngoại nên
    /// không cần seed Route/Bus cho từng chuyến (đúng lệ "seed dòng mồ côi" của AuditLogCoverageTests).
    /// </summary>
    private static async Task<Trip> SeedTripAsync(
        TicketValidationTestFactory factory,
        Guid routeId,
        Guid driverId,
        DateTime departureTime,
        TripStatus status = TripStatus.Scheduled)
    {
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            BusId = Guid.NewGuid(),
            DriverId = driverId,
            Status = status,
            DepartureTime = departureTime,
        };

        await factory.SeedAsync(db => db.Trips.Add(trip));

        return trip;
    }

    /// <summary>
    /// Vé với chuyến trong tương lai + hành khách + ghế tự sinh — cho test chỉ cần "một vé hợp lệ".
    /// Trả về mã QR đã ký. DriverId của chuyến lấy đại một Guid (InMemory không cưỡng chế FK).
    /// </summary>
    private static async Task<string> SeedTicketAndSignAsync(TicketValidationTestFactory factory, DateTime departureTime)
    {
        var trip = await SeedTripAsync(factory, routeId: Guid.NewGuid(), Guid.NewGuid(), departureTime);
        var ticket = await SeedTicketAsync(factory, trip.Id);

        return ticket.Code;
    }

    /// <summary>
    /// Vé mồ côi đủ kiểu, <c>Code</c> = mã ký thật ghi vào cột ngay lúc seed — vé phát hành thật
    /// luôn mang mã ký SẴN trong cột Code (docs/30 §3.1: in QR từ mã ĐÃ LƯU, không sinh lại lúc
    /// hiển thị). Test quét chính <c>ticket.Code</c>, không sinh lại — mỗi lượt sinh ra mã khác.
    /// </summary>
    private static async Task<Ticket> SeedTicketAsync(
        TicketValidationTestFactory factory,
        Guid tripId,
        Guid? seatId = null,
        Guid? userId = null,
        TicketStatus status = TicketStatus.Paid,
        DateTime? usedAt = null,
        Guid? boardingStopId = null,
        Guid? alightingStopId = null)
    {
        var seat = seatId ?? (await SeedSeatAsync(factory, "B" + Random.Shared.Next(1, 99))).Id;
        var passenger = userId ?? (await SeedUserAsync(
            factory, RoleIds.Passenger, RoleCodes.Passenger, "Hành Khách " + Random.Shared.Next(1000))).Id;

        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Code = string.Empty,
            TripId = tripId,
            SeatId = seat,
            UserId = passenger,
            BoardingStopId = boardingStopId,
            AlightingStopId = alightingStopId,
            Price = 12_000,
            Status = status,
            UsedAt = usedAt,
        };

        await factory.SeedAsync(db => db.Tickets.Add(ticket));

        // Vé phát hành thật luôn mang mã ký SẴN trong cột Code (docs/30 §3.1: in QR từ mã ĐÃ LƯU) —
        // test phải bắt chước đúng: ký một mã rồi ghi vào vé, các test quét chính mã này
        // (ticket.Code), không sinh lại vì mỗi lượt sinh ra mã khác.
        var code = await SignCodeAsync(factory, ticket.Id);

        ticket.Code = code;

        await factory.SeedAsync(db =>
        {
            db.Tickets.Single(t => t.Id == ticket.Id).Code = code;
        });

        return ticket;
    }

    private static async Task<string> SignCodeAsync(TicketValidationTestFactory factory, Guid ticketId)
    {
        using var scope = factory.Services.CreateScope();
        var qr = scope.ServiceProvider.GetRequiredService<ITicketQrService>();

        return qr.GenerateCode(ticketId);
    }

    /// <summary>Mốc UTC trong tương lai — `day` càng lớn càng xa hiện tại.</summary>
    private static DateTime TuongLai(int day) => DateTime.UtcNow.AddDays(day);
}
