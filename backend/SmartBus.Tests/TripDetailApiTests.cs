using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;
using RouteEntity = SmartBus.Api.Entities.Route;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test tích hợp cho API chi tiết chuyến — <c>GET /api/trips/{id}</c>
/// (task story 13 — Vàng Thị Dăm).
///
/// Dùng lại <see cref="TestAppFactory"/> của JwtAuthTests: chạy trên app thật, mỗi test một
/// CSDL InMemory riêng.
///
/// ⚠️ Hai giới hạn của provider InMemory ảnh hưởng tới cách đọc kết quả ở đây:
///   • KHÔNG dựng khoá ngoại Restrict, nên seed được một chuyến trỏ tới tuyến/xe không tồn tại —
///     đó chính là cách dựng ca "dữ liệu mồ côi" bên dưới. Ở PostgreSQL thật những dòng đó không
///     tồn tại được, nhưng nhánh 404 vẫn phải có vì join trong là thứ quyết định, không phải CSDL.
///   • KHÔNG dựng unique index, nên muốn thử hai trạm cùng <c>StopOrder</c> thì phải tự seed —
///     xem ca <c>Hai_tram_cung_thu_tu_thi_xep_tiep_theo_stopId</c>.
/// </summary>
public class TripDetailApiTests
{
    /// <summary>Giờ khởi hành cố định — dùng DateTimeKind.Utc để chuỗi ISO trả về có hậu tố Z.</summary>
    private static readonly DateTime DefaultDeparture = new(2026, 10, 1, 1, 0, 0, DateTimeKind.Utc);

    // ---------------------------------------------------------------------------------------
    // Phân quyền
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Khong_gui_token_thi_tra_401()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync(TripUrl(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(RoleCodes.Driver)]
    [InlineData(RoleCodes.Passenger)]
    public async Task Vai_tro_khac_quan_ly_thi_tra_403(string roleCode)
    {
        using var factory = new TestAppFactory();

        var client = await SignInAsync(factory, RoleIdsFor(roleCode), roleCode);
        var (trip, _, _) = await SeedTripWithRouteAndBusAsync(factory);

        // Story 13 là "Là quản lý, tôi muốn thiết lập thời gian biểu…" — cùng nhóm với /routes và
        // /stops, nên Tài xế và Hành khách đều 403 dù chuyến có thật.
        var response = await client.GetAsync(TripUrl(trip.Id));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------------
    // Nội dung chi tiết — bốn thứ story yêu cầu: giờ chạy, loại xe, sức chứa, danh sách trạm dừng
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Tra_du_gio_chay_loai_xe_suc_chua_va_danh_sach_tram_dung()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory, code: "01");
        var bus = await SeedBusAsync(factory, "29B-123.45", "Hyundai County 29 chỗ", capacity: 29);
        var trip = await SeedTripAsync(
            factory,
            route.Id,
            bus.Id,
            arrivalTime: DefaultDeparture.AddMinutes(45));

        await SeedStopOnRouteAsync(factory, route.Id, "Bến xe Mỹ Đình", stopOrder: 1, distanceKm: 0m);
        await SeedStopOnRouteAsync(factory, route.Id, "Cầu Giấy", stopOrder: 2, distanceKm: 4.2m);

        var body = await ReadJsonAsync(await client.GetAsync(TripUrl(trip.Id)));

        // Giờ chạy
        Assert.Equal(DefaultDeparture, body.GetProperty("departureTime").GetDateTime());
        Assert.Equal(DefaultDeparture.AddMinutes(45), body.GetProperty("arrivalTime").GetDateTime());

        // Tuyến — trả kèm để màn hình khỏi gọi thêm /routes/{id}
        Assert.Equal(route.Id, body.GetProperty("routeId").GetGuid());
        Assert.Equal("01", body.GetProperty("routeCode").GetString());
        Assert.Equal("Bến xe Mỹ Đình — Bến xe Gia Lâm", body.GetProperty("routeName").GetString());

        // Loại xe + sức chứa
        Assert.Equal(bus.Id, body.GetProperty("busId").GetGuid());
        Assert.Equal("29B-123.45", body.GetProperty("licensePlate").GetString());
        Assert.Equal("Hyundai County 29 chỗ", body.GetProperty("busType").GetString());
        Assert.Equal(29, body.GetProperty("capacity").GetInt32());

        // Danh sách trạm dừng
        Assert.Equal(["Bến xe Mỹ Đình", "Cầu Giấy"], StopNamesOf(body));

        var firstStop = body.GetProperty("stops")[0];
        Assert.Equal(1, firstStop.GetProperty("stopOrder").GetInt32());
        Assert.Equal(0m, firstStop.GetProperty("distanceKm").GetDecimal());
        Assert.Equal("Số 1 Cầu Giấy, Hà Nội", body.GetProperty("stops")[1].GetProperty("stopAddress").GetString());
    }

