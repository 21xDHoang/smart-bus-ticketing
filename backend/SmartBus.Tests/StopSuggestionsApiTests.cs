using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SmartBus.Api.Entities;
using RouteEntity = SmartBus.Api.Entities.Route;

namespace SmartBus.Tests;

/// <summary>
/// Test cho <c>GET /api/stops/suggestions</c> — sửa lỗi trải nghiệm màn "Tra cứu tuyến"
/// (Sprint 5, Vàng Thị Dăm). Hợp đồng đầy đủ ở mục "Gợi ý trạm cho tra cứu tuyến —
/// /stops/suggestions" của docs/api-contract.md.
///
/// Ba thứ lớp test này khoá:
///
/// 1. <b>Chỉ trạm trên tuyến Active</b> — trạm mồ côi của tuyến đã xoá/ngừng khai thác không bao
///    giờ được gợi ý. Đây chính là lỗi đang sửa: hành khách chọn trạm còn trong bảng nhưng không
///    tuyến nào chạy qua, rồi nhận ô trống "không tìm thấy tuyến".
/// 2. <b>Lọc theo thứ tự trạm</b> — chọn điểm đi rồi thì ô điểm đến chỉ gợi ý trạm đứng sau trên
///    cùng tuyến; chọn điểm đến thì ô điểm đi chỉ gợi ý trạm đứng trước.
/// 3. <b>Bỏ dấu</b> — cùng thuật toán với /stops/search, gồm cả chữ <c>đ</c>.
///
/// Mỗi lớp test tự chép helper — đúng lệ đang có của dự án (không có lớp base chung).
/// </summary>
public class StopSuggestionsApiTests
{
    // =======================================================================================
    // 1. Công khai — hành khách chọn trạm trước khi đăng nhập
    // =======================================================================================

    [Fact]
    public async Task Khong_token_van_goi_duoc()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        await SeedStopOnActiveRouteAsync(factory, "Trạm Cầu Giấy", "Số 1 Cầu Giấy, Hà Nội");

