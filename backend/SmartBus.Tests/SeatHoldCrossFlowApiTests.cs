using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Entities;
using RouteEntity = SmartBus.Api.Entities.Route;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Kiểm thử CHÉO cả luồng giữ chỗ — năm endpoint của hai bề mặt
/// (<c>GET /api/trips/{id}/seats</c> + bốn endpoint <c>/api/seat-holds</c>) đi liền một mạch, thay
/// vì mỗi endpoint một file như bộ test hiện có.
///
/// Task *"Kiểm thử chéo luồng giữ chỗ trên môi trường online"* (dòng 26 Sprint 3, Giàng A Vàng) —
/// story 3 *"Giữ chỗ tạm thời"*. Môi trường online **chưa tồn tại** (báo cáo kèm theo,
/// <c>docs/bao-cao-kiem-thu-cheo-giu-cho.md</c> mục 1), nên đây là phần thay thế chạy được:
/// app THẬT qua <see cref="TestAppFactory"/> (app thật + EF InMemory, mỗi ca một CSDL riêng).
///
/// **Vì sao cần file này dù đã có năm file test cho cùng bề mặt:**
/// <c>SeatHoldCreateApiTests</c>, <c>SeatHoldExtendApiTests</c>, <c>SeatHoldLookupApiTests</c>,
/// <c>SeatHoldReleaseApiTests</c> và <c>TripSeatMapApiTests</c> mỗi file khoá MỘT endpoint. Không
/// file nào khẳng định được điều mà luồng thật hứa: *giữ ghế xong thì sơ đồ phải đổi màu, nhả
/// xong thì sơ đồ phải trả ghế về trống* — tức là hai bề mặt **nhìn thấy nhau**. Đó đúng là phần
/// "chéo", và nó chỉ sai khi cả hai bên đều đúng riêng lẻ.
///
/// ⚠️ Giới hạn của provider InMemory, đọc kèm để không hiểu sai màu xanh: InMemory **bỏ qua mọi
/// unique index**, gồm cả partial unique index chống trùng ghế <c>(TripId, SeatId) WHERE Status =
/// 'Holding'</c> (docs/26 §3) — hai request đồng thời cùng giữ một ghế KHÔNG được phủ ở đây. Các ca
/// dưới đây vì vậy luôn dựng ghế ở trạng thái đã có sẵn rồi mới gọi, và nhánh <c>DbUpdateException</c>
/// của service không nằm trong tầm với. Phần đua thật nằm ở nhánh
/// <c>feature/3-kiem-thu-dong-thoi-50-khach-cung-giu-1-ghe</c> (SQLite — engine duy nhất máy này
/// cưỡng chế được index).
///
/// ⚠️ File này cố ý **KHÔNG** khoá trạng thái <c>"Paid"</c> của ghế đã bán. Đo trên bản đã merge:
/// seed một dòng <c>Tickets</c> <c>Status = Paid</c> cho <c>(chuyến, ghế)</c> thì
/// <c>GET /trips/{id}/seats</c> vẫn trả <c>"Available"</c> và <c>POST /seat-holds</c> trên ghế đó
/// vẫn <b>201</b> — <c>TripSeatMapService</c> không hỏi bảng <c>Tickets</c>. Đó là LỖI có thật, đã
/// ghi ở báo cáo kèm theo (G1) và phải do người sở hữu service sửa; viết một ca đỏ ở đây là biến
/// lỗi thành chốt hồi quy cho hành vi SAI (quy ước 2.4: test đỏ thì báo người viết code, không tự
/// sửa cho xanh).
/// </summary>
public class SeatHoldCrossFlowApiTests
{
    private const string SeatHoldsUrl = "/api/seat-holds";