    [Fact]
    public async Task Danh_sach_tram_dung_xep_theo_thu_tu_chay_du_luu_nguoc()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (trip, route, _) = await SeedTripWithRouteAndBusAsync(factory);

        // Seed cố ý NGƯỢC thứ tự chạy: dòng "Trạm C" (3) nằm trước dòng "Trạm A" (1) trong CSDL.
        // Nếu service quên orderby thì thứ tự trả về là thứ tự chèn, không phải thứ tự chạy — và
        // đây đúng là thứ tự mà màn hình dùng để vẽ lộ trình.
        await SeedStopOnRouteAsync(factory, route.Id, "Trạm C", stopOrder: 3, distanceKm: 2.5m);
        await SeedStopOnRouteAsync(factory, route.Id, "Trạm A", stopOrder: 1, distanceKm: 0m);
        await SeedStopOnRouteAsync(factory, route.Id, "Trạm B", stopOrder: 2, distanceKm: 1.8m);

        var body = await ReadJsonAsync(await client.GetAsync(TripUrl(trip.Id)));

        Assert.Equal(["Trạm A", "Trạm B", "Trạm C"], StopNamesOf(body));
        Assert.Equal([1, 2, 3], StopOrdersOf(body));
    }

    [Fact]
    public async Task Hai_tram_cung_thu_tu_thi_xep_tiep_theo_stopId()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (trip, route, _) = await SeedTripWithRouteAndBusAsync(factory);

        // CSDL không ràng buộc unique trên (RouteId, StopOrder) — hai request POST chạy song song
        // có thể cùng ghi ra StopOrder = 1 (xem ghi chú ở mục "Trạm trên tuyến" của
        // api-contract.md). Khe hở đó là có thật, nên thứ tự hiển thị phải ổn định giữa các lần
        // gọi: xếp tiếp theo stopId.
        var stopA = await SeedStopOnRouteAsync(factory, route.Id, "Trạm A", stopOrder: 1);
        var stopB = await SeedStopOnRouteAsync(factory, route.Id, "Trạm B", stopOrder: 1);

        var expected = new[] { stopA.StopId, stopB.StopId }.OrderBy(stopId => stopId).ToArray();

        var first = await ReadJsonAsync(await client.GetAsync(TripUrl(trip.Id)));
        var second = await ReadJsonAsync(await client.GetAsync(TripUrl(trip.Id)));

        Assert.Equal(expected, StopIdsOf(first));
        Assert.Equal(expected, StopIdsOf(second));
    }

    [Fact]
    public async Task Hai_chuyen_cung_tuyen_thi_cung_danh_sach_tram()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory);
        var morning = await SeedTripAsync(factory, route.Id, bus.Id, DefaultDeparture);
        var afternoon = await SeedTripAsync(factory, route.Id, bus.Id, DefaultDeparture.AddHours(6));

        await SeedStopOnRouteAsync(factory, route.Id, "Trạm A", stopOrder: 1);
        await SeedStopOnRouteAsync(factory, route.Id, "Trạm B", stopOrder: 2);

        var firstTrip = await ReadJsonAsync(await client.GetAsync(TripUrl(morning.Id)));
        var secondTrip = await ReadJsonAsync(await client.GetAsync(TripUrl(afternoon.Id)));

        // Quy ước A9 — không có bảng TripStops: thứ tự trạm là của TUYẾN, mọi chuyến của tuyến dùng
        // chung. Đây là điều kiện để sửa thứ tự trạm một lần là mọi chuyến đổi theo, không phải
        // đồng bộ gì thêm.
        Assert.Equal(StopNamesOf(firstTrip), StopNamesOf(secondTrip));
        Assert.Equal(["Trạm A", "Trạm B"], StopNamesOf(secondTrip));
    }

    [Fact]
    public async Task Tuyen_chua_gan_tram_nao_thi_tra_mang_rong()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (trip, _, _) = await SeedTripWithRouteAndBusAsync(factory);

        var response = await client.GetAsync(TripUrl(trip.Id));
        var body = await ReadJsonAsync(response);

        // Chuyến có thật thì vẫn là 200 — tuyến chưa gán trạm là chuyện bình thường (lịch trình
        // vừa sinh xong, quản lý chưa xếp trạm), không phải "không tìm thấy".
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, body.GetProperty("stops").GetArrayLength());
    }

    [Fact]
    public async Task ArrivalTime_chua_chot_thi_tra_null()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory);
        var trip = await SeedTripAsync(factory, route.Id, bus.Id, arrivalTime: null);

        var body = await ReadJsonAsync(await client.GetAsync(TripUrl(trip.Id)));

        Assert.Equal(JsonValueKind.Null, body.GetProperty("arrivalTime").ValueKind);
    }

    [Fact]
    public async Task Xe_dang_bao_duong_thi_van_xem_duoc_va_thay_dung_trang_thai()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory, status: BusStatus.Maintenance);
        var trip = await SeedTripAsync(factory, route.Id, bus.Id);

        var response = await client.GetAsync(TripUrl(trip.Id));
        var body = await ReadJsonAsync(response);

        // Xe vào bảo dưỡng SAU khi chuyến đã sinh là tình huống có thật: chuyến vẫn Scheduled và
        // vẫn phải xem được, chỉ là người điều hành cần thấy đúng trạng thái xe để mà đổi xe.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Maintenance", body.GetProperty("busStatus").GetString());
    }

    [Fact]
    public async Task Trang_thai_tra_ve_chuoi_chu_khong_phai_so()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory);
        var running = await SeedTripAsync(factory, route.Id, bus.Id, status: TripStatus.Running);

        var body = await ReadJsonAsync(await client.GetAsync(TripUrl(running.Id)));

        // Program.cs không đăng ký JsonStringEnumConverter, nên nếu DTO để kiểu enum thì ở đây sẽ
        // là số 1 — đọc không hiểu, trái tinh thần quy ước A3.
        Assert.Equal(JsonValueKind.String, body.GetProperty("status").ValueKind);
        Assert.Equal("Running", body.GetProperty("status").GetString());
    }

    // ---------------------------------------------------------------------------------------
    // Hình dạng response — khoá lại đúng những gì docs/api-contract.md đã chốt (⛔5)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Response_chi_tra_dung_cac_truong_trong_hop_dong()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (trip, route, _) = await SeedTripWithRouteAndBusAsync(factory);
        await SeedStopOnRouteAsync(factory, route.Id, "Trạm A", stopOrder: 1);

        var body = await ReadJsonAsync(await client.GetAsync(TripUrl(trip.Id)));

        // Thêm một trường vào response là ĐỔI HÌNH DẠNG API: theo ⛔5 phải sửa api-contract.md
        // trước rồi báo người viết frontend. Ca này làm đổ test ngay lúc đó, để việc sửa hợp đồng
        // là một quyết định có ý thức chứ không phải một dòng code lỡ tay.
        Assert.Equal(
            [
                "arrivalTime", "busId", "busStatus", "busType", "capacity", "departureTime",
                "destination", "id", "licensePlate", "origin", "routeCode", "routeId", "routeName",
                "status", "stops",
            ],
            PropertyNamesOf(body));

        // Cố ý KHÔNG có vị trí hiện tại của xe (chuyện của Sprint 3) và seatsRemaining (chưa có
        // bảng vé — task của Hoàng). Khẳng định sự VẮNG MẶT cũng là khẳng định hợp đồng.
        Assert.DoesNotContain("currentLat", PropertyNamesOf(body));
        Assert.DoesNotContain("seatsRemaining", PropertyNamesOf(body));
    }

    [Fact]
    public async Task Moi_tram_khong_tra_kem_id_cua_dong_RouteStop()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (trip, route, _) = await SeedTripWithRouteAndBusAsync(factory);
        await SeedStopOnRouteAsync(factory, route.Id, "Trạm A", stopOrder: 1);

        var body = await ReadJsonAsync(await client.GetAsync(TripUrl(trip.Id)));
        var stop = body.GetProperty("stops")[0];

        // Chuyến không sở hữu dòng RouteStops: gỡ/sửa trạm là thao tác trên TUYẾN. Trả kèm id của
        // dòng ở đây là mời người gọi gửi nó vào DELETE /routes/{routeId}/stops/{id} — nơi nó chỉ
        // có nghĩa khi đi kèm đúng routeId.
        Assert.Equal(
            ["distanceKm", "latitude", "longitude", "stopAddress", "stopId", "stopName", "stopOrder"],
            PropertyNamesOf(stop));
    }

    // ---------------------------------------------------------------------------------------
    // Không tìm thấy
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Chuyen_khong_ton_tai_thi_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var response = await client.GetAsync(TripUrl(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("chuyến", await MessageAsync(response), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Id_khong_phai_guid_thi_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        // Ràng buộc {id:guid} trên route: chuỗi không phải GUID không khớp endpoint nào cả.
        var response = await client.GetAsync("/api/trips/khong-phai-guid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Tuyen_cua_chuyen_khong_con_thi_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        // Chuyến trỏ tới tuyến không tồn tại. InMemory không dựng khoá ngoại nên seed được dòng
        // này; ở PostgreSQL thật khoá ngoại Restrict chặn từ trước nên không có dòng mồ côi.
        var bus = await SeedBusAsync(factory);
        var trip = await SeedTripAsync(factory, routeId: Guid.NewGuid(), busId: bus.Id);

        var response = await client.GetAsync(TripUrl(trip.Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Xe_cua_chuyen_khong_con_thi_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var trip = await SeedTripAsync(factory, route.Id, busId: Guid.NewGuid());

        var response = await client.GetAsync(TripUrl(trip.Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------------
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // ---------------------------------------------------------------------------------------

    private static string TripUrl(Guid tripId) => $"/api/trips/{tripId}";

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        var body = await ReadJsonAsync(response);
        return body.GetProperty("message").GetString() ?? string.Empty;
    }

    private static string[] PropertyNamesOf(JsonElement element)
        => element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();

    private static string[] StopNamesOf(JsonElement body)
        => body.GetProperty("stops").EnumerateArray()
            .Select(stop => stop.GetProperty("stopName").GetString() ?? string.Empty)
            .ToArray();

    private static Guid[] StopIdsOf(JsonElement body)
        => body.GetProperty("stops").EnumerateArray()
            .Select(stop => stop.GetProperty("stopId").GetGuid())
            .ToArray();

    private static int[] StopOrdersOf(JsonElement body)
        => body.GetProperty("stops").EnumerateArray()
            .Select(stop => stop.GetProperty("stopOrder").GetInt32())
            .ToArray();

    private static async Task<HttpClient> SignInAsync(TestAppFactory factory, Guid roleId, string roleCode)
    {
        await EnsureAllRolesAsync(factory);
        var user = await SeedUserAsync(factory, roleId, roleCode);

        return ClientWith(factory, factory.CreateTokenFor(user));
    }

    private static async Task<HttpClient> SignInAsManagerAsync(TestAppFactory factory)
        => await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager);

    private static HttpClient ClientWith(TestAppFactory factory, string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    private static Guid RoleIdsFor(string roleCode) => roleCode switch
    {
        RoleCodes.Admin => RoleIds.Admin,
        RoleCodes.Manager => RoleIds.Manager,
        RoleCodes.Driver => RoleIds.Driver,
        _ => RoleIds.Passenger,
    };

    /// <summary>
    /// Dựng đúng nền dữ liệu mà migration tạo sẵn ở production: 4 vai trò chuẩn.
    /// Provider InMemory KHÔNG chạy <c>HasData</c>, nên không seed thì tài khoản trỏ tới vai trò
    /// không tồn tại và mọi request đều 403 dù token hợp lệ.
    /// </summary>
    private static async Task EnsureAllRolesAsync(TestAppFactory factory)
    {
        await factory.SeedAsync(db =>
        {
            foreach (var (id, code) in CanonicalRoles)
            {
                if (!db.Roles.Any(r => r.Id == id || r.Code == code))
                {
                    db.Roles.Add(new Role { Id = id, Code = code, Name = code });
                }
            }
        });
    }

    /// <summary>Bốn vai trò migration seed sẵn — khớp <c>AppDbContext.UserRoles.cs</c>.</summary>
    private static readonly (Guid Id, string Code)[] CanonicalRoles =
    [
        (RoleIds.Admin, RoleCodes.Admin),
        (RoleIds.Manager, RoleCodes.Manager),
        (RoleIds.Driver, RoleCodes.Driver),
        (RoleIds.Passenger, RoleCodes.Passenger),
    ];

    private static async Task<UserEntity> SeedUserAsync(TestAppFactory factory, Guid roleId, string roleCode)
    {
        await factory.SeedAsync(db =>
        {
            if (!db.Roles.Any(r => r.Id == roleId || r.Code == roleCode))
            {
                db.Roles.Add(new Role { Id = roleId, Code = roleCode, Name = roleCode });
            }
        });

        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "09" + Random.Shared.Next(10_000_000, 99_999_999),
            FullName = "Người Dùng Test",
            PasswordHash = PasswordService.HashPassword("matkhau123"),
            IsActive = true,
            RoleId = roleId,
            // Vai trò chính phải luôn có mặt ở bảng nối — đúng bất biến mà AuthService giữ.
            UserRoles = [new UserRole { RoleId = roleId }],
        };

        await factory.SeedAsync(db => db.Users.Add(user));

        return user;
    }

    private static async Task<RouteEntity> SeedRouteAsync(TestAppFactory factory, string code = "01")
    {
        var route = new RouteEntity
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = "Bến xe Mỹ Đình — Bến xe Gia Lâm",
            Origin = "Bến xe Mỹ Đình",
            Destination = "Bến xe Gia Lâm",
        };

        await factory.SeedAsync(db => db.Routes.Add(route));

        return route;
    }

    private static async Task<Bus> SeedBusAsync(
        TestAppFactory factory,
        string licensePlate = "29B-123.45",
        string busType = "Hyundai County 29 chỗ",
        int capacity = 29,
        BusStatus status = BusStatus.Active)
    {
        var bus = new Bus
        {
            Id = Guid.NewGuid(),
            LicensePlate = licensePlate,
            BusType = busType,
            Capacity = capacity,
            Status = status,
        };

        await factory.SeedAsync(db => db.Buses.Add(bus));

        return bus;
    }

    private static async Task<Trip> SeedTripAsync(
        TestAppFactory factory,
        Guid routeId,
        Guid busId,
        DateTime? departureTime = null,
        DateTime? arrivalTime = null,
        TripStatus status = TripStatus.Scheduled)
    {
        // Chỉ gán khoá ngoại, KHÔNG gán navigation: Route/Bus được seed ở scope khác, gán navigation
        // vào đây sẽ khiến EF tưởng chúng là bản ghi mới và chèn trùng khoá chính.
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            BusId = busId,
            DepartureTime = departureTime ?? DefaultDeparture,
            ArrivalTime = arrivalTime,
            Status = status,
        };

        await factory.SeedAsync(db => db.Trips.Add(trip));

        return trip;
    }

    /// <summary>Seed một trạm rồi gán luôn vào tuyến với thứ tự cho trước.</summary>
    private static async Task<RouteStop> SeedStopOnRouteAsync(
        TestAppFactory factory,
        Guid routeId,
        string stopName,
        int stopOrder,
        decimal distanceKm = 0m)
    {
        var stop = new Stop
        {
            Id = Guid.NewGuid(),
            Name = stopName,
            Address = "Số 1 Cầu Giấy, Hà Nội",
            Latitude = 21.0307,
            Longitude = 105.8034,
        };

        await factory.SeedAsync(db => db.Stops.Add(stop));

        var routeStop = new RouteStop
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            StopId = stop.Id,
            StopOrder = stopOrder,
            DistanceKm = distanceKm,
        };

        await factory.SeedAsync(db => db.RouteStops.Add(routeStop));

        return routeStop;
    }

    /// <summary>Bộ ba tối thiểu để một chuyến xem được: tuyến có thật, xe có thật, chuyến của hai thứ đó.</summary>
    private static async Task<(Trip Trip, RouteEntity Route, Bus Bus)> SeedTripWithRouteAndBusAsync(
        TestAppFactory factory)
    {
        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory);
        var trip = await SeedTripAsync(factory, route.Id, bus.Id);

        return (trip, route, bus);
    }
}