        var response = await client.GetAsync("/api/stops/suggestions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // =======================================================================================
    // 2. Chỉ trạm trên tuyến Active — lỗi chính đang sửa
    // =======================================================================================

    /// <summary>
    /// Bốn trạm: hai trên tuyến Active, một trên tuyến Inactive, một mồ côi hoàn toàn. Chỉ hai
    /// trạm đầu được trả về. Nếu ai đó bỏ bộ lọc <c>RouteStatus.Active</c> hoặc quay về
    /// <c>/stops/search</c>, ca này đỏ.
    /// </summary>
    [Fact]
    public async Task Chi_tra_tram_tren_tuyen_dang_khai_thac()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var activeRoute = await SeedRouteAsync(factory, "01", RouteStatus.Active);
        await SeedRouteStopAsync(
            factory,
            activeRoute.Id,
            (await SeedStopAsync(factory, "Trạm A", "Số 1 Đường A")).Id,
            stopOrder: 1);
        await SeedRouteStopAsync(
            factory,
            activeRoute.Id,
            (await SeedStopAsync(factory, "Trạm B", "Số 2 Đường B")).Id,
            stopOrder: 2);

        var inactiveRoute = await SeedRouteAsync(factory, "02", RouteStatus.Inactive);
        await SeedRouteStopAsync(
            factory,
            inactiveRoute.Id,
            (await SeedStopAsync(factory, "Trạm C", "Số 3 Đường C")).Id,
            stopOrder: 1);

        await SeedStopAsync(factory, "Trạm D", "Số 4 Đường D");

        var body = await ReadJsonAsync(await client.GetAsync("/api/stops/suggestions"));

        Assert.Equal(
            ["Trạm A", "Trạm B"],
            body.EnumerateArray().Select(dong => dong.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task Khong_co_tuyen_active_tra_mang_rong()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var route = await SeedRouteAsync(factory, "01", RouteStatus.Inactive);
        await SeedRouteStopAsync(
            factory,
            route.Id,
            (await SeedStopAsync(factory, "Trạm A", "Số 1 Đường A")).Id,
            stopOrder: 1);

        var response = await client.GetAsync("/api/stops/suggestions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, (await ReadJsonAsync(response)).GetArrayLength());
    }

    /// <summary>
    /// Badge tuyến: mỗi trạm trả kèm các tuyến Active đi qua nó, sắp theo mã. Màn hình dùng mã
    /// để hành khách phân biệt hai trạm trùng tên trên hai tuyến khác nhau.
    /// </summary>
    [Fact]
    public async Task Moi_tram_tra_kem_ma_cac_tuyen_di_qua()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var stop = await SeedStopAsync(factory, "Trạm Trung Tâm", "Số 1 Trung Tâm");
        var route02 = await SeedRouteAsync(factory, "02");
        var route01 = await SeedRouteAsync(factory, "01");

        await SeedRouteStopAsync(factory, route02.Id, stop.Id, stopOrder: 2);
        await SeedRouteStopAsync(factory, route01.Id, stop.Id, stopOrder: 1);

        var body = await ReadJsonAsync(await client.GetAsync("/api/stops/suggestions"));
        var dong = Assert.Single(body.EnumerateArray());

        Assert.Equal(
            ["01", "02"],
            dong.GetProperty("routes").EnumerateArray()
                .Select(route => route.GetProperty("routeCode").GetString()));
    }

    // =======================================================================================
    // 3. Lọc theo thứ tự trạm — ô điểm đi / điểm đến không gợi ý ngược chiều
    // =======================================================================================

    /// <summary>
    /// Tuyến A(1) → B(2) → C(3) → D(4). Chọn A làm điểm đi thì ô điểm đến chỉ được thấy B, C, D —
    /// không thấy A (điểm đi) và không thấy trạm của tuyến khác.
    /// </summary>
    [Fact]
    public async Task Chon_diem_di_thi_chi_goi_y_tram_sau_no()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var route = await SeedRouteAsync(factory, "01");
        var stopA = await SeedStopAsync(factory, "Trạm A", "Số 1 Đường A");
        var stopB = await SeedStopAsync(factory, "Trạm B", "Số 2 Đường B");
        var stopC = await SeedStopAsync(factory, "Trạm C", "Số 3 Đường C");
        var stopD = await SeedStopAsync(factory, "Trạm D", "Số 4 Đường D");

        await SeedRouteStopAsync(factory, route.Id, stopA.Id, stopOrder: 1);
        await SeedRouteStopAsync(factory, route.Id, stopB.Id, stopOrder: 2);
        await SeedRouteStopAsync(factory, route.Id, stopC.Id, stopOrder: 3);
        await SeedRouteStopAsync(factory, route.Id, stopD.Id, stopOrder: 4);

        var body = await ReadJsonAsync(
            await client.GetAsync($"/api/stops/suggestions?originStopId={stopA.Id}"));

        Assert.Equal(
            ["Trạm B", "Trạm C", "Trạm D"],
            body.EnumerateArray().Select(dong => dong.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task Chon_diem_den_thi_chi_goi_y_tram_truoc_no()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var route = await SeedRouteAsync(factory, "01");
        var stopA = await SeedStopAsync(factory, "Trạm A", "Số 1 Đường A");
        var stopB = await SeedStopAsync(factory, "Trạm B", "Số 2 Đường B");
        var stopC = await SeedStopAsync(factory, "Trạm C", "Số 3 Đường C");

        await SeedRouteStopAsync(factory, route.Id, stopA.Id, stopOrder: 1);
        await SeedRouteStopAsync(factory, route.Id, stopB.Id, stopOrder: 2);
        await SeedRouteStopAsync(factory, route.Id, stopC.Id, stopOrder: 3);

        var body = await ReadJsonAsync(
            await client.GetAsync($"/api/stops/suggestions?destinationStopId={stopC.Id}"));

        Assert.Equal(
            ["Trạm A", "Trạm B"],
            body.EnumerateArray().Select(dong => dong.GetProperty("name").GetString()));
    }

    /// <summary>
    /// Có cả hai đầu: chỉ trạm nằm GIỮA, không phải mọi trạm sau điểm đi. Tuyến A(1)…E(5), chọn
    /// A → D thì B, C được gợi ý; D và E không.
    /// </summary>
    [Fact]
    public async Task Co_ca_hai_dau_thi_chi_goi_y_tram_o_giua()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var route = await SeedRouteAsync(factory, "01");
        var stops = new List<Stop>();

        for (var order = 1; order <= 5; order++)
        {
            var stop = await SeedStopAsync(factory, $"Trạm {order}", $"Số {order} Đường Mẫu");
            stops.Add(stop);
            await SeedRouteStopAsync(factory, route.Id, stop.Id, stopOrder: order);
        }

        var body = await ReadJsonAsync(await client.GetAsync(
            $"/api/stops/suggestions?originStopId={stops[0].Id}&destinationStopId={stops[3].Id}"));

        Assert.Equal(
            ["Trạm 2", "Trạm 3"],
            body.EnumerateArray().Select(dong => dong.GetProperty("name").GetString()));
    }

    /// <summary>
    /// Trạm đã chọn không thuộc tuyến Active nào → không có trạm nào hợp lệ. Bộ lọc mềm: trả
    /// <c>[]</c> kèm 200, không phải 404 — màn hình tự hiện cảnh báo dưới ô nhập.
    /// </summary>
    [Fact]
    public async Task Tram_da_chon_khong_thuoc_tuyen_active_tra_mang_rong()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var route = await SeedRouteAsync(factory, "01");
        var activeStop = await SeedStopAsync(factory, "Trạm A", "Số 1 Đường A");
        await SeedRouteStopAsync(factory, route.Id, activeStop.Id, stopOrder: 1);

        var orphan = await SeedStopAsync(factory, "Trạm Mồ Côi", "Số 2 Đường B");

        var body = await ReadJsonAsync(
            await client.GetAsync($"/api/stops/suggestions?originStopId={orphan.Id}"));

        Assert.Equal(0, body.GetArrayLength());
    }

    // =======================================================================================
    // 4. Bỏ dấu + địa chỉ
    // =======================================================================================

    [Theory]
    [InlineData("tan long")]
    [InlineData("Tân Long")]
    [InlineData("TÂN LONG")]
    [InlineData("  tan   long  ")]
    public async Task Go_khong_dau_van_ra_dung_tram(string keyword)
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        await SeedStopOnActiveRouteAsync(factory, "Phường Tân Long", "Tân Long, Thái Nguyên");
        await SeedStopOnActiveRouteAsync(factory, "Phổ Yên", "Phổ Yên, Thái Nguyên");

        var body = await ReadJsonAsync(
            await client.GetAsync($"/api/stops/suggestions?keyword={Uri.EscapeDataString(keyword)}"));

        var dong = Assert.Single(body.EnumerateArray());
        Assert.Equal("Phường Tân Long", dong.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Chu_d_moc_khop_voi_d_thuong()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        await SeedStopOnActiveRouteAsync(factory, "Trạm Đống Đa", "Số 1 Tôn Đức Thắng");

        var body = await ReadJsonAsync(
            await client.GetAsync("/api/stops/suggestions?keyword=dong%20da"));

        var dong = Assert.Single(body.EnumerateArray());
        Assert.Equal("Trạm Đống Đa", dong.GetProperty("name").GetString());
    }

    // =======================================================================================
    // 5. limit — cùng hợp đồng với /stops/search
    // =======================================================================================

    [Theory]
    [InlineData("0")]
    [InlineData("51")]
    [InlineData("abc")]
    public async Task Limit_ngoai_khoang_tra_400_kem_loi_o_truong_limit(string limit)
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        await SeedStopOnActiveRouteAsync(factory, "Trạm Cầu Giấy", "Số 1 Cầu Giấy");

        var response = await client.GetAsync($"/api/stops/suggestions?limit={limit}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await ReadJsonAsync(response);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
        AssertCoLoiTheoTruong(body, "limit");
    }

    // =======================================================================================
    // Helper — mỗi lớp test tự chép, không có lớp base chung
    // =======================================================================================

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

    private static async Task<Stop> SeedStopOnActiveRouteAsync(
        TestAppFactory factory,
        string name,
        string address)
    {
        var route = await SeedRouteAsync(factory, Guid.NewGuid().ToString("N")[..8]);
        var stop = await SeedStopAsync(factory, name, address);

        await SeedRouteStopAsync(factory, route.Id, stop.Id, stopOrder: 1);

        return stop;
    }
}
