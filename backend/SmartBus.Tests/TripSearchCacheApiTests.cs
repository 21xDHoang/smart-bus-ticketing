using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SmartBus.Api.Entities;
using RouteEntity = SmartBus.Api.Entities.Route;

namespace SmartBus.Tests;

/// <summary>
/// Test tích hợp cho bộ nhớ đệm của <c>GET /api/trips/search</c>
/// (task *"Cache kết quả tìm kiếm tuyến phổ biến để giảm tải DB"* — Nguyễn Duy Kiên).
///
/// Đây cũng là **test đầu tiên của endpoint này**: bộ đệm nằm trước
/// <c>TripSearchService</c> (file của Phùng Duy Hoàng) nên các ca dưới đây vừa ghim hành vi đệm vừa
/// ghim những gì hợp đồng đã hứa mà trước nay chưa ai khoá lại — thứ tự chuyến, hình dạng response,
/// 404/400 đi thẳng. Endpoint là API CÔNG KHAI nên test không cần token (khác mọi test `/trips` khác
/// trong bộ test).
///
/// Cách chứng minh "đệm có thật sự đứng trước endpoint": gọi hai lượt cho nóng đệm (lượt 1 ghi nhận,
/// lượt 2 mới lưu — xem <see cref="TripSearchResultCache"/>), rồi **ghi thẳng một chuyến mới vào
/// CSDL** và gọi lượt thứ ba. Không có đệm thì lượt ba phải thấy chuyến mới; có đệm thì vẫn thấy kết
/// quả cũ. Đó là cách duy nhất quan sát được đệm từ bên ngoài HTTP mà không phải mở một API nội bộ.
/// </summary>
public class TripSearchCacheApiTests
{
    /// <summary>Giờ khởi hành cố định — DateTimeKind.Utc để chuỗi ISO gửi lên có hậu tố Z.</summary>
    private static readonly DateTime Gio8 = new(2026, 10, 2, 8, 0, 0, DateTimeKind.Utc);