    // ---------------------------------------------------------------------------------------
    // 1. Luồng trọn vẹn — hai bề mặt nhìn thấy nhau
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Luong_giu_cho_tron_ven_tu_so_do_toi_khi_nha_ghe()
    {
        using var factory = new TestAppFactory();
        var (trip, _, seats) = await SeedTripWithFullDataAsync(factory);
        var khach = await SeedUserAsync(factory);
        var khachKhac = await SeedUserAsync(factory);
        var client = ClientWith(factory, factory.CreateTokenFor(khach));
        var clientKhac = ClientWith(factory, factory.CreateTokenFor(khachKhac));
        var soDo = factory.CreateClient();

        var a1 = seats[0];

        // (1) Sơ đồ ghế đọc được KHÔNG cần token (US 2 chốt: khách chọn chỗ trước khi đăng nhập).
        var truoc = await SoDoCuaGheAsync(soDo, trip.Id, a1.Id);
        Assert.Equal("Available", truoc);

        // (2) Giữ ghế — 201 kèm phiên Holding.
        var tao = await client.PostAsJsonAsync(SeatHoldsUrl, new { tripId = trip.Id, seatIds = new[] { a1.Id } });
        Assert.Equal(HttpStatusCode.Created, tao.StatusCode);

        var phien = await ReadJsonAsync(tao);
        var maPhien = phien.GetProperty("sessionCode").GetString()!;
        Assert.Equal("Holding", phien.GetProperty("status").GetString());
        Assert.True(phien.GetProperty("canExtend").GetBoolean());

        // (3) Tra trạng thái theo mã phiên — chính chủ 200, người khác 404 (không lộ là phiên có thật).
        var tra = await client.GetAsync($"{SeatHoldsUrl}/{maPhien}");
        Assert.Equal(HttpStatusCode.OK, tra.StatusCode);

        var traNguoiKhac = await clientKhac.GetAsync($"{SeatHoldsUrl}/{maPhien}");
        Assert.Equal(HttpStatusCode.NotFound, traNguoiKhac.StatusCode);

        // (4) ĐÂY LÀ PHẦN CHÉO: sơ đồ ghế phải đổi màu vì lượt giữ vừa rồi.
        Assert.Equal("Held", await SoDoCuaGheAsync(soDo, trip.Id, a1.Id));

        // (5) Gia hạn lần đầu 200, hết lượt (canExtend = false); lần hai 409.
        var giaHan = await client.PostAsync($"{SeatHoldsUrl}/{maPhien}/extend", null);
        Assert.Equal(HttpStatusCode.OK, giaHan.StatusCode);
        Assert.False((await ReadJsonAsync(giaHan)).GetProperty("canExtend").GetBoolean());

        var giaHanLai = await client.PostAsync($"{SeatHoldsUrl}/{maPhien}/extend", null);
        Assert.Equal(HttpStatusCode.Conflict, giaHanLai.StatusCode);

        // (6) Nhả ghế — 200, và GỌI LẠI vẫn 200 (nhả là thao tác idempotent, khác gia hạn).
        var nha = await client.PostAsync($"{SeatHoldsUrl}/{maPhien}/release", null);
        Assert.Equal(HttpStatusCode.OK, nha.StatusCode);
        Assert.Equal("Released", (await ReadJsonAsync(nha)).GetProperty("status").GetString());

        var nhaiLai = await client.PostAsync($"{SeatHoldsUrl}/{maPhien}/release", null);
        Assert.Equal(HttpStatusCode.OK, nhaiLai.StatusCode);

        // (7) PHẦN CHÉO thứ hai: nhả xong sơ đồ phải trả ghế về trống — không thì ghế bị bỏ quên.
        Assert.Equal("Available", await SoDoCuaGheAsync(soDo, trip.Id, a1.Id));

        // (8) Người khác nhả phiên của mình — vẫn 404, không có đường tắt.
        Assert.Equal(HttpStatusCode.NotFound,
            (await clientKhac.PostAsync($"{SeatHoldsUrl}/{maPhien}/release", null)).StatusCode);
    }

    // ---------------------------------------------------------------------------------------
    // 2. Phân quyền trên cả bề mặt
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Ba_endpoint_theo_phien_tra_401_khi_khong_co_token()
    {
        using var factory = new TestAppFactory();
        var khongToken = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await khongToken.GetAsync($"{SeatHoldsUrl}/PHIEN-BAT-KY")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await khongToken.PostAsync($"{SeatHoldsUrl}/PHIEN-BAT-KY/extend", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await khongToken.PostAsync($"{SeatHoldsUrl}/PHIEN-BAT-KY/release", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await khongToken.PostAsJsonAsync(SeatHoldsUrl, new { tripId = Guid.NewGuid(), seatIds = new[] { Guid.NewGuid() } })).StatusCode);
    }

