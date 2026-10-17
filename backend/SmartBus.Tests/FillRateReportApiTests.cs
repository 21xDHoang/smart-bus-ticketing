using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;
using RouteEntity = SmartBus.Api.Entities.Route;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test tích hợp cho GET /api/reports/fill-rate — thống kê tỷ lệ lấp đầy theo chuyến, tuyến, khung
/// giờ (US 20 "Tỷ lệ lấp đầy", Sprint 4 — Nguyễn Duy Kiên). Hợp đồng đầy đủ ở mục "Tỷ lệ lấp đầy —
/// /reports/fill-rate" của docs/api-contract.md.
///
/// Trọng tâm: mẫu số là sức chứa ghế của xe; vé Cancelled bị loại; chuyến 0 khách vẫn hiện; trung
/// bình có trọng số; hai ngưỡng đầy/trống so trên giá trị đã làm tròn; năm khung giờ phủ trọn 24h
/// và cộng khớp tóm tắt; trục ngày là giờ Việt Nam; tổng khách khớp báo cáo khung giờ cao điểm.
///
/// Mỗi test dựng một app riêng với CSDL InMemory riêng; helper seed chép lại trong lớp test,
/// không có lớp base chung (đúng lệ đang có của dự án).
/// </summary>
public class FillRateReportApiTests
{
    private const string Url = "/api/reports/fill-rate";
    private const string Ngay14 = "?from=2026-10-14&to=2026-10-14";

    // -------------------------------------------------------------- Quyền và xác thực

