using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SmartBus.Api.Entities;
using RouteEntity = SmartBus.Api.Entities.Route;

namespace SmartBus.Tests;

/// <summary>
/// Test tích hợp cho API sơ đồ ghế theo chuyến — <c>GET /api/trips/{id}/seats</c>
/// (task *"API lấy sơ đồ ghế theo chuyến + trạng thái từng ghế"* — Trần Trung Hiếu, story 2).
///
/// Dùng lại <see cref="TestAppFactory"/> của JwtAuthTests: chạy trên app thật, mỗi test một
/// CSDL InMemory riêng. Endpoint CÔNG KHAI nên file này không có phần phân quyền — khác hẳn
/// TripDetailApiTests (Admin/Manager).
///
/// ⚠️ Giới hạn của provider InMemory ảnh hưởng tới cách đọc kết quả ở đây:
///   • KHÔNG dựng khoá ngoại Restrict, nên seed được chuyến trỏ tới xe không tồn tại — đó chính là
///     cách dựng ca "dữ liệu mồ côi". Ở PostgreSQL thật những dòng đó không tồn tại được, nhưng
///     nhánh 404 vẫn phải có vì join trong là thứ quyết định, không phải CSDL.
///   • KHÔNG dựng unique index, nên không có test nào khẳng định được partial index
///     (TripId, SeatId) WHERE Status = 'Holding' — chốt chống trùng ghế đó chỉ tồn tại ở tầng
///     PostgreSQL, xem docs/26-csdl-so-do-ghe.md §3.
/// </summary>
public class TripSeatMapApiTests
{
    // ---------------------------------------------------------------------------------------
    // Công khai
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Khong_can_dang_nhap_van_xem_duoc_so_do()
    {
        using var factory = new TestAppFactory();
        var (trip, _, _) = await SeedTripWithFullDataAsync(factory);

        var response = await factory.CreateClient().GetAsync(SeatMapUrl(trip.Id));

        // Cùng lối GET /trips/search: hành khách xem sơ đồ ghế để chọn chỗ TRƯỚC khi đăng nhập,
        // đăng nhập là bước của API giữ ghế (US 3).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------------
    // Nội dung sơ đồ — thứ story yêu cầu: dàn ghế của xe + trạng thái từng ghế
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Tra_du_so_do_ghe_va_gia_pho_thong()
    {
        using var factory = new TestAppFactory();
        var (trip, bus, seats) = await SeedTripWithFullDataAsync(factory);

        var body = await ReadJsonAsync(await factory.CreateClient().GetAsync(SeatMapUrl(trip.Id)));

        Assert.Equal(trip.Id, body.GetProperty("tripId").GetGuid());
        Assert.Equal("Xe buýt 45 chỗ", body.GetProperty("busType").GetString());
        Assert.Equal(1, body.GetProperty("floors").GetInt32());
        Assert.Equal(7000m, body.GetProperty("pricePerSeat").GetDecimal());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("vipSurcharge").ValueKind);

        var seatArray = body.GetProperty("seats");
        Assert.Equal(seats.Count, seatArray.GetArrayLength());

        var firstSeat = seatArray[0];
        Assert.Equal("A1", firstSeat.GetProperty("seatNumber").GetString());
        Assert.Equal("Vip", firstSeat.GetProperty("seatType").GetString());
        Assert.Equal(7000m, firstSeat.GetProperty("price").GetDecimal());
        Assert.Equal(seats[0].Id, firstSeat.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Ghe_dang_giu_thi_Held_con_lai_Available()
    {
        using var factory = new TestAppFactory();
        var (trip, _, seats) = await SeedTripWithFullDataAsync(factory);

        await SeedHoldAsync(factory, trip.Id, seats[1].Id, SeatHoldStatus.Holding);

        var body = await ReadJsonAsync(await factory.CreateClient().GetAsync(SeatMapUrl(trip.Id)));
        var seatArray = body.GetProperty("seats");

        Assert.Equal("Available", seatArray[0].GetProperty("status").GetString());
        Assert.Equal("Held", seatArray[1].GetProperty("status").GetString());
        Assert.Equal("Available", seatArray[2].GetProperty("status").GetString());
    }

    [Theory]
    [InlineData(SeatHoldStatus.Expired)]
    [InlineData(SeatHoldStatus.Released)]
    [InlineData(SeatHoldStatus.Confirmed)]
    public async Task Hold_het_han_hoac_da_nha_khong_chan_ghe(SeatHoldStatus status)
    {
        using var factory = new TestAppFactory();
        var (trip, _, seats) = await SeedTripWithFullDataAsync(factory);

        await SeedHoldAsync(factory, trip.Id, seats[0].Id, status);

        var body = await ReadJsonAsync(await factory.CreateClient().GetAsync(SeatMapUrl(trip.Id)));

        // Chỉ Status = Holding mới chặn ghế — đúng điều kiện của partial unique index ở
        // AppDbContext.Seat.cs. Hold đã hết hạn / đã nhả / đã thành vé không giữ ghế nữa.
        Assert.Equal("Available", body.GetProperty("seats")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Hold_cua_chuyen_khac_khong_anh_huong_den_chuyen_dang_xem()
    {
        using var factory = new TestAppFactory();
        var (trip, bus, seats) = await SeedTripWithFullDataAsync(factory);
        var otherTrip = await SeedTripAsync(factory, routeId: trip.RouteId, busId: bus.Id);

        // Cùng một xe, cùng một ghế — nhưng giữ cho CHUYẾN KHÁC. Trạng thái là của cặp (chuyến,
        // ghế): giữ cho chuyến kia không được chặn ghế của chuyến này.
        await SeedHoldAsync(factory, otherTrip.Id, seats[0].Id, SeatHoldStatus.Holding);

        var body = await ReadJsonAsync(await factory.CreateClient().GetAsync(SeatMapUrl(trip.Id)));

        Assert.Equal("Available", body.GetProperty("seats")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Thu_tu_ghe_xep_theo_tang_hang_cot_du_luu_nguoc()
    {
        using var factory = new TestAppFactory();
        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory);
        var layout = await SeedLayoutAsync(factory, bus.BusType);
        var trip = await SeedTripAsync(factory, route.Id, bus.Id);

        // Seed cố ý NGƯỢC thứ tự vẽ: ghế tầng 2 hàng 2 nằm trước ghế tầng 1 hàng 1 trong CSDL.
        // Nếu service quên orderby thì thứ tự trả về là thứ tự chèn, không phải thứ tự vẽ sơ đồ.
        var seat2_2 = await SeedSeatAsync(factory, bus.Id, layout.Id, floor: 2, row: 2, column: 1, "T2-B1");
        var seat1_2 = await SeedSeatAsync(factory, bus.Id, layout.Id, floor: 1, row: 2, column: 1, "B1");
        var seat1_1 = await SeedSeatAsync(factory, bus.Id, layout.Id, floor: 1, row: 1, column: 1, "A1");

        var body = await ReadJsonAsync(await factory.CreateClient().GetAsync(SeatMapUrl(trip.Id)));
        var ids = SeatIdsOf(body);

        Assert.Equal([seat1_1.Id, seat1_2.Id, seat2_2.Id], ids);
    }

    [Fact]
    public async Task Loai_xe_chua_co_so_do_thi_tra_mang_rong_va_floors_0()
    {
        using var factory = new TestAppFactory();
        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory);
        var trip = await SeedTripAsync(factory, route.Id, bus.Id);

        var response = await factory.CreateClient().GetAsync(SeatMapUrl(trip.Id));
        var body = await ReadJsonAsync(response);

        // Xe chưa có sơ đồ cho loại của nó thì chưa sinh được ghế — trạng thái dữ liệu HỢP LỆ
        // (docs/26 §1), không phải "không tìm thấy".
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, body.GetProperty("floors").GetInt32());
        Assert.Equal(0, body.GetProperty("seats").GetArrayLength());
    }

    [Fact]
    public async Task Tuyen_chua_cau_hinh_gia_thi_pricePerSeat_va_price_null()
    {
        using var factory = new TestAppFactory();
        var (trip, _, _) = await SeedTripWithFullDataAsync(factory, seedFare: false);

        var body = await ReadJsonAsync(await factory.CreateClient().GetAsync(SeatMapUrl(trip.Id)));

        // Thiếu giá là trạng thái dữ liệu bình thường — cùng lối price của GET /trips/search.
        Assert.Equal(JsonValueKind.Null, body.GetProperty("pricePerSeat").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("seats")[0].GetProperty("price").ValueKind);
    }

    // ---------------------------------------------------------------------------------------
    // Hình dạng response — khoá lại đúng những gì docs/api-contract.md đã chốt (⛔5)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Response_chi_tra_dung_cac_truong_trong_hop_dong()
    {
        using var factory = new TestAppFactory();
        var (trip, _, _) = await SeedTripWithFullDataAsync(factory);

        var body = await ReadJsonAsync(await factory.CreateClient().GetAsync(SeatMapUrl(trip.Id)));

        // Thêm một trường vào response là ĐỔI HÌNH DẠNG API: theo ⛔5 phải sửa api-contract.md
        // trước rồi báo người viết frontend. Ca này làm đổ test ngay lúc đó, để việc sửa hợp đồng
        // là một quyết định có ý thức chứ không phải một dòng code lỡ tay.
        Assert.Equal(
            ["busType", "floors", "pricePerSeat", "seats", "tripId", "vipSurcharge"],
            PropertyNamesOf(body));

        Assert.Equal(
            ["columnIndex", "floor", "id", "price", "rowIndex", "seatNumber", "seatType", "status"],
            PropertyNamesOf(body.GetProperty("seats")[0]));
    }

    // ---------------------------------------------------------------------------------------
    // Không tìm thấy
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Chuyen_khong_ton_tai_thi_tra_404()
    {
        using var factory = new TestAppFactory();

        var response = await factory.CreateClient().GetAsync(SeatMapUrl(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("chuyến", await MessageAsync(response), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Xe_cua_chuyen_khong_con_thi_tra_404()
    {
        using var factory = new TestAppFactory();

        // Chuyến trỏ tới xe không tồn tại. InMemory không dựng khoá ngoại nên seed được dòng này;
        // ở PostgreSQL thật khoá ngoại Restrict chặn từ trước nên không có dòng mồ côi.
        var route = await SeedRouteAsync(factory);
        var trip = await SeedTripAsync(factory, route.Id, busId: Guid.NewGuid());

        var response = await factory.CreateClient().GetAsync(SeatMapUrl(trip.Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Id_khong_phai_guid_thi_tra_404()
    {
        using var factory = new TestAppFactory();

        // Ràng buộc {id:guid} trên route: chuỗi không phải GUID không khớp endpoint nào cả.
        var response = await factory.CreateClient().GetAsync("/api/trips/khong-phai-guid/seats");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------------
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // ---------------------------------------------------------------------------------------

    private static string SeatMapUrl(Guid tripId) => $"/api/trips/{tripId}/seats";

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        var body = await ReadJsonAsync(response);
        return body.GetProperty("message").GetString() ?? string.Empty;
    }

    private static string[] PropertyNamesOf(JsonElement element)
        => element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();

    private static Guid[] SeatIdsOf(JsonElement body)
        => body.GetProperty("seats").EnumerateArray()
            .Select(seat => seat.GetProperty("id").GetGuid())
            .ToArray();

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
        string busType = "Xe buýt 45 chỗ")
    {
        var bus = new Bus
        {
            Id = Guid.NewGuid(),
            LicensePlate = "29B-123.45",
            BusType = busType,
            Capacity = 45,
        };

        await factory.SeedAsync(db => db.Buses.Add(bus));

        return bus;
    }

    private static async Task<SeatLayout> SeedLayoutAsync(TestAppFactory factory, string busType)
    {
        var layout = new SeatLayout
        {
            Id = Guid.NewGuid(),
            BusType = busType,
            NumberOfFloors = 1,
            RowsPerFloor = 9,
            ColumnsPerRow = 5,
            TotalSeats = 45,
            VipSeatPositions = "1-1-1;1-1-2",
        };

        await factory.SeedAsync(db => db.SeatLayouts.Add(layout));

        return layout;
    }

    private static async Task<Seat> SeedSeatAsync(
        TestAppFactory factory,
        Guid busId,
        Guid seatLayoutId,
        int floor,
        int row,
        int column,
        string seatNumber,
        SeatType seatType = SeatType.Standard)
    {
        var seat = new Seat
        {
            Id = Guid.NewGuid(),
            BusId = busId,
            SeatLayoutId = seatLayoutId,
            Floor = floor,
            RowIndex = row,
            ColumnIndex = column,
            SeatNumber = seatNumber,
            SeatType = seatType,
        };

        await factory.SeedAsync(db => db.Seats.Add(seat));

        return seat;
    }

    private static async Task<Trip> SeedTripAsync(
        TestAppFactory factory,
        Guid routeId,
        Guid busId)
    {
        // Chỉ gán khoá ngoại, KHÔNG gán navigation: Route/Bus được seed ở scope khác, gán navigation
        // vào đây sẽ khiến EF tưởng chúng là bản ghi mới và chèn trùng khoá chính.
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            BusId = busId,
            DepartureTime = new DateTime(2026, 10, 1, 1, 0, 0, DateTimeKind.Utc),
        };

        await factory.SeedAsync(db => db.Trips.Add(trip));

        return trip;
    }

    private static async Task SeedHoldAsync(
        TestAppFactory factory,
        Guid tripId,
        Guid seatId,
        SeatHoldStatus status)
    {
        // UserId không trỏ tới tài khoản thật — InMemory không cưỡng chế khoá ngoại nên không cần
        // seed Users cho ca này (đúng lệ "seed dòng mồ côi" của TripDetailApiTests).
        var hold = new SeatHold
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            SeatId = seatId,
            UserId = Guid.NewGuid(),
            SessionCode = $"PHIEN-{Guid.NewGuid():N}",
            Status = status,
            ExpiresAt = new DateTime(2026, 10, 1, 1, 10, 0, DateTimeKind.Utc),
        };

        await factory.SeedAsync(db => db.SeatHolds.Add(hold));
    }

    /// <summary>
    /// Nền dữ liệu đầy đủ: tuyến + giá phổ thông + xe + sơ đồ + ba ghế (ghế đầu là VIP) + chuyến.
    /// </summary>
    private static async Task<(Trip Trip, Bus Bus, List<Seat> Seats)> SeedTripWithFullDataAsync(
        TestAppFactory factory,
        bool seedFare = true)
    {
        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory);
        var layout = await SeedLayoutAsync(factory, bus.BusType);
        var trip = await SeedTripAsync(factory, route.Id, bus.Id);

        if (seedFare)
        {
            var fare = new Fare
            {
                Id = Guid.NewGuid(),
                RouteId = route.Id,
                PassengerType = PassengerType.Standard,
                Price = 7000m,
            };

            await factory.SeedAsync(db => db.Fares.Add(fare));
        }

        var seats = new List<Seat>
        {
            await SeedSeatAsync(factory, bus.Id, layout.Id, floor: 1, row: 1, column: 1, "A1", SeatType.Vip),
            await SeedSeatAsync(factory, bus.Id, layout.Id, floor: 1, row: 1, column: 2, "A2", SeatType.Vip),
            await SeedSeatAsync(factory, bus.Id, layout.Id, floor: 1, row: 1, column: 3, "A3"),
        };

        return (trip, bus, seats);
    }
}