    /// <summary>
    /// Cả bốn vai trò của dự án đều giữ ghế được: <c>[Authorize]</c> trần, không policy vai trò
    /// (docs/api-contract.md, mục "Giữ chỗ"). Ca này khoá đúng lời hứa đó — và là ca sẽ đỏ nếu ai
    /// đó gắn <c>[Authorize(Policy = RbacPolicies.PassengerOnly)]</c> lên bề mặt này.
    /// </summary>
    [Theory]
    [InlineData("44444444-4444-4444-4444-444444444444")] // Passenger
    [InlineData("33333333-3333-3333-3333-333333333333")] // Driver
    [InlineData("22222222-2222-2222-2222-222222222222")] // Manager
    [InlineData("11111111-1111-1111-1111-111111111111")] // Admin
    public async Task Moi_vai_tro_dang_nhap_deu_giu_duoc_ghe(string vaiTro)
    {
        using var factory = new TestAppFactory();
        var (trip, _, seats) = await SeedTripWithFullDataAsync(factory);
        var nguoiDung = await SeedUserAsync(factory, Guid.Parse(vaiTro));
        var client = ClientWith(factory, factory.CreateTokenFor(nguoiDung));

        var response = await client.PostAsJsonAsync(SeatHoldsUrl, new { tripId = trip.Id, seatIds = new[] { seats[0].Id } });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Holding", (await ReadJsonAsync(response)).GetProperty("status").GetString());
    }

    // ---------------------------------------------------------------------------------------
    // 3. Mã phiên và thứ tự ghế trong phiên
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Ma_phien_theo_khuon_PHIEN_cong_tam_ky_tu_hex()
    {
        using var factory = new TestAppFactory();
        var (trip, _, seats) = await SeedTripWithFullDataAsync(factory);
        var client = await SignInAsPassengerAsync(factory);

        var response = await client.PostAsJsonAsync(SeatHoldsUrl, new { tripId = trip.Id, seatIds = new[] { seats[0].Id } });
        var maPhien = (await ReadJsonAsync(response)).GetProperty("sessionCode").GetString()!;

        // Khuôn của hợp đồng (ví dụ "PHIEN-8f3a2c1d"): tiền tố + đúng 8 ký tự hex.
        Assert.StartsWith("PHIEN-", maPhien, StringComparison.Ordinal);
        var duoi = maPhien["PHIEN-".Length..];
        Assert.Equal(8, duoi.Length);
        Assert.All(duoi, kyTu => Assert.Contains(kyTu, "0123456789abcdef"));
    }

    [Fact]
    public async Task Seat_numbers_sap_theo_toa_do_du_khach_gui_dao_thu_tu()
    {
        using var factory = new TestAppFactory();
        var (trip, _, seats) = await SeedTripWithFullDataAsync(factory);
        var client = await SignInAsPassengerAsync(factory);

        // Gửi lên ĐẢO THỨ TỰ (A3, A1, A2) — hợp đồng hứa trả về theo toạ độ sơ đồ, không theo
        // thứ tự khách bấm, vì màn hình hiển thị nhóm ghế đúng chỗ trên sơ đồ.
        var response = await client.PostAsJsonAsync(SeatHoldsUrl,
            new { tripId = trip.Id, seatIds = new[] { seats[2].Id, seats[0].Id, seats[1].Id } });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(["A1", "A2", "A3"], SeatNumbersOf(await ReadJsonAsync(response)));
    }