    [Fact]
    public async Task Khong_gui_token_thi_tra_401()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Url)).StatusCode);
    }

    [Fact]
    public async Task Vai_tro_khach_hang_tra_403()
    {
        using var factory = new TestAppFactory();
        var passenger = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Hành Khách Test");
        var client = ClientWith(factory, factory.CreateTokenFor(passenger));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Url)).StatusCode);
    }

    [Fact]
    public async Task Quan_ly_xem_duoc()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Url)).StatusCode);
    }

    // -------------------------------------------------------------- Số liệu từng chuyến

    [Fact]
    public async Task Tinh_ty_le_theo_suc_chua_va_bo_ve_huy()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19", "Bến Thành — Suối Tiên");
        var bus = await SeedBusAsync(factory, "29B-123.45", capacity: 40);
        var trip = await SeedTripAsync(factory, route.Id, bus.Id, Vn(14, 5, 30));

        await SeedTicketsAsync(factory, manager.Id, trip.Id, 2, TicketStatus.Paid);
        await SeedTicketsAsync(factory, manager.Id, trip.Id, 1, TicketStatus.Used);
        await SeedTicketsAsync(factory, manager.Id, trip.Id, 1, TicketStatus.Cancelled);

        var body = await GetJsonAsync(client, Url + Ngay14);
        var rows = body.RootElement.GetProperty("trips").EnumerateArray().ToArray();

        var row = Assert.Single(rows);
        Assert.Equal(trip.Id, row.GetProperty("tripId").GetGuid());
        Assert.Equal("C19-0530", row.GetProperty("tripCode").GetString());
        Assert.Equal(route.Id, row.GetProperty("routeId").GetGuid());
        Assert.Equal("19", row.GetProperty("routeCode").GetString());
        Assert.Equal("Bến Thành — Suối Tiên", row.GetProperty("routeName").GetString());
        Assert.Equal(bus.Id, row.GetProperty("busId").GetGuid());
        Assert.Equal("29B-123.45", row.GetProperty("busPlate").GetString());
        Assert.Equal(40, row.GetProperty("capacity").GetInt32());
        Assert.Equal(3, row.GetProperty("passengers").GetInt32());
        Assert.Equal(7.5m, row.GetProperty("fillRatePct").GetDecimal());

        // Giờ khởi hành trả kèm offset +07:00 — màn hình hiển thị thẳng, không tự đổi múi giờ.
        Assert.Equal("2026-10-14T05:30:00+07:00", row.GetProperty("departureTime").GetString());
    }

    [Fact]
    public async Task Chuyen_khong_co_ve_van_hien_voi_ty_le_0()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var bus = await SeedBusAsync(factory, "29B-123.45", capacity: 40);
        var coKhach = await SeedTripAsync(factory, route.Id, bus.Id, Vn(14, 6, 0));
        var khongKhach = await SeedTripAsync(factory, route.Id, bus.Id, Vn(14, 7, 0));

        await SeedTicketsAsync(factory, manager.Id, coKhach.Id, 10);

        var body = await GetJsonAsync(client, Url + Ngay14);
        var rows = body.RootElement.GetProperty("trips").EnumerateArray().ToArray();

        Assert.Equal(2, rows.Length);

        // Sắp theo giờ khởi hành: chuyến 06:00 (10 khách) trước chuyến 07:00 (0 khách).
        Assert.Equal(coKhach.Id, rows[0].GetProperty("tripId").GetGuid());
        Assert.Equal(25.0m, rows[0].GetProperty("fillRatePct").GetDecimal());

        Assert.Equal(khongKhach.Id, rows[1].GetProperty("tripId").GetGuid());
        Assert.Equal(0, rows[1].GetProperty("passengers").GetInt32());
        Assert.Equal(0m, rows[1].GetProperty("fillRatePct").GetDecimal());

        var summary = body.RootElement.GetProperty("summary");
        Assert.Equal(2, summary.GetProperty("totalTrips").GetInt32());
        Assert.Equal(10, summary.GetProperty("totalPassengers").GetInt32());
        Assert.Equal(80, summary.GetProperty("totalSeats").GetInt32());
        Assert.Equal(12.5m, summary.GetProperty("averageFillRatePct").GetDecimal());
        Assert.Equal(0, summary.GetProperty("fullTrips").GetInt32());
        Assert.Equal(2, summary.GetProperty("emptyTrips").GetInt32());
    }

    [Fact]
    public async Task Ty_le_khong_chan_o_100_khi_ban_vuot_suc_chua()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var bus = await SeedBusAsync(factory, "29B-123.45", capacity: 40);
        var trip = await SeedTripAsync(factory, route.Id, bus.Id, Vn(14, 6, 0));

        await SeedTicketsAsync(factory, manager.Id, trip.Id, 44);

        var body = await GetJsonAsync(client, Url + Ngay14);
        var row = body.RootElement.GetProperty("trips").EnumerateArray().Single();

        // Bán vượt sức chứa là dữ liệu phải lộ ra, không bị cắt về 100.
        Assert.Equal(110.0m, row.GetProperty("fillRatePct").GetDecimal());
    }

    [Fact]
    public async Task Xe_suc_chua_0_tra_ty_le_0_khong_chia_cho_0()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var bus = await SeedBusAsync(factory, "29B-123.45", capacity: 0);
        var trip = await SeedTripAsync(factory, route.Id, bus.Id, Vn(14, 6, 0));

        await SeedTicketsAsync(factory, manager.Id, trip.Id, 5);

        var body = await GetJsonAsync(client, Url + Ngay14);
        var row = body.RootElement.GetProperty("trips").EnumerateArray().Single();

        Assert.Equal(0m, row.GetProperty("fillRatePct").GetDecimal());
        Assert.Equal(0m, body.RootElement.GetProperty("summary")
            .GetProperty("averageFillRatePct").GetDecimal());
    }

    [Fact]
    public async Task Trung_binh_co_trong_so_theo_suc_chua_khong_phai_trung_binh_cong()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var xe40 = await SeedBusAsync(factory, "29B-000.40", capacity: 40);
        var xe60 = await SeedBusAsync(factory, "29B-000.60", capacity: 60);
        var chuyenDay = await SeedTripAsync(factory, route.Id, xe40.Id, Vn(14, 6, 0));
        var chuyenRong = await SeedTripAsync(factory, route.Id, xe60.Id, Vn(14, 7, 0));

        await SeedTicketsAsync(factory, manager.Id, chuyenDay.Id, 40);

        var body = await GetJsonAsync(client, Url + Ngay14);
        var summary = body.RootElement.GetProperty("summary");

        // 40/40 = 100% và 0/60 = 0% → trung bình cộng là 50%, trung bình có trọng số là 40/100 = 40%.
        Assert.Equal(40.0m, summary.GetProperty("averageFillRatePct").GetDecimal());

        var routeRow = body.RootElement.GetProperty("routes").EnumerateArray().Single();
        Assert.Equal(40.0m, routeRow.GetProperty("averageFillRatePct").GetDecimal());
    }

    [Fact]
    public async Task Nguong_day_va_trong_so_tren_gia_tri_da_lam_tron()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var bus = await SeedBusAsync(factory, "29B-123.45", capacity: 100);
        var day = await SeedTripAsync(factory, route.Id, bus.Id, Vn(14, 6, 0));
        var trungTinh = await SeedTripAsync(factory, route.Id, bus.Id, Vn(14, 7, 0));
        var trong = await SeedTripAsync(factory, route.Id, bus.Id, Vn(14, 8, 0));

        await SeedTicketsAsync(factory, manager.Id, day.Id, 80);       // 80.0% → đầy
        await SeedTicketsAsync(factory, manager.Id, trungTinh.Id, 50); // 50.0% → không đầy, không trống
        await SeedTicketsAsync(factory, manager.Id, trong.Id, 49);     // 49.0% → trống

        var body = await GetJsonAsync(client, Url + Ngay14);
        var summary = body.RootElement.GetProperty("summary");

        Assert.Equal(3, summary.GetProperty("totalTrips").GetInt32());
        Assert.Equal(1, summary.GetProperty("fullTrips").GetInt32());
        Assert.Equal(1, summary.GetProperty("emptyTrips").GetInt32());

        var routeRow = body.RootElement.GetProperty("routes").EnumerateArray().Single();
        Assert.Equal(1, routeRow.GetProperty("fullTrips").GetInt32());
        Assert.Equal(1, routeRow.GetProperty("emptyTrips").GetInt32());
    }

    // -------------------------------------------------------------- Gộp theo tuyến

    [Fact]
    public async Task Gop_theo_tuyen_sap_theo_ma_va_cong_khop_tom_tat()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route19 = await SeedRouteAsync(factory, "19", "Bến Thành — Suối Tiên");
        var route01 = await SeedRouteAsync(factory, "01", "Bến Thành — Chợ Lớn");
        var bus40 = await SeedBusAsync(factory, "29B-123.45", capacity: 40);
        var bus50 = await SeedBusAsync(factory, "29B-678.90", capacity: 50);

        var a1 = await SeedTripAsync(factory, route19.Id, bus40.Id, Vn(14, 6, 0));
        var a2 = await SeedTripAsync(factory, route19.Id, bus40.Id, Vn(14, 7, 0));
        var b1 = await SeedTripAsync(factory, route01.Id, bus50.Id, Vn(14, 8, 0));

        await SeedTicketsAsync(factory, manager.Id, a1.Id, 10);
        await SeedTicketsAsync(factory, manager.Id, a2.Id, 20);
        await SeedTicketsAsync(factory, manager.Id, b1.Id, 25);

        var body = await GetJsonAsync(client, Url + Ngay14);
        var routes = body.RootElement.GetProperty("routes").EnumerateArray().ToArray();
        var summary = body.RootElement.GetProperty("summary");

        // Thứ tự cố định theo mã tuyến tăng dần (Ordinal): "01" trước "19".
        Assert.Equal(2, routes.Length);
        Assert.Equal("01", routes[0].GetProperty("routeCode").GetString());
        Assert.Equal(1, routes[0].GetProperty("totalTrips").GetInt32());
        Assert.Equal(25, routes[0].GetProperty("totalPassengers").GetInt32());
        Assert.Equal(50, routes[0].GetProperty("totalSeats").GetInt32());
        Assert.Equal(50.0m, routes[0].GetProperty("averageFillRatePct").GetDecimal());

        Assert.Equal("19", routes[1].GetProperty("routeCode").GetString());
        Assert.Equal(2, routes[1].GetProperty("totalTrips").GetInt32());
        Assert.Equal(30, routes[1].GetProperty("totalPassengers").GetInt32());
        Assert.Equal(80, routes[1].GetProperty("totalSeats").GetInt32());
        Assert.Equal(37.5m, routes[1].GetProperty("averageFillRatePct").GetDecimal());

        // Ba cách nhìn cùng một tập chuyến → cộng tuyến phải ra đúng tóm tắt.
        Assert.Equal(summary.GetProperty("totalTrips").GetInt32(), routes.Sum(r => r.GetProperty("totalTrips").GetInt32()));
        Assert.Equal(summary.GetProperty("totalPassengers").GetInt32(), routes.Sum(r => r.GetProperty("totalPassengers").GetInt32()));
        Assert.Equal(summary.GetProperty("totalSeats").GetInt32(), routes.Sum(r => r.GetProperty("totalSeats").GetInt32()));
        Assert.Equal(3, summary.GetProperty("totalTrips").GetInt32());
        Assert.Equal(55, summary.GetProperty("totalPassengers").GetInt32());
        Assert.Equal(130, summary.GetProperty("totalSeats").GetInt32());
        Assert.Equal(42.3m, summary.GetProperty("averageFillRatePct").GetDecimal());
    }

    // -------------------------------------------------------------- Gộp theo khung giờ

    [Fact]
    public async Task Gop_theo_khung_gio_du_nam_khung_theo_thu_tu_va_dung_bien()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var bus = await SeedBusAsync(factory, "29B-123.45", capacity: 40);

        // Sáu chuyến, mỗi khung đúng một — riêng night có hai (22:00 và 04:59).
        // 08:59 vẫn là early; 09:00 đã sang morning; 17:00 đã sang evening.
        await SeedTripAsync(factory, route.Id, bus.Id, Vn(14, 8, 59));
        await SeedTripAsync(factory, route.Id, bus.Id, Vn(14, 9, 0));
        await SeedTripAsync(factory, route.Id, bus.Id, Vn(14, 13, 0));
        await SeedTripAsync(factory, route.Id, bus.Id, Vn(14, 17, 0));
        await SeedTripAsync(factory, route.Id, bus.Id, Vn(14, 22, 0));
        await SeedTripAsync(factory, route.Id, bus.Id, Vn(14, 4, 59));

        var body = await GetJsonAsync(client, Url + Ngay14);
        var slots = body.RootElement.GetProperty("timeSlots").EnumerateArray().ToArray();

        Assert.Equal(5, slots.Length);
        Assert.Equal(new[] { "early", "morning", "afternoon", "evening", "night" },
            slots.Select(s => s.GetProperty("timeSlot").GetString()).ToArray());
        Assert.Equal(new[] { 5, 9, 13, 17, 22 }, slots.Select(s => s.GetProperty("fromHour").GetInt32()).ToArray());
        Assert.Equal(new[] { 9, 13, 17, 22, 5 }, slots.Select(s => s.GetProperty("toHour").GetInt32()).ToArray());
        Assert.Equal(new[] { 1, 1, 1, 1, 2 }, slots.Select(s => s.GetProperty("totalTrips").GetInt32()).ToArray());

        var summary = body.RootElement.GetProperty("summary");
        Assert.Equal(summary.GetProperty("totalTrips").GetInt32(),
            slots.Sum(s => s.GetProperty("totalTrips").GetInt32()));
        Assert.Equal(summary.GetProperty("totalPassengers").GetInt32(),
            slots.Sum(s => s.GetProperty("totalPassengers").GetInt32()));
        Assert.Equal(summary.GetProperty("totalSeats").GetInt32(),
            slots.Sum(s => s.GetProperty("totalSeats").GetInt32()));
    }

    [Fact]
    public async Task Loc_khung_gio_chi_tra_chuyen_trong_khung_va_van_du_nam_dong()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var bus = await SeedBusAsync(factory, "29B-123.45", capacity: 40);
        var early = await SeedTripAsync(factory, route.Id, bus.Id, Vn(14, 6, 0));
        var morning = await SeedTripAsync(factory, route.Id, bus.Id, Vn(14, 10, 0));

        await SeedTicketsAsync(factory, manager.Id, early.Id, 20);
        await SeedTicketsAsync(factory, manager.Id, morning.Id, 20);

        var body = await GetJsonAsync(client, $"{Url}{Ngay14}&timeSlot=EARLY");
        var rows = body.RootElement.GetProperty("trips").EnumerateArray().ToArray();
        var slots = body.RootElement.GetProperty("timeSlots").EnumerateArray().ToArray();

        // Không phân biệt hoa/thường; chỉ còn chuyến 06:00.
        var row = Assert.Single(rows);
        Assert.Equal(early.Id, row.GetProperty("tripId").GetGuid());

        Assert.Equal(1, body.RootElement.GetProperty("summary").GetProperty("totalTrips").GetInt32());
        Assert.Equal(5, slots.Length);
        Assert.Equal(new[] { 1, 0, 0, 0, 0 }, slots.Select(s => s.GetProperty("totalTrips").GetInt32()).ToArray());
    }

    [Fact]
    public async Task TimeSlot_sai_tra_400_voi_loi_o_truong_timeSlot()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var response = await client.GetAsync($"{Url}{Ngay14}&timeSlot=noon");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("timeSlot", out _));
    }

    // -------------------------------------------------------------- Lọc tuyến

    [Fact]
    public async Task Loc_theo_tuyen_chi_tra_chuyen_cua_tuyen_do()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var routeA = await SeedRouteAsync(factory, "19");
        var routeB = await SeedRouteAsync(factory, "01");
        var bus = await SeedBusAsync(factory, "29B-123.45");
        await SeedTripAsync(factory, routeA.Id, bus.Id, Vn(14, 6, 0));
        await SeedTripAsync(factory, routeB.Id, bus.Id, Vn(14, 7, 0));

        var body = await GetJsonAsync(client, $"{Url}{Ngay14}&routeId={routeB.Id}");
        var rows = body.RootElement.GetProperty("trips").EnumerateArray().ToArray();

        var row = Assert.Single(rows);
        Assert.Equal(routeB.Id, row.GetProperty("routeId").GetGuid());
        Assert.Equal(1, body.RootElement.GetProperty("summary").GetProperty("totalTrips").GetInt32());
    }

    [Fact]
    public async Task Tuyen_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var response = await client.GetAsync($"{Url}{Ngay14}&routeId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Không tìm thấy tuyến đường.", body.RootElement.GetProperty("message").GetString());
    }

    // -------------------------------------------------------------- Trục thời gian giờ Việt Nam

    [Fact]
    public async Task Bien_ngay_theo_gio_Viet_Nam_chu_khong_phai_UTC()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var bus = await SeedBusAsync(factory, "29B-123.45");

        // Ngày báo cáo duy nhất: 14/10/2026 (giờ VN) = [13/10 17:00Z, 14/10 17:00Z).
        await SeedTripAsync(factory, route.Id, bus.Id, Utc(2026, 10, 13, 16, 59, 59)); // 23:59:59 ngày 13 VN — ngoài
        await SeedTripAsync(factory, route.Id, bus.Id, Utc(2026, 10, 13, 17, 0, 0));   // 00:00 ngày 14 VN — trong
        await SeedTripAsync(factory, route.Id, bus.Id, Utc(2026, 10, 14, 16, 59, 59)); // 23:59:59 ngày 14 VN — trong
        await SeedTripAsync(factory, route.Id, bus.Id, Utc(2026, 10, 14, 17, 0, 0));   // 00:00 ngày 15 VN — ngoài

        var body = await GetJsonAsync(client, Url + Ngay14);

        Assert.Equal(2, body.RootElement.GetProperty("summary").GetProperty("totalTrips").GetInt32());
    }

    [Fact]
    public async Task Bo_trong_from_to_lay_30_ngay_gan_nhat()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var body = await GetJsonAsync(client, Url);

        var from = DateOnly.Parse(body.RootElement.GetProperty("from").GetString()!);
        var to = DateOnly.Parse(body.RootElement.GetProperty("to").GetString()!);

        Assert.Equal(to.AddDays(-29), from);

        var todayVietnam = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        Assert.Equal(todayVietnam, to);
    }

    // -------------------------------------------------------------- Dữ liệu đầu vào

    [Fact]
    public async Task To_truoc_from_tra_400_va_loi_o_ca_hai_o_ngay()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var response = await client.GetAsync($"{Url}?from=2026-10-15&to=2026-10-14");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = body.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("from", out _));
        Assert.True(errors.TryGetProperty("to", out _));
    }

    [Fact]
    public async Task Khoang_qua_366_ngay_tra_400()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        // 367 ngày — vượt trần 366 đúng một ngày (2025-01-01 → 2026-01-02).
        var response = await client.GetAsync($"{Url}?from=2025-01-01&to=2026-01-02");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Ky_khong_co_chuyen_tra_tom_tat_0_va_du_nam_khung_gio()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var body = await GetJsonAsync(client, Url + Ngay14);
        var summary = body.RootElement.GetProperty("summary");

        Assert.Equal(0, summary.GetProperty("totalTrips").GetInt32());
        Assert.Equal(0, summary.GetProperty("totalPassengers").GetInt32());
        Assert.Equal(0m, summary.GetProperty("averageFillRatePct").GetDecimal());
        Assert.Empty(body.RootElement.GetProperty("trips").EnumerateArray());
        Assert.Empty(body.RootElement.GetProperty("routes").EnumerateArray());

        var slots = body.RootElement.GetProperty("timeSlots").EnumerateArray().ToArray();
        Assert.Equal(5, slots.Length);
        Assert.All(slots, s => Assert.Equal(0, s.GetProperty("totalTrips").GetInt32()));
    }

    // -------------------------------------------------------------- Đối chiếu báo cáo khung giờ

    [Fact]
    public async Task Tong_khach_khop_bao_cao_khung_gio_cao_diem()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "19");
        var bus = await SeedBusAsync(factory, "29B-123.45");
        var trip1 = await SeedTripAsync(factory, route.Id, bus.Id, Vn(14, 6, 0));
        var trip2 = await SeedTripAsync(factory, route.Id, bus.Id, Vn(14, 18, 0));

        await SeedTicketsAsync(factory, manager.Id, trip1.Id, 12);
        await SeedTicketsAsync(factory, manager.Id, trip2.Id, 7);
        await SeedTicketsAsync(factory, manager.Id, trip2.Id, 3, TicketStatus.Cancelled);

        var fillRate = await GetJsonAsync(client, Url + Ngay14);
        var peakHours = await GetJsonAsync(client, "/api/reports/peak-hours" + Ngay14);

        // Cùng định nghĩa hành khách (Paid + Used, bỏ Cancelled) trên cùng khoảng ngày.
        Assert.Equal(
            peakHours.RootElement.GetProperty("totalPassengers").GetInt32(),
            fillRate.RootElement.GetProperty("summary").GetProperty("totalPassengers").GetInt32());
    }

    // =======================================================================================
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // =======================================================================================

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string url)
        => JsonDocument.Parse(await (await client.GetAsync(url)).Content.ReadAsStringAsync());

    private static HttpClient ClientWith(TestAppFactory factory, string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
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

    private static async Task<RouteEntity> SeedRouteAsync(
        TestAppFactory factory,
        string code,
        string? name = null)
    {
        var route = new RouteEntity
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name ?? $"Tuyến {code}",
            Origin = "Bến Thành",
            Destination = "Chợ Lớn",
        };

        await factory.SeedAsync(db => db.Routes.Add(route));

        return route;
    }

    private static async Task<Bus> SeedBusAsync(
        TestAppFactory factory,
        string licensePlate,
        int capacity = 45)
    {
        var bus = new Bus
        {
            Id = Guid.NewGuid(),
            LicensePlate = licensePlate,
            BusType = "Xe buýt 45 chỗ",
            Capacity = capacity,
        };

        await factory.SeedAsync(db => db.Buses.Add(bus));

        return bus;
    }

    /// <summary>Chuyến chỉ có khoá ngoại, không gán navigation (đúng lệ "seed dòng mồ côi").</summary>
    private static async Task<Trip> SeedTripAsync(
        TestAppFactory factory,
        Guid routeId,
        Guid busId,
        DateTime departureUtc)
    {
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            BusId = busId,
            DepartureTime = departureUtc,
        };

        await factory.SeedAsync(db => db.Trips.Add(trip));

        return trip;
    }

    /// <summary>Thêm <paramref name="count"/> vé trong MỘT lần lưu — tránh 80 lượt SaveChanges ở test ngưỡng.</summary>
    private static async Task SeedTicketsAsync(
        TestAppFactory factory,
        Guid userId,
        Guid tripId,
        int count,
        TicketStatus status = TicketStatus.Paid)
    {
        if (count == 0)
        {
            return;
        }

        await factory.SeedAsync(db =>
        {
            for (var i = 0; i < count; i++)
            {
                db.Tickets.Add(new Ticket
                {
                    Id = Guid.NewGuid(),
                    Code = "TK-" + Guid.NewGuid().ToString("N")[..12],
                    TripId = tripId,
                    SeatId = Guid.NewGuid(),
                    UserId = userId,
                    Price = 10_000m,
                    Status = status,
                    UsedAt = status == TicketStatus.Used ? Ngay14Utc : null,
                });
            }
        });
    }

    /// <summary>Mốc UTC tương ứng một giờ Việt Nam (tháng 10/2026).</summary>
    private static DateTime Vn(int day, int hour, int minute = 0)
        => new DateTimeOffset(
                new DateTime(2026, 10, day, hour, minute, 0, DateTimeKind.Unspecified),
                TimeSpan.FromHours(7))
            .UtcDateTime;

    private static DateTime Utc(int year, int month, int day, int hour, int minute, int second)
        => new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);

    private static readonly DateTime Ngay14Utc = Utc(2026, 10, 14, 1, 0, 0);
}
