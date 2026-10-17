using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SmartBus.Api.Entities;
using RouteEntity = SmartBus.Api.Entities.Route;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test tích hợp cho API đề xuất tăng/giảm tần suất chạy xe theo dữ liệu —
/// GET /api/reports/frequency-proposals (US 20 "Tỷ lệ lấp đầy", Sprint 4 — Phùng Duy Hoàng). Hợp
/// đồng đầy đủ ở mục "Đề xuất tần suất chạy xe — /reports/frequency-proposals" của
/// docs/api-contract.md.
///
/// Mỗi test dựng một app riêng với CSDL InMemory riêng để không lẫn dữ liệu sang nhau. Helper seed
/// chép lại trong từng lớp test, không có lớp base chung (đúng lệ đang có của dự án).
/// </summary>
public class FrequencyProposalApiTests
{
    // -------------------------------------------------------------- Quyền và xác thực

    [Fact]
    public async Task Khong_gui_token_thi_tra_401()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/reports/frequency-proposals")).StatusCode);
    }

    [Fact]
    public async Task Vai_tro_khach_hang_tra_403()
    {
        using var factory = new TestAppFactory();
        var passenger = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Hành Khách Test");
        var client = ClientWith(factory, factory.CreateTokenFor(passenger));

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/reports/frequency-proposals")).StatusCode);
    }

    [Fact]
    public async Task Quan_ly_xem_duoc_de_xuat()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var response = await client.GetAsync("/api/reports/frequency-proposals");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // CSDL trống: vẫn trả đủ 24 giờ, nhưng không có gì để đề xuất.
        Assert.False(body.RootElement.GetProperty("hasEnoughData").GetBoolean());
        Assert.Empty(body.RootElement.GetProperty("increaseHours").EnumerateArray());
        Assert.Empty(body.RootElement.GetProperty("decreaseHours").EnumerateArray());

        var hours = body.RootElement.GetProperty("hours").EnumerateArray().ToArray();
        Assert.Equal(24, hours.Length);
        Assert.Equal(Enumerable.Range(0, 24), hours.Select(h => h.GetProperty("hour").GetInt32()));
        Assert.All(hours, h =>
        {
            Assert.Equal("NoService", h.GetProperty("action").GetString());
            Assert.Equal(JsonValueKind.Null, h.GetProperty("suggestedTripsInPeriod").ValueKind);
            Assert.Equal(JsonValueKind.Null, h.GetProperty("changePercent").ValueKind);
        });
    }

    [Fact]
    public async Task Admin_cung_xem_duoc()
    {
        using var factory = new TestAppFactory();
        var admin = await SeedUserAsync(factory, RoleIds.Admin, RoleCodes.Admin, "Quản Trị Test");
        var client = ClientWith(factory, factory.CreateTokenFor(admin));

        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync("/api/reports/frequency-proposals")).StatusCode);
    }

    // -------------------------------------------------------------- Trục thời gian (giờ Việt Nam)

    [Fact]
    public async Task Gio_tinh_theo_gio_Viet_Nam_khong_phai_gio_UTC()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "20");
        var trip = await SeedTripAsync(
            factory, route.Id, departureTime: new DateTime(2026, 10, 14, 16, 59, 59, DateTimeKind.Utc));
        await SeedTicketsAsync(factory, manager.Id, trip.Id, 1);

        // 16:59:59Z = 23:59:59 giờ Việt Nam ngày 14/10 — phải nằm ở giờ 23, không phải giờ 16 UTC.
        using var body = JsonDocument.Parse(await (await client.GetAsync(
            "/api/reports/frequency-proposals?from=2026-10-14&to=2026-10-15")).Content.ReadAsStringAsync());

        var hour23 = FindHour(body, 23);
        Assert.Equal(1, hour23.GetProperty("passengers").GetInt32());
        Assert.Equal(1, hour23.GetProperty("trips").GetInt32());

        Assert.Equal("NoService", FindHour(body, 16).GetProperty("action").GetString());
    }

    // -------------------------------------------------------------- Quy tắc đề xuất

    [Fact]
    public async Task De_xuat_tang_giam_va_giu_theo_mat_do_tung_gio()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "20");

        // Ví dụ số trong hợp đồng: 100 khách / 20 chuyến → mật độ mục tiêu 5 khách/chuyến.
        var trip3 = await SeedTripsAsync(
            factory, route.Id, new DateTime(2026, 10, 13, 20, 0, 0, DateTimeKind.Utc), 2);   // 03:00 VN, 0 khách
        var trip6 = await SeedTripsAsync(
            factory, route.Id, new DateTime(2026, 10, 13, 23, 0, 0, DateTimeKind.Utc), 6);   // 06:00 VN, 30 khách
        var trip11 = await SeedTripsAsync(
            factory, route.Id, new DateTime(2026, 10, 14, 4, 0, 0, DateTimeKind.Utc), 4);    // 11:00 VN, 10 khách
        var trip17 = await SeedTripsAsync(
            factory, route.Id, new DateTime(2026, 10, 14, 10, 0, 0, DateTimeKind.Utc), 4);   // 17:00 VN, 60 khách
        var trip22 = await SeedTripsAsync(
            factory, route.Id, new DateTime(2026, 10, 14, 15, 0, 0, DateTimeKind.Utc), 4);   // 22:00 VN, 0 khách

        await SeedTicketsAsync(factory, manager.Id, trip6.Id, 30);
        await SeedTicketsAsync(factory, manager.Id, trip11.Id, 10);
        await SeedTicketsAsync(factory, manager.Id, trip17.Id, 60);

        using var body = JsonDocument.Parse(await (await client.GetAsync(
            "/api/reports/frequency-proposals?from=2026-10-14&to=2026-10-14")).Content.ReadAsStringAsync());

        Assert.True(body.RootElement.GetProperty("hasEnoughData").GetBoolean());
        Assert.Equal(1, body.RootElement.GetProperty("periodDays").GetInt32());
        Assert.Equal(100, body.RootElement.GetProperty("totalPassengers").GetInt32());
        Assert.Equal(20, body.RootElement.GetProperty("totalTrips").GetInt32());
        Assert.Equal(5d, body.RootElement.GetProperty("averagePassengersPerTrip").GetDouble());

        Assert.Equal([17], body.RootElement.GetProperty("increaseHours").EnumerateArray()
            .Select(h => h.GetInt32()).ToArray());
        Assert.Equal([3, 11, 22], body.RootElement.GetProperty("decreaseHours").EnumerateArray()
            .Select(h => h.GetInt32()).ToArray());

        // Giờ 3: có chạy nhưng không có khách → đề xuất 0 chuyến, giãn cách hiện tại 30 phút.
        var hour3 = FindHour(body, 3);
        Assert.False(hour3.GetProperty("isPeak").GetBoolean());
        Assert.Equal(2d, hour3.GetProperty("currentTripsPerDay").GetDouble());
        Assert.Equal(30d, hour3.GetProperty("currentFrequencyMinutes").GetDouble());
        Assert.Equal(0, hour3.GetProperty("suggestedTripsInPeriod").GetInt32());
        Assert.Equal(0d, hour3.GetProperty("suggestedTripsPerDay").GetDouble());
        Assert.Equal(JsonValueKind.Null, hour3.GetProperty("suggestedFrequencyMinutes").ValueKind);
        Assert.Equal(-100d, hour3.GetProperty("changePercent").GetDouble());
        Assert.Equal("Decrease", hour3.GetProperty("action").GetString());

        // Giờ 6: mật độ đúng bằng trung bình, là cao điểm → giữ nguyên 6 chuyến, 10 phút.
        var hour6 = FindHour(body, 6);
        Assert.True(hour6.GetProperty("isPeak").GetBoolean());
        Assert.Equal(6, hour6.GetProperty("suggestedTripsInPeriod").GetInt32());
        Assert.Equal(6d, hour6.GetProperty("suggestedTripsPerDay").GetDouble());
        Assert.Equal(10d, hour6.GetProperty("suggestedFrequencyMinutes").GetDouble());
        Assert.Equal(0d, hour6.GetProperty("changePercent").GetDouble());
        Assert.Equal("Keep", hour6.GetProperty("action").GetString());

        // Giờ 11: mật độ 2.5 dưới trung bình → giảm 4 → 2 chuyến, 15 → 30 phút, -50%.
        var hour11 = FindHour(body, 11);
        Assert.Equal(2, hour11.GetProperty("suggestedTripsInPeriod").GetInt32());
        Assert.Equal(30d, hour11.GetProperty("suggestedFrequencyMinutes").GetDouble());
        Assert.Equal(-50d, hour11.GetProperty("changePercent").GetDouble());
        Assert.Equal("Decrease", hour11.GetProperty("action").GetString());

        // Giờ 17: mật độ 15 gấp ba trung bình, cao điểm → tăng 4 → 12 chuyến, 15 → 5 phút, +200%.
        var hour17 = FindHour(body, 17);
        Assert.True(hour17.GetProperty("isPeak").GetBoolean());
        Assert.Equal(12, hour17.GetProperty("suggestedTripsInPeriod").GetInt32());
        Assert.Equal(5d, hour17.GetProperty("suggestedFrequencyMinutes").GetDouble());
        Assert.Equal(200d, hour17.GetProperty("changePercent").GetDouble());
        Assert.Equal("Increase", hour17.GetProperty("action").GetString());

        // Giờ 22: có chạy nhưng không có khách → đề xuất 0 chuyến.
        var hour22 = FindHour(body, 22);
        Assert.Equal(0, hour22.GetProperty("suggestedTripsInPeriod").GetInt32());
        Assert.Equal(JsonValueKind.Null, hour22.GetProperty("suggestedFrequencyMinutes").ValueKind);
        Assert.Equal(-100d, hour22.GetProperty("changePercent").GetDouble());
        Assert.Equal("Decrease", hour22.GetProperty("action").GetString());

        // Giờ không có chuyến: NoService, không đề xuất gì (khác với đề xuất 0 chuyến).
        var hour0 = FindHour(body, 0);
        Assert.Equal("NoService", hour0.GetProperty("action").GetString());
        Assert.Equal(0d, hour0.GetProperty("currentTripsPerDay").GetDouble());
        Assert.Equal(JsonValueKind.Null, hour0.GetProperty("currentFrequencyMinutes").ValueKind);
        Assert.Equal(JsonValueKind.Null, hour0.GetProperty("suggestedTripsInPeriod").ValueKind);
        Assert.Equal(JsonValueKind.Null, hour0.GetProperty("changePercent").ValueKind);
    }

    [Fact]
    public async Task Ca_ky_khong_co_khach_thi_NoData_va_khong_de_xuat()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "20");
        await SeedTripsAsync(factory, route.Id, new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc), 2);   // 07:00 VN

        using var body = JsonDocument.Parse(await (await client.GetAsync(
            "/api/reports/frequency-proposals?from=2026-10-14&to=2026-10-14")).Content.ReadAsStringAsync());

        // Có chuyến nhưng chưa bán được vé nào — dữ liệu rỗng không phải bằng chứng để cắt chuyến.
        Assert.False(body.RootElement.GetProperty("hasEnoughData").GetBoolean());
        Assert.Empty(body.RootElement.GetProperty("increaseHours").EnumerateArray());
        Assert.Empty(body.RootElement.GetProperty("decreaseHours").EnumerateArray());

        var hour7 = FindHour(body, 7);
        Assert.Equal("NoData", hour7.GetProperty("action").GetString());
        Assert.Equal(JsonValueKind.Null, hour7.GetProperty("suggestedTripsInPeriod").ValueKind);
        Assert.Equal(JsonValueKind.Null, hour7.GetProperty("changePercent").ValueKind);

        Assert.Equal("NoService", FindHour(body, 8).GetProperty("action").GetString());
    }

    [Fact]
    public async Task Mat_do_trung_binh_lam_tron_ve_0_thi_NoData()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "20");
        var trip = await SeedTripsAsync(
            factory, route.Id, new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc), 300);   // 07:00 VN
        await SeedTicketsAsync(factory, manager.Id, trip.Id, 1);

        using var body = JsonDocument.Parse(await (await client.GetAsync(
            "/api/reports/frequency-proposals?from=2026-10-14&to=2026-10-14")).Content.ReadAsStringAsync());

        // 1 khách / 300 chuyến = 0.0033 khách/chuyến → làm tròn 2 chữ số bằng 0. Không có mật độ mục
        // tiêu thì phép chia vô nghĩa; báo cáo gốc vẫn có thể đánh dấu cao điểm (0 ≥ 0), nên chốt
        // chặn này nằm ở lớp đề xuất chứ không thể dựa vào isPeak.
        Assert.False(body.RootElement.GetProperty("hasEnoughData").GetBoolean());
        Assert.Equal("NoData", FindHour(body, 7).GetProperty("action").GetString());
        Assert.Equal(JsonValueKind.Null, FindHour(body, 7).GetProperty("suggestedTripsInPeriod").ValueKind);
    }

    [Fact]
    public async Task Gio_co_chuyen_nhung_khong_co_khach_de_xuat_0_chuyen()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "20");
        var trip7 = await SeedTripAsync(
            factory, route.Id, departureTime: new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc));   // 07:00 VN
        var trip8 = await SeedTripAsync(
            factory, route.Id, departureTime: new DateTime(2026, 10, 14, 1, 0, 0, DateTimeKind.Utc));   // 08:00 VN
        await SeedTicketsAsync(factory, manager.Id, trip7.Id, 10);

        using var body = JsonDocument.Parse(await (await client.GetAsync(
            "/api/reports/frequency-proposals?from=2026-10-14&to=2026-10-14")).Content.ReadAsStringAsync());

        // Cả kỳ có dữ liệu (mật độ trung bình 5), nhưng giờ 8 không bán được vé nào → 0 chuyến.
        Assert.True(body.RootElement.GetProperty("hasEnoughData").GetBoolean());
        Assert.Equal("Increase", FindHour(body, 7).GetProperty("action").GetString());

        var hour8 = FindHour(body, 8);
        Assert.Equal("Decrease", hour8.GetProperty("action").GetString());
        Assert.Equal(0, hour8.GetProperty("suggestedTripsInPeriod").GetInt32());
        Assert.Equal(-100d, hour8.GetProperty("changePercent").GetDouble());
        Assert.Equal(JsonValueKind.Null, hour8.GetProperty("suggestedFrequencyMinutes").ValueKind);
    }

    [Fact]
    public async Task So_chuyen_de_xuat_lam_tron_AwayFromZero()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "20");
        var trip7 = await SeedTripsAsync(
            factory, route.Id, new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc), 2);    // 07:00 VN
        var trip8 = await SeedTripsAsync(
            factory, route.Id, new DateTime(2026, 10, 14, 1, 0, 0, DateTimeKind.Utc), 3);    // 08:00 VN
        await SeedTicketsAsync(factory, manager.Id, trip7.Id, 7);
        await SeedTicketsAsync(factory, manager.Id, trip8.Id, 3);

        using var body = JsonDocument.Parse(await (await client.GetAsync(
            "/api/reports/frequency-proposals?from=2026-10-14&to=2026-10-14")).Content.ReadAsStringAsync());

        // Mật độ mục tiêu 10/5 = 2.0. Giờ 7 cao điểm: 7/2 = 3.5 → 4 (AwayFromZero, không phải 3).
        // Giờ 8 thưa: 3/2 = 1.5 → 2, giảm từ 3 → 2 = -33.33%.
        Assert.Equal(4, FindHour(body, 7).GetProperty("suggestedTripsInPeriod").GetInt32());
        Assert.Equal(100d, FindHour(body, 7).GetProperty("changePercent").GetDouble());
        Assert.Equal(2, FindHour(body, 8).GetProperty("suggestedTripsInPeriod").GetInt32());
        Assert.Equal(-33.33d, FindHour(body, 8).GetProperty("changePercent").GetDouble());
    }

    // -------------------------------------------------------------- Quy đổi theo số ngày

    [Fact]
    public async Task Khoang_nhieu_ngay_quy_doi_dung_chuyen_moi_ngay()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "20");
        var trip7 = await SeedTripAsync(
            factory, route.Id, departureTime: new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc));   // 07:00 VN ngày 1
        var trip8 = await SeedTripAsync(
            factory, route.Id, departureTime: new DateTime(2026, 10, 15, 1, 0, 0, DateTimeKind.Utc));   // 08:00 VN ngày 2
        await SeedTicketsAsync(factory, manager.Id, trip8.Id, 2);

        using var body = JsonDocument.Parse(await (await client.GetAsync(
            "/api/reports/frequency-proposals?from=2026-10-14&to=2026-10-15")).Content.ReadAsStringAsync());

        Assert.Equal(2, body.RootElement.GetProperty("periodDays").GetInt32());

        // Giờ 7: 1 chuyến / 2 ngày = 0.5 chuyến/ngày → KHÔNG có giãn cách trong giờ để nói (null).
        var hour7 = FindHour(body, 7);
        Assert.Equal(0.5d, hour7.GetProperty("currentTripsPerDay").GetDouble());
        Assert.Equal(JsonValueKind.Null, hour7.GetProperty("currentFrequencyMinutes").ValueKind);
        Assert.Equal(0, hour7.GetProperty("suggestedTripsInPeriod").GetInt32());
        Assert.Equal(JsonValueKind.Null, hour7.GetProperty("suggestedFrequencyMinutes").ValueKind);
        Assert.Equal("Decrease", hour7.GetProperty("action").GetString());

        // Giờ 8: dưới 1 chuyến/ngày hiện tại nhưng đề xuất 2 chuyến cả kỳ = 1 chuyến/ngày → 60 phút.
        var hour8 = FindHour(body, 8);
        Assert.Equal(0.5d, hour8.GetProperty("currentTripsPerDay").GetDouble());
        Assert.Equal(JsonValueKind.Null, hour8.GetProperty("currentFrequencyMinutes").ValueKind);
        Assert.Equal(2, hour8.GetProperty("suggestedTripsInPeriod").GetInt32());
        Assert.Equal(1d, hour8.GetProperty("suggestedTripsPerDay").GetDouble());
        Assert.Equal(60d, hour8.GetProperty("suggestedFrequencyMinutes").GetDouble());
        Assert.Equal("Increase", hour8.GetProperty("action").GetString());
    }

    [Fact]
    public async Task Bo_trong_tham_so_lay_30_ngay_gan_nhat()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        using var body = JsonDocument.Parse(await (await client.GetAsync(
            "/api/reports/frequency-proposals")).Content.ReadAsStringAsync());

        var from = DateOnly.Parse(body.RootElement.GetProperty("from").GetString()!);
        var to = DateOnly.Parse(body.RootElement.GetProperty("to").GetString()!);

        Assert.Equal(to.AddDays(-29), from);
        Assert.Equal(30, body.RootElement.GetProperty("periodDays").GetInt32());
        Assert.Equal(24, body.RootElement.GetProperty("hours").GetArrayLength());

        var todayVietnam = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        Assert.Equal(todayVietnam, to);
    }

    // -------------------------------------------------------------- Một nguồn số với báo cáo gốc

    [Fact]
    public async Task Bon_so_goc_cua_moi_gio_khop_bao_cao_khung_gio_cao_diem()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var route = await SeedRouteAsync(factory, "20");
        var trip7 = await SeedTripsAsync(
            factory, route.Id, new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc), 2);    // 07:00 VN
        var trip17 = await SeedTripsAsync(
            factory, route.Id, new DateTime(2026, 10, 14, 10, 0, 0, DateTimeKind.Utc), 1);   // 17:00 VN
        await SeedTicketsAsync(factory, manager.Id, trip7.Id, 9);
        await SeedTicketsAsync(factory, manager.Id, trip17.Id, 8);

        const string query = "from=2026-10-14&to=2026-10-14";

        using var proposal = JsonDocument.Parse(await (await client.GetAsync(
            $"/api/reports/frequency-proposals?{query}")).Content.ReadAsStringAsync());
        using var peak = JsonDocument.Parse(await (await client.GetAsync(
            $"/api/reports/peak-hours?{query}")).Content.ReadAsStringAsync());

        // Đẳng thức là phép kiểm THẬT: endpoint đề xuất đọc thẳng báo cáo gốc, không tự đếm lại.
        Assert.Equal(
            peak.RootElement.GetProperty("totalPassengers").GetInt32(),
            proposal.RootElement.GetProperty("totalPassengers").GetInt32());
        Assert.Equal(
            peak.RootElement.GetProperty("totalTrips").GetInt32(),
            proposal.RootElement.GetProperty("totalTrips").GetInt32());
        Assert.Equal(
            peak.RootElement.GetProperty("averagePassengersPerTrip").GetDouble(),
            proposal.RootElement.GetProperty("averagePassengersPerTrip").GetDouble());

        for (var hour = 0; hour < 24; hour++)
        {
            var expected = FindHour(peak, hour);
            var actual = FindHour(proposal, hour);

            Assert.Equal(expected.GetProperty("passengers").GetInt32(), actual.GetProperty("passengers").GetInt32());
            Assert.Equal(expected.GetProperty("trips").GetInt32(), actual.GetProperty("trips").GetInt32());
            Assert.Equal(
                expected.GetProperty("averagePassengersPerTrip").GetDouble(),
                actual.GetProperty("averagePassengersPerTrip").GetDouble());
            Assert.Equal(expected.GetProperty("isPeak").GetBoolean(), actual.GetProperty("isPeak").GetBoolean());
        }
    }

    // -------------------------------------------------------------- Lọc theo tuyến

    [Fact]
    public async Task Loc_theo_tuyen_chi_tinh_chuyen_cua_tuyen_do()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var routeA = await SeedRouteAsync(factory, "20A");
        var routeB = await SeedRouteAsync(factory, "20B");
        var tripA = await SeedTripAsync(
            factory, routeA.Id, departureTime: new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc));
        var tripB = await SeedTripAsync(
            factory, routeB.Id, departureTime: new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc));

        await SeedTicketsAsync(factory, manager.Id, tripA.Id, 2);
        await SeedTicketsAsync(factory, manager.Id, tripB.Id, 4);

        using var body = JsonDocument.Parse(await (await client.GetAsync(
            $"/api/reports/frequency-proposals?from=2026-10-14&to=2026-10-14&routeId={routeA.Id}"))
            .Content.ReadAsStringAsync());

        Assert.Equal(2, body.RootElement.GetProperty("totalPassengers").GetInt32());
        Assert.Equal(1, body.RootElement.GetProperty("totalTrips").GetInt32());
        Assert.Equal(2d, body.RootElement.GetProperty("averagePassengersPerTrip").GetDouble());
    }

    [Fact]
    public async Task Loc_theo_tuyen_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var response = await client.GetAsync(
            $"/api/reports/frequency-proposals?from=2026-10-14&to=2026-10-14&routeId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // -------------------------------------------------------------- Dữ liệu đầu vào

    [Fact]
    public async Task To_truoc_from_tra_400_va_loi_o_ca_hai_o_ngay()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var response = await client.GetAsync(
            "/api/reports/frequency-proposals?from=2026-10-15&to=2026-10-14");

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

        var response = await client.GetAsync(
            "/api/reports/frequency-proposals?from=2025-01-01&to=2026-01-02");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // =======================================================================================
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // =======================================================================================

    private static JsonElement FindHour(JsonDocument body, int hour)
        => body.RootElement.GetProperty("hours").EnumerateArray()
            .Single(h => h.GetProperty("hour").GetInt32() == hour);

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
    private static async Task<Trip> SeedTripAsync(
        TestAppFactory factory,
        Guid routeId,
        DateTime departureTime)
        => await SeedTripsAsync(factory, routeId, departureTime, 1);

    /// <summary>
    /// Nhiều chuyến cùng giờ trong MỘT lần ghi — dùng khi test cần mật độ lớn mà không muốn mở
    /// hàng trăm scope CSDL. Trả chuyến đầu tiên để gắn vé.
    /// </summary>
    private static async Task<Trip> SeedTripsAsync(
        TestAppFactory factory,
        Guid routeId,
        DateTime departureTime,
        int count)
    {
        var trips = Enumerable.Range(0, count).Select(_ => new Trip
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            BusId = Guid.NewGuid(),
            DepartureTime = departureTime,
        }).ToArray();

        await factory.SeedAsync(db => db.Trips.AddRange(trips));

        return trips[0];
    }

    /// <summary>Vé mồ côi kiểu revenue: SeatId là Guid rác — InMemory không kiểm khoá ngoại.</summary>
    private static async Task SeedTicketsAsync(
        TestAppFactory factory,
        Guid userId,
        Guid tripId,
        int count)
    {
        var tickets = Enumerable.Range(0, count).Select(_ => new Ticket
        {
            Id = Guid.NewGuid(),
            Code = "TKT-" + Guid.NewGuid().ToString("N")[..8],
            TripId = tripId,
            SeatId = Guid.NewGuid(),
            UserId = userId,
            Price = 50_000m,
            Status = TicketStatus.Paid,
            CreatedAt = Ngay(1),
        }).ToArray();

        await factory.SeedAsync(db => db.Tickets.AddRange(tickets));
    }

    /// <summary>Mốc UTC trong quá khứ xa — `day` càng lớn càng gần hiện tại.</summary>
    private static DateTime Ngay(int day)
        => new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc).AddDays(day);
}