    // ---------------------------------------------------------------------------------------
    // 4. Công thức gia hạn và nguồn giá — hai thứ chỉ đọc được qua hành vi, không qua hình dạng
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Hợp đồng chốt: "Hạn mới = thời điểm gia hạn + 10 phút, KHÔNG phải hạn cũ + 10 phút"
    /// (docs/api-contract.md, mục extend — có hẳn một khối "vì sao" cho lựa chọn này). Ca này dựng
    /// một phiên còn hạn 1 phút rồi gia hạn, để hai công thức cho ra hai kết quả KHÁC NHAU — dựng
    /// phiên mới tinh rồi gia hạn ngay thì hai công thức trùng nhau và ca test không phân biệt được gì.
    /// </summary>
    [Fact]
    public async Task Han_moi_cua_gia_han_tinh_tu_luc_goi_chu_khong_phai_han_cu()
    {
        using var factory = new TestAppFactory();
        var (trip, _, seats) = await SeedTripWithFullDataAsync(factory);
        var khach = await SeedUserAsync(factory);
        var client = ClientWith(factory, factory.CreateTokenFor(khach));

        var maPhien = "PHIEN-HAN-CU-1";
        var hanCu = DateTime.UtcNow.AddMinutes(1);

        await factory.SeedAsync(db => db.SeatHolds.Add(new SeatHold
        {
            Id = Guid.NewGuid(),
            TripId = trip.Id,
            SeatId = seats[0].Id,
            UserId = khach.Id,
            SessionCode = maPhien,
            Status = SeatHoldStatus.Holding,
            ExpiresAt = hanCu,
        }));

        var truoc = DateTime.UtcNow;
        var response = await client.PostAsync($"{SeatHoldsUrl}/{maPhien}/extend", null);
        var hanMoi = (await ReadJsonAsync(response)).GetProperty("expiresAt").GetDateTime();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Cửa sổ 10 phút được đếm lại từ LÚC GỌI, nên hạn mới phải rất gần `truoc + 10 phút`, và
        // cách xa `hanCu + 10 phút` đúng bằng khoảng đã trôi từ lúc dựng phiên tới lúc gia hạn.
        Assert.True(
            Math.Abs((hanMoi - truoc.AddMinutes(10)).TotalSeconds) < 5,
            $"Hạn mới {hanMoi:O} không tính từ lúc gọi ({truoc.AddMinutes(10):O}).");
        Assert.True(
            (hanCu.AddMinutes(10) - hanMoi).TotalSeconds > 30,
            $"Hạn mới {hanMoi:O} trùng với 'hạn cũ + 10 phút' ({hanCu.AddMinutes(10):O}) — nghi cộng dồn vào hạn cũ.");
    }

    /// <summary>
    /// Giá phổ thông lấy đúng bản <c>PassengerType.Standard</c> — tuyến có nhiều mức giá (sinh viên,
    /// người cao tuổi…) là chuyện bình thường, và sơ đồ ghế chỉ được đọc một mức.
    /// </summary>
    [Fact]
    public async Task Gia_pho_thong_lay_dung_ban_Standard_khi_tuyen_co_nhieu_muc_gia()
    {
        using var factory = new TestAppFactory();
        var (trip, _, seats) = await SeedTripWithFullDataAsync(factory);
        var soDo = factory.CreateClient();

        await factory.SeedAsync(db => db.Fares.Add(new Fare
        {
            Id = Guid.NewGuid(),
            RouteId = trip.RouteId,
            PassengerType = PassengerType.Student,
            Price = 5000m,
        }));

        var body = await ReadJsonAsync(await soDo.GetAsync(SeatMapUrl(trip.Id)));

        Assert.Equal(7000m, body.GetProperty("pricePerSeat").GetDecimal());

        var a1 = GheTrongSoDo(body, seats[0].Id);
        Assert.Equal(7000m, a1.GetProperty("price").GetDecimal());
    }