    // ---------------------------------------------------------------------------------------
    // Bộ đệm có thật sự đứng trước endpoint không
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Luot_goi_thu_ba_tra_ket_qua_cu_tu_bo_nho_dem()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory);
        await SeedTripAsync(factory, route.Id, bus.Id, Gio8);
        await SeedFareAsync(factory, route.Id, PassengerType.Standard, 7000m);

        var url = SearchUrl(route.Id);

        Assert.Equal(1, (await ReadJsonAsync(await client.GetAsync(url))).GetArrayLength());
        Assert.Equal(1, (await ReadJsonAsync(await client.GetAsync(url))).GetArrayLength());

        // Chuyến thứ hai ghi thẳng vào CSDL — bộ đệm không được biết về nó.
        await SeedTripAsync(factory, route.Id, bus.Id, Gio8.AddHours(1));

        var luotBa = await ReadJsonAsync(await client.GetAsync(url));

        // Vẫn đúng kết quả cũ ⇒ có bộ đệm đứng trước endpoint thật.
        Assert.Equal(1, luotBa.GetArrayLength());
        Assert.Equal(Gio8, luotBa[0].GetProperty("departureTime").GetDateTime());

        // Đối chứng: một tuyến KHÁC chỉ mới hỏi một lượt thì vẫn đọc tươi từ CSDL — bộ đệm không
        // dùng chung kết quả giữa hai tuyến.
        var routeKhac = await SeedRouteAsync(factory, code: "02");
        await SeedTripAsync(factory, routeKhac.Id, bus.Id, Gio8);
        await SeedTripAsync(factory, routeKhac.Id, bus.Id, Gio8.AddHours(1));

        var cuaTuyenKhac = await ReadJsonAsync(await client.GetAsync(SearchUrl(routeKhac.Id)));

        Assert.Equal(2, cuaTuyenKhac.GetArrayLength());
    }

    [Fact]
    public async Task Khac_khoang_thoi_gian_thi_khong_dung_chung_ket_qua()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory);
        await SeedTripAsync(factory, route.Id, bus.Id, Gio8);
        await SeedTripAsync(factory, route.Id, bus.Id, Gio8.AddHours(2));

        // Làm nóng khoá "không lọc thời gian" (2 lượt) rồi thêm một chuyến nữa.
        var caNgay = SearchUrl(route.Id);
        Assert.Equal(2, (await ReadJsonAsync(await client.GetAsync(caNgay))).GetArrayLength());
        Assert.Equal(2, (await ReadJsonAsync(await client.GetAsync(caNgay))).GetArrayLength());

        await SeedTripAsync(factory, route.Id, bus.Id, Gio8.AddHours(4));

        // Khoá khác (lọc từ 11:00) là truy vấn khác → hỏi CSDL → thấy đúng chuyến vừa thêm.
        var tu11h = await ReadJsonAsync(await client.GetAsync(SearchUrl(route.Id, from: Gio8.AddHours(3))));

        Assert.Equal(1, tu11h.GetArrayLength());
        Assert.Equal(Gio8.AddHours(4), tu11h[0].GetProperty("departureTime").GetDateTime());
    }

    // ---------------------------------------------------------------------------------------
    // Hợp đồng vẫn nguyên vẹn khi có đệm
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Ket_qua_tra_ve_giu_nguyen_cac_truong_trong_hop_dong()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory);
        await SeedTripAsync(factory, route.Id, bus.Id, Gio8);
        await SeedFareAsync(factory, route.Id, PassengerType.Standard, 7000m);

        var body = await ReadJsonAsync(await client.GetAsync(SearchUrl(route.Id)));

        // Có đệm hay không thì hình dạng response cũng phải y hệt hợp đồng: đệm là chuyện tốc độ,
        // không phải chuyện thêm/bớt trường (⛔5). Thêm một trường là đổi hình dạng API.
        Assert.Equal(
            [
                "arrivalTime", "busType", "capacity", "departureTime", "id", "price",
                "routeCode", "routeId", "routeName", "seatsRemaining",
            ],
            PropertyNamesOf(body[0]));

        Assert.Equal(route.Id, body[0].GetProperty("routeId").GetGuid());
        Assert.Equal("01", body[0].GetProperty("routeCode").GetString());
        Assert.Equal(7000m, body[0].GetProperty("price").GetDecimal());
    }

    [Fact]
    public async Task Khong_can_token_va_chi_tra_chuyen_Scheduled_xep_theo_gio_chay()
    {
        using var factory = new TestAppFactory();

        // KHÔNG gắn token: đây là endpoint công khai duy nhất của bề mặt /trips — đệm không được
        // đụng tới chuyện phân quyền.
        var client = factory.CreateClient();

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory);
        await SeedTripAsync(factory, route.Id, bus.Id, Gio8.AddHours(2));
        await SeedTripAsync(factory, route.Id, bus.Id, Gio8);
        await SeedTripAsync(factory, route.Id, bus.Id, Gio8.AddHours(1), status: TripStatus.Cancelled);

        var response = await client.GetAsync(SearchUrl(route.Id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);

        // Chuyến đã huỷ không phải lựa chọn để đặt vé; hai chuyến còn lại xếp theo giờ khởi hành.
        Assert.Equal(
            [Gio8, Gio8.AddHours(2)],
            body.EnumerateArray().Select(trip => trip.GetProperty("departureTime").GetDateTime()));
    }

    // ---------------------------------------------------------------------------------------
    // Lỗi KHÔNG bị đệm
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Tuyen_khong_ton_tai_tra_404_va_khong_bi_dem()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var routeId = Guid.NewGuid();

        var lan1 = await client.GetAsync(SearchUrl(routeId));

        Assert.Equal(HttpStatusCode.NotFound, lan1.StatusCode);
        Assert.Contains("Không tìm thấy tuyến đường", await MessageAsync(lan1));

        // Tuyến vừa được tạo (ví dụ ngay sau khi điều hành thêm tuyến) phải tra được NGAY — 404 ở
        // lượt trước là lỗi, không phải kết quả, nên không được nằm trong đệm.
        await SeedRouteAsync(factory, id: routeId);

        var lan2 = await client.GetAsync(SearchUrl(routeId));

        Assert.Equal(HttpStatusCode.OK, lan2.StatusCode);
        Assert.Equal(0, (await ReadJsonAsync(lan2)).GetArrayLength());
    }

    [Fact]
    public async Task Khoang_thoi_gian_sai_van_tra_400_kem_errors_to()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var route = await SeedRouteAsync(factory);

        var response = await client.GetAsync(SearchUrl(route.Id, from: Gio8.AddHours(1), to: Gio8));

        // Lớp bọc đệm chỉ đệm kết quả thành công — 400 phải đi thẳng từ service thật lên, nguyên
        // hình dạng { message, errors }.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await ReadJsonAsync(response);

        // Lỗi nghiệp vụ (ServiceResult.Invalid) trả câu của service ở cả `message` lẫn `errors.to` —
        // chỉ lỗi bind/validate của ModelState mới mang câu "Dữ liệu đầu vào không hợp lệ".
        Assert.Equal("Thời điểm kết thúc phải sau thời điểm bắt đầu", body.GetProperty("message").GetString());
        Assert.Equal(
            "Thời điểm kết thúc phải sau thời điểm bắt đầu",
            body.GetProperty("errors").GetProperty("to")[0].GetString());
    }

    // ---------------------------------------------------------------------------------------
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // ---------------------------------------------------------------------------------------

    private static string SearchUrl(Guid routeId, DateTime? from = null, DateTime? to = null)
    {
        var url = $"/api/trips/search?routeId={routeId}";

        if (from is { } tu) url += $"&from={Uri.EscapeDataString(MocIso(tu))}";
        if (to is { } den) url += $"&to={Uri.EscapeDataString(MocIso(den))}";

        return url;
    }

    private static string MocIso(DateTime value)
        => value.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        var body = await ReadJsonAsync(response);
        return body.GetProperty("message").GetString() ?? string.Empty;
    }

    private static string[] PropertyNamesOf(JsonElement element)
        => element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();

    private static async Task<RouteEntity> SeedRouteAsync(
        TestAppFactory factory,
        string code = "01",
        Guid? id = null)
    {
        var route = new RouteEntity
        {
            Id = id ?? Guid.NewGuid(),
            Code = code,
            Name = "Bến xe Mỹ Đình — Bến xe Gia Lâm",
            Origin = "Bến xe Mỹ Đình",
            Destination = "Bến xe Gia Lâm",
        };

        await factory.SeedAsync(db => db.Routes.Add(route));

        return route;
    }

    private static async Task<Bus> SeedBusAsync(TestAppFactory factory)
    {
        var bus = new Bus
        {
            Id = Guid.NewGuid(),
            LicensePlate = "29B-123.45",
            BusType = "Xe buýt 45 chỗ",
            Capacity = 45,
            Status = BusStatus.Active,
        };

        await factory.SeedAsync(db => db.Buses.Add(bus));

        return bus;
    }

    private static async Task<Trip> SeedTripAsync(
        TestAppFactory factory,
        Guid routeId,
        Guid busId,
        DateTime departureTime,
        TripStatus status = TripStatus.Scheduled)
    {
        // Chỉ gán khoá ngoại, KHÔNG gán navigation: Route/Bus được seed ở scope khác, gán navigation
        // vào đây sẽ khiến EF tưởng chúng là bản ghi mới và chèn trùng khoá chính.
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            BusId = busId,
            DepartureTime = departureTime,
            ArrivalTime = departureTime.AddMinutes(45),
            Status = status,
        };

        await factory.SeedAsync(db => db.Trips.Add(trip));

        return trip;
    }

    private static async Task<Fare> SeedFareAsync(
        TestAppFactory factory,
        Guid routeId,
        PassengerType passengerType,
        decimal price)
    {
        var fare = new Fare
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            PassengerType = passengerType,
            Price = price,
        };

        await factory.SeedAsync(db => db.Fares.Add(fare));

        return fare;
    }
}
