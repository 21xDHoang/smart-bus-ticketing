using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SmartBus.Api.Entities;
using RouteEntity = SmartBus.Api.Entities.Route;

namespace SmartBus.Tests;

/// <summary>
/// Test cho <c>GET /api/routes/suggestions</c> — sửa lỗi trải nghiệm màn "Tra cứu tuyến"
/// (Sprint 5, Vàng Thị Dăm). Hợp đồng đầy đủ ở mục "Gợi ý tuyến cho ngày đã chọn —
/// /routes/suggestions" của docs/api-contract.md.
///
/// Điểm khác cốt lõi so với <c>/routes/search</c>: <b>ngày không lọc tuyến</b>. Tuyến nối được
/// hai trạm luôn có mặt trong <c>matches</c>, kèm lịch chạy và ngày gần nhất — nhờ vậy màn hình
/// không rơi vào ô "Không tìm thấy tuyến phù hợp" chỉ vì ngày đó chưa có chuyến.
///
/// Mỗi lớp test tự chép helper — đúng lệ đang có của dự án (không có lớp base chung).
/// </summary>
public class RouteSuggestionsApiTests
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    // =======================================================================================
    // 1. Công khai
    // =======================================================================================

    [Fact]
    public async Task Khong_token_van_goi_duoc()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var today = VietnamToday();
        var (origin, destination) = await SeedRouteWithTwoStopsAsync(factory);

        var response = await client.GetAsync(Url(origin.Id, destination.Id, today));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // =======================================================================================
    // 2. Ngày không có chuyến — vẫn trả tuyến, kèm ngày gần nhất
    // =======================================================================================

    /// <summary>
    /// Chuyến có vào hôm nay và hôm nay + 2, KHÔNG có vào hôm nay + 1. Hành khách chọn ngày
    /// thiếu chuyến: tuyến vẫn phải nằm trong <c>matches</c> (không phải mảng rỗng), kèm ngày gần
    /// nhất. Hai ngày cách đều (hôm nay và +2) — hợp đồng chốt chọn ngày <b>tương lai</b>.
    /// </summary>
    [Fact]
    public async Task Ngay_khong_co_chuyen_van_tra_tuyen_va_chon_ngay_tuong_lai_gan_nhat()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var today = VietnamToday();
        var (origin, destination, route, bus) = await SeedRouteWithTwoStopsAndBusAsync(factory);

        await SeedTripAsync(factory, route.Id, bus.Id, today, hour: 8);
        await SeedTripAsync(factory, route.Id, bus.Id, today.AddDays(2), hour: 9);

        var selected = today.AddDays(1);
        var body = await ReadJsonAsync(await client.GetAsync(Url(origin.Id, destination.Id, selected)));

        var match = Assert.Single(body.GetProperty("matches").EnumerateArray());

        Assert.Equal("01", match.GetProperty("routeCode").GetString());
        Assert.False(match.GetProperty("hasTripsOnDate").GetBoolean());
        Assert.False(match.GetProperty("runsDaily").GetBoolean());

        Assert.Equal(
            [Iso(today), Iso(today.AddDays(2))],
            match.GetProperty("availableDates").EnumerateArray()
                .Select(day => day.GetString()));

        Assert.Equal(Iso(today.AddDays(2)), match.GetProperty("nearestAvailableDate").GetString());
        Assert.Equal(
            new DateTimeOffset(today.AddDays(2).ToDateTime(new TimeOnly(9, 0)), VietnamOffset),
            match.GetProperty("nextDeparture").GetDateTimeOffset());
        Assert.Equal(1, match.GetProperty("departureCountOnNearestDate").GetInt32());

        Assert.Equal("noTripsThatDay", body.GetProperty("emptyReason").GetString());
        Assert.Equal(Iso(today.AddDays(6)), body.GetProperty("bookingHorizonEnd").GetString());
    }

    /// <summary>
    /// Ngày vượt quá lịch bán vé (background job chỉ sinh trước 7 ngày): vẫn trả tuyến, nhưng mã
    /// lý do là <c>dateBeyondHorizon</c> để màn hình báo "Lịch bán vé hiện đến …" thay vì kết
    /// luận tuyến không tồn tại.
    /// </summary>
    [Fact]
    public async Task Ngay_ngoai_tam_sinh_chuyen_tra_tuyen_kem_dateBeyondHorizon()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var today = VietnamToday();
        var (origin, destination, route, bus) = await SeedRouteWithTwoStopsAndBusAsync(factory);

        for (var offset = 0; offset < 7; offset++)
        {
            await SeedTripAsync(factory, route.Id, bus.Id, today.AddDays(offset), hour: 8);
        }

        var selected = today.AddDays(10);
        var body = await ReadJsonAsync(await client.GetAsync(Url(origin.Id, destination.Id, selected)));

        var match = Assert.Single(body.GetProperty("matches").EnumerateArray());

        Assert.True(match.GetProperty("runsDaily").GetBoolean());
        Assert.False(match.GetProperty("hasTripsOnDate").GetBoolean());
        Assert.Equal(Iso(today.AddDays(6)), match.GetProperty("nearestAvailableDate").GetString());
        Assert.Equal(7, match.GetProperty("availableDates").GetArrayLength());

        Assert.Equal("dateBeyondHorizon", body.GetProperty("emptyReason").GetString());
        Assert.Equal(Iso(today.AddDays(6)), body.GetProperty("bookingHorizonEnd").GetString());
    }

    /// <summary>
    /// Đối chứng: ngày ĐÃ có chuyến thì <c>emptyReason</c> phải là <c>null</c> — nếu ai đó luôn
    /// gán một mã lý do thì màn hình sẽ hiện cảnh báo vô cớ trên kết quả tốt.
    /// </summary>
    [Fact]
    public async Task Ngay_co_chuyen_thi_emptyReason_null()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var today = VietnamToday();
        var (origin, destination, route, bus) = await SeedRouteWithTwoStopsAndBusAsync(factory);

        for (var offset = 0; offset < 7; offset++)
        {
            await SeedTripAsync(factory, route.Id, bus.Id, today.AddDays(offset), hour: 8);
        }

        var body = await ReadJsonAsync(
            await client.GetAsync(Url(origin.Id, destination.Id, today.AddDays(1))));

        var match = Assert.Single(body.GetProperty("matches").EnumerateArray());

        Assert.True(match.GetProperty("hasTripsOnDate").GetBoolean());
        Assert.True(match.GetProperty("runsDaily").GetBoolean());
        Assert.Equal(Iso(today.AddDays(1)), match.GetProperty("nearestAvailableDate").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("emptyReason").ValueKind);
    }

    // =======================================================================================
    // 3. Không có tuyến nối — gợi ý tuyến thay thế
    // =======================================================================================

    /// <summary>
    /// Tuyến O1 đi qua điểm đi nhưng không tới điểm đến; tuyến D1 tới điểm đến nhưng không từ
    /// điểm đi. <c>matches</c> rỗng, hai nhóm thay thế có dữ liệu — đây là chỗ màn hình thay ô
    /// trống bằng gợi ý thật.
    /// </summary>
    [Fact]
    public async Task Khong_co_tuyen_noi_truc_tiep_thi_tra_tuyen_thay_the()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var today = VietnamToday();
        var origin = await SeedStopAsync(factory, "Điểm Đi", "Số 1 Đường A");
        var destination = await SeedStopAsync(factory, "Điểm Đến", "Số 9 Đường Z");

        var fromOriginRoute = await SeedRouteAsync(factory, "O1");
        await SeedRouteStopAsync(factory, fromOriginRoute.Id, origin.Id, stopOrder: 1);
        var x = await SeedStopAsync(factory, "Trạm X", "Số 2 Đường B");
        await SeedRouteStopAsync(factory, fromOriginRoute.Id, x.Id, stopOrder: 2);

        var toDestinationRoute = await SeedRouteAsync(factory, "D1");
        var y = await SeedStopAsync(factory, "Trạm Y", "Số 8 Đường Y");
        await SeedRouteStopAsync(factory, toDestinationRoute.Id, y.Id, stopOrder: 1);
        await SeedRouteStopAsync(factory, toDestinationRoute.Id, destination.Id, stopOrder: 2);

        var body = await ReadJsonAsync(await client.GetAsync(Url(origin.Id, destination.Id, today)));

        Assert.Equal(0, body.GetProperty("matches").GetArrayLength());

        var alternatives = body.GetProperty("alternatives");

        Assert.Equal(
            ["O1"],
            alternatives.GetProperty("fromOrigin").EnumerateArray()
                .Select(route => route.GetProperty("routeCode").GetString()));

        Assert.Equal(
            ["D1"],
            alternatives.GetProperty("toDestination").EnumerateArray()
                .Select(route => route.GetProperty("routeCode").GetString()));

        Assert.Equal("noRouteBetweenStops", body.GetProperty("emptyReason").GetString());
    }

    /// <summary>
    /// Điểm đi mồ côi (không thuộc tuyến Active nào) → <c>originNotServed</c>, và màn hình biết
    /// để tô đỏ đúng ô. Khác trường hợp "có trạm nhưng không có tuyến nối".
    /// </summary>
    [Fact]
    public async Task Diem_di_khong_thuoc_tuyen_active_tra_originNotServed()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var today = VietnamToday();
        var origin = await SeedStopAsync(factory, "Trạm Mồ Côi", "Số 1 Đường A");
        var destination = await SeedStopAsync(factory, "Điểm Đến", "Số 9 Đường Z");

        var route = await SeedRouteAsync(factory, "01");
        var x = await SeedStopAsync(factory, "Trạm X", "Số 2 Đường B");
        await SeedRouteStopAsync(factory, route.Id, x.Id, stopOrder: 1);
        await SeedRouteStopAsync(factory, route.Id, destination.Id, stopOrder: 2);

        var body = await ReadJsonAsync(await client.GetAsync(Url(origin.Id, destination.Id, today)));

        Assert.False(body.GetProperty("origin").GetProperty("onActiveRoute").GetBoolean());
        Assert.True(body.GetProperty("destination").GetProperty("onActiveRoute").GetBoolean());
        Assert.Equal("originNotServed", body.GetProperty("emptyReason").GetString());
    }

    [Fact]
    public async Task Khong_co_tuyen_active_nao_tra_noActiveRoute()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var today = VietnamToday();
        var origin = await SeedStopAsync(factory, "Điểm Đi", "Số 1 Đường A");
        var destination = await SeedStopAsync(factory, "Điểm Đến", "Số 9 Đường Z");

        var route = await SeedRouteAsync(factory, "01", RouteStatus.Inactive);
        await SeedRouteStopAsync(factory, route.Id, origin.Id, stopOrder: 1);
        await SeedRouteStopAsync(factory, route.Id, destination.Id, stopOrder: 2);

        var body = await ReadJsonAsync(await client.GetAsync(Url(origin.Id, destination.Id, today)));

        Assert.Equal(0, body.GetProperty("matches").GetArrayLength());
        Assert.Equal("noActiveRoute", body.GetProperty("emptyReason").GetString());
    }

    // =======================================================================================
    // 4. Khớp hai đầu — id, tên không dấu, thứ tự mã tuyến
    // =======================================================================================

    /// <summary>
    /// Màn hình mới gửi id, nhưng hợp đồng vẫn giữ đường tên cho client cũ. Tên phải khớp
    /// <b>không dấu</b> — nếu chỉ <c>ToLower().Contains()</c> như <c>/routes/search</c> cũ thì
    /// "tan long" không ra gì.
    /// </summary>
    [Fact]
    public async Task Ten_khong_dau_van_giai_duoc_ra_tram()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var today = VietnamToday();
        var route = await SeedRouteAsync(factory, "01");
        var origin = await SeedStopAsync(factory, "Tân Long", "Tân Long, Thái Nguyên");
        var destination = await SeedStopAsync(factory, "Phổ Yên", "Phổ Yên, Thái Nguyên");
        await SeedRouteStopAsync(factory, route.Id, origin.Id, stopOrder: 1);
        await SeedRouteStopAsync(factory, route.Id, destination.Id, stopOrder: 2);

        var response = await client.GetAsync(
            $"/api/routes/suggestions?origin=tan%20long&destination=pho%20yen&date={Iso(today)}");

        var body = await ReadJsonAsync(response);

        Assert.Equal("Tân Long", body.GetProperty("origin").GetProperty("name").GetString());
        Assert.Equal("Phổ Yên", body.GetProperty("destination").GetProperty("name").GetString());
        Assert.Equal(1, body.GetProperty("matches").GetArrayLength());
    }

    [Fact]
    public async Task Nhieu_tuyen_khop_thi_xep_theo_ma_tuyen()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var today = VietnamToday();
        var origin = await SeedStopAsync(factory, "Điểm Đi", "Số 1 Đường A");
        var destination = await SeedStopAsync(factory, "Điểm Đến", "Số 9 Đường Z");

        foreach (var code in new[] { "02", "01" })
        {
            var route = await SeedRouteAsync(factory, code);
            await SeedRouteStopAsync(factory, route.Id, origin.Id, stopOrder: 1);
            await SeedRouteStopAsync(factory, route.Id, destination.Id, stopOrder: 2);
        }

        var body = await ReadJsonAsync(await client.GetAsync(Url(origin.Id, destination.Id, today)));

        Assert.Equal(
            ["01", "02"],
            body.GetProperty("matches").EnumerateArray()
                .Select(route => route.GetProperty("routeCode").GetString()));
    }

    // =======================================================================================
    // 5. Lỗi gọi — 400/404, cùng khuôn { message, errors }
    // =======================================================================================

    [Fact]
    public async Task Hai_dau_trung_nhau_tra_400_kem_loi_o_truong_destination()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var stop = await SeedStopAsync(factory, "Trạm A", "Số 1 Đường A");

        var response = await client.GetAsync(Url(stop.Id, stop.Id, VietnamToday()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertCoLoiTheoTruong(await ReadJsonAsync(response), "destination");
    }

    [Fact]
    public async Task Thieu_diem_di_tra_400_kem_loi_o_truong_origin()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var destination = await SeedStopAsync(factory, "Trạm B", "Số 2 Đường B");

        var response = await client.GetAsync($"/api/routes/suggestions?destinationStopId={destination.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertCoLoiTheoTruong(await ReadJsonAsync(response), "origin");
    }

    [Fact]
    public async Task Id_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var destination = await SeedStopAsync(factory, "Trạm B", "Số 2 Đường B");

        var response = await client.GetAsync(
            $"/api/routes/suggestions?originStopId={Guid.NewGuid()}&destinationStopId={destination.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(
            (await ReadJsonAsync(response)).GetProperty("message").GetString()));
    }

    [Theory]
    [InlineData("2026-13-40")]
    [InlineData("17/10/2026")]
    [InlineData("hom-nay")]
    public async Task Date_sai_dinh_dang_tra_400_kem_loi_o_truong_date(string date)
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var (origin, destination) = await SeedRouteWithTwoStopsAsync(factory);

        // Không truyền date hợp lệ vào Url trước rồi nối thêm — hai tham số date cùng tên sẽ
        // khiến model binder lấy giá trị đầu và ca test mất tác dụng.
        var response = await client.GetAsync(
            $"{Url(origin.Id, destination.Id)}&date={Uri.EscapeDataString(date)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertCoLoiTheoTruong(await ReadJsonAsync(response), "date");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("15")]
    public async Task WindowDays_ngoai_khoang_tra_400(string windowDays)
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var today = VietnamToday();
        var (origin, destination) = await SeedRouteWithTwoStopsAsync(factory);

        var response = await client.GetAsync(
            $"{Url(origin.Id, destination.Id, today)}&windowDays={windowDays}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertCoLoiTheoTruong(await ReadJsonAsync(response), "windowDays");
    }

    // =======================================================================================
    // Helper — mỗi lớp test tự chép, không có lớp base chung
    // =======================================================================================

    /// <summary>Hôm nay theo giờ Việt Nam — mọi mốc seed và tham số đều tính từ đây.</summary>
    private static DateOnly VietnamToday()
        => DateOnly.FromDateTime(DateTime.UtcNow.Add(VietnamOffset));

    private static string Iso(DateOnly day)
        => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateTime UtcAt(DateOnly day, int hour)
        => new DateTimeOffset(day.ToDateTime(new TimeOnly(hour, 0)), VietnamOffset).UtcDateTime;

    private static string Url(Guid originStopId, Guid destinationStopId, DateOnly? date = null)
    {
        var parts = new List<string>
        {
            $"originStopId={originStopId}",
            $"destinationStopId={destinationStopId}",
        };

        if (date is { } selected)
        {
            parts.Add($"date={Iso(selected)}");
        }

        return $"/api/routes/suggestions?{string.Join("&", parts)}";
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static void AssertCoLoiTheoTruong(JsonElement body, string truong)
    {
        Assert.True(body.TryGetProperty("errors", out var errors), "Lỗi phải có khoá 'errors' (quy ước D3).");
        Assert.True(
            errors.TryGetProperty(truong, out var danhSach),
            $"errors phải có khoá '{truong}'. Thực tế: {errors}");
        Assert.NotEmpty(danhSach.EnumerateArray());
    }

    private static async Task<Stop> SeedStopAsync(TestAppFactory factory, string name, string address)
    {
        var stop = new Stop
        {
            Id = Guid.NewGuid(),
            Name = name,
            Address = address,
            Latitude = 21.0,
            Longitude = 105.0,
        };

        await factory.SeedAsync(db => db.Stops.Add(stop));

        return stop;
    }

    private static async Task<RouteEntity> SeedRouteAsync(
        TestAppFactory factory,
        string code,
        RouteStatus status = RouteStatus.Active)
    {
        var route = new RouteEntity
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = $"Tuyến {code}",
            Origin = "Điểm đầu",
            Destination = "Điểm cuối",
            Status = status,
        };

        await factory.SeedAsync(db => db.Routes.Add(route));

        return route;
    }

    private static async Task<RouteStop> SeedRouteStopAsync(
        TestAppFactory factory,
        Guid routeId,
        Guid stopId,
        int stopOrder)
    {
        var routeStop = new RouteStop
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            StopId = stopId,
            StopOrder = stopOrder,
            DistanceKm = stopOrder,
        };

        await factory.SeedAsync(db => db.RouteStops.Add(routeStop));

        return routeStop;
    }

    private static async Task<Bus> SeedBusAsync(TestAppFactory factory)
    {
        var bus = new Bus
        {
            Id = Guid.NewGuid(),
            LicensePlate = $"29B-{Random.Shared.Next(100, 999)}.{Random.Shared.Next(10, 99)}",
            BusType = "Hyundai County 29 chỗ",
            Capacity = 29,
            Status = BusStatus.Active,
        };

        await factory.SeedAsync(db => db.Buses.Add(bus));

        return bus;
    }

    private static async Task<Trip> SeedTripAsync(
        TestAppFactory factory,
        Guid routeId,
        Guid busId,
        DateOnly day,
        int hour,
        TripStatus status = TripStatus.Scheduled)
    {
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            BusId = busId,
            DepartureTime = UtcAt(day, hour),
            Status = status,
        };

        await factory.SeedAsync(db => db.Trips.Add(trip));

        return trip;
    }

    /// <summary>Dựng tuyến "01" với đúng hai trạm và một xe — nền chung cho nhiều ca.</summary>
    private static async Task<(Stop Origin, Stop Destination, RouteEntity Route, Bus Bus)>
        SeedRouteWithTwoStopsAndBusAsync(TestAppFactory factory)
    {
        var route = await SeedRouteAsync(factory, "01");
        var origin = await SeedStopAsync(factory, "Điểm Đi", "Số 1 Đường A");
        var destination = await SeedStopAsync(factory, "Điểm Đến", "Số 9 Đường Z");

        await SeedRouteStopAsync(factory, route.Id, origin.Id, stopOrder: 1);
        await SeedRouteStopAsync(factory, route.Id, destination.Id, stopOrder: 2);

        var bus = await SeedBusAsync(factory);

        return (origin, destination, route, bus);
    }

    /// <summary>Dựng tuyến chỉ có hai trạm — dùng cho các ca chỉ cần 200/400/404.</summary>
    private static async Task<(Stop Origin, Stop Destination)> SeedRouteWithTwoStopsAsync(
        TestAppFactory factory)
    {
        var route = await SeedRouteAsync(factory, "01");
        var origin = await SeedStopAsync(factory, "Điểm Đi", "Số 1 Đường A");
        var destination = await SeedStopAsync(factory, "Điểm Đến", "Số 9 Đường Z");

        await SeedRouteStopAsync(factory, route.Id, origin.Id, stopOrder: 1);
        await SeedRouteStopAsync(factory, route.Id, destination.Id, stopOrder: 2);

        return (origin, destination);
    }
}