    /// <summary>
    /// Tuyến chưa cấu hình giá là trạng thái dữ liệu BÌNH THƯỜNG, không phải lỗi — sơ đồ vẫn phải
    /// trả về đủ ghế với giá <c>null</c>. Ca này khoá lời hứa "nullable" của hình dạng response
    /// (chỗ mà <c>frontend/src/api/seatMapApi.ts</c> đang khai là số không-null — xem báo cáo G5).
    /// </summary>
    [Fact]
    public async Task Tuyen_chua_cau_hinh_gia_thi_tra_null_chu_khong_phai_loi()
    {
        using var factory = new TestAppFactory();
        var (trip, _, seats) = await SeedTripWithFullDataAsync(factory, seedFare: false);
        var soDo = factory.CreateClient();

        var response = await soDo.GetAsync(SeatMapUrl(trip.Id));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("pricePerSeat").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("vipSurcharge").ValueKind);
        Assert.Equal(JsonValueKind.Null, GheTrongSoDo(body, seats[0].Id).GetProperty("price").ValueKind);
    }

    // ---------------------------------------------------------------------------------------
    // 5. Sơ đồ ghế — số tầng và thứ tự vẽ
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task So_do_hai_tang_tra_du_so_tang_va_sap_theo_tang_roi_hang_roi_cot()
    {
        using var factory = new TestAppFactory();
        var route = await SeedRouteAsync(factory, "02");
        var bus = await SeedBusAsync(factory, "Xe buýt 2 tầng 60 chỗ");
        var layout = await SeedLayoutAsync(factory, bus.BusType, soTang: 2, soHang: 6, soCot: 5, tongGhe: 60);
        var trip = await SeedTripAsync(factory, bus.Id, routeId: route.Id);

        // Seed CỐ Ý lệch thứ tự chèn: tầng 2 trước tầng 1, cột 2 trước cột 1.
        await SeedSeatAsync(factory, bus.Id, layout.Id, 2, 1, 1, "T2-A1");
        await SeedSeatAsync(factory, bus.Id, layout.Id, 1, 1, 2, "T1-A2");
        await SeedSeatAsync(factory, bus.Id, layout.Id, 1, 1, 1, "T1-A1");

        var body = await ReadJsonAsync(await factory.CreateClient().GetAsync(SeatMapUrl(trip.Id)));

        Assert.Equal("Xe buýt 2 tầng 60 chỗ", body.GetProperty("busType").GetString());
        Assert.Equal(2, body.GetProperty("floors").GetInt32());

        // Thứ tự vẽ: tầng → hàng → cột. Tiền tố "T1-"/"T2-" do tầng dữ liệu seed đặt (docs/26 §2)
        // và service chỉ chuyển tiếp nguyên văn — ca này khoá cả hai điều đó cùng lúc.
        var thuTu = body.GetProperty("seats").EnumerateArray()
            .Select(seat => seat.GetProperty("seatNumber").GetString() ?? string.Empty)
            .ToArray();

        Assert.Equal(["T1-A1", "T1-A2", "T2-A1"], thuTu);
    }

    // ---------------------------------------------------------------------------------------
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // ---------------------------------------------------------------------------------------

    private static string SeatMapUrl(Guid tripId) => $"/api/trips/{tripId}/seats";

    /// <summary>Trạng thái của ĐÚNG một ghế trên sơ đồ — phần "chéo" của cả file này.</summary>
    private static async Task<string> SoDoCuaGheAsync(HttpClient client, Guid tripId, Guid seatId)
    {
        var body = await ReadJsonAsync(await client.GetAsync(SeatMapUrl(tripId)));

        return GheTrongSoDo(body, seatId).GetProperty("status").GetString() ?? string.Empty;
    }

    private static JsonElement GheTrongSoDo(JsonElement body, Guid seatId)
        => body.GetProperty("seats").EnumerateArray().First(seat => seat.GetProperty("id").GetGuid() == seatId);

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string[] SeatNumbersOf(JsonElement body)
        => body.GetProperty("seatNumbers").EnumerateArray()
            .Select(number => number.GetString() ?? string.Empty)
            .ToArray();

    private static async Task<HttpClient> SignInAsPassengerAsync(TestAppFactory factory)
    {
        var user = await SeedUserAsync(factory);

        return ClientWith(factory, factory.CreateTokenFor(user));
    }

    private static HttpClient ClientWith(TestAppFactory factory, string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    /// <summary>
    /// Tài khoản kèm vai trò. Provider InMemory KHÔNG chạy <c>HasData</c> nên phải seed vai trò
    /// trước — không thì token hợp lệ mà người dùng trỏ tới vai trò không tồn tại.
    /// </summary>
    private static async Task<UserEntity> SeedUserAsync(TestAppFactory factory, Guid? roleId = null)
    {
        var vaiTro = roleId ?? RoleIds.Passenger;

        await factory.SeedAsync(db =>
        {
            if (!db.Roles.Any(r => r.Id == vaiTro))
            {
                var ma = TenVaiTro(vaiTro);
                db.Roles.Add(new Role { Id = vaiTro, Code = ma, Name = ma });
            }
        });

        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "09" + Random.Shared.Next(10_000_000, 99_999_999),
            FullName = "Hành Khách Test",
            PasswordHash = "hash-khong-dung-toi-trong-test",
            IsActive = true,
            RoleId = vaiTro,
            UserRoles = [new UserRole { RoleId = vaiTro }],
        };

        await factory.SeedAsync(db => db.Users.Add(user));

        return user;
    }

    private static string TenVaiTro(Guid roleId) => roleId == RoleIds.Admin
        ? RoleCodes.Admin
        : roleId == RoleIds.Manager
            ? RoleCodes.Manager
            : roleId == RoleIds.Driver
                ? RoleCodes.Driver
                : RoleCodes.Passenger;

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

    private static async Task<Bus> SeedBusAsync(TestAppFactory factory, string busType = "Xe buýt 45 chỗ")
    {
        var bus = new Bus
        {
            Id = Guid.NewGuid(),
            LicensePlate = "29B-" + Random.Shared.Next(100, 999) + "." + Random.Shared.Next(10, 99),
            BusType = busType,
            Capacity = 45,
        };

        await factory.SeedAsync(db => db.Buses.Add(bus));

        return bus;
    }

    private static async Task<SeatLayout> SeedLayoutAsync(
        TestAppFactory factory,
        string busType,
        int soTang = 1,
        int soHang = 9,
        int soCot = 5,
        int tongGhe = 45)
    {
        var layout = new SeatLayout
        {
            Id = Guid.NewGuid(),
            BusType = busType,
            NumberOfFloors = soTang,
            RowsPerFloor = soHang,
            ColumnsPerRow = soCot,
            TotalSeats = tongGhe,
            VipSeatPositions = "1-1-1",
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

    /// <summary>
    /// Chuyến gắn với xe. <paramref name="routeId"/> mặc định là một Guid MỚI — ca nào cần giá phải
    /// truyền đúng tuyến đã seed <c>Fares</c>, vì <c>TripSeatMapService</c> tra giá theo
    /// <c>Trip.RouteId</c> (lệch một Guid là giá ra null).
    /// </summary>
    private static async Task<Trip> SeedTripAsync(
        TestAppFactory factory,
        Guid busId,
        TripStatus status = TripStatus.Scheduled,
        Guid? routeId = null)
    {
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RouteId = routeId ?? Guid.NewGuid(),
            BusId = busId,
            Status = status,
            DepartureTime = new DateTime(2026, 10, 1, 1, 0, 0, DateTimeKind.Utc),
        };

        await factory.SeedAsync(db => db.Trips.Add(trip));

        return trip;
    }

    /// <summary>
    /// Nền dữ liệu đầy đủ: tuyến + giá phổ thông + xe + sơ đồ + ba ghế (A1 là VIP) + chuyến.
    /// Khác bản của <c>TripSeatMapApiTests</c> ở một điểm: chuyến trỏ về ĐÚNG tuyến vừa seed, nên
    /// giá phổ thông có nguồn — bản kia để chuyến mang RouteId mới nên <c>pricePerSeat</c> luôn null.
    /// </summary>
    private static async Task<(Trip Trip, Bus Bus, List<Seat> Seats)> SeedTripWithFullDataAsync(
        TestAppFactory factory,
        bool seedFare = true)
    {
        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory);
        var layout = await SeedLayoutAsync(factory, bus.BusType);
        var trip = await SeedTripAsync(factory, bus.Id, routeId: route.Id);

        if (seedFare)
        {
            await factory.SeedAsync(db => db.Fares.Add(new Fare
            {
                Id = Guid.NewGuid(),
                RouteId = route.Id,
                PassengerType = PassengerType.Standard,
                Price = 7000m,
            }));
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
