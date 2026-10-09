using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SmartBus.Api.Entities;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test tích hợp cho API kiểm tra trạng thái giữ chỗ theo mã phiên —
/// <c>GET /api/seat-holds/{sessionCode}</c> (task *"API kiểm tra trạng thái giữ chỗ theo mã phiên"*
/// — Trần Trung Hiếu, story 3).
///
/// Dùng lại <see cref="TestAppFactory"/> của JwtAuthTests: chạy trên app thật, mỗi test một
/// CSDL InMemory riêng. Endpoint yêu cầu đăng nhập nên mọi ca đều sign in trước (trừ ca 401).
///
/// ⚠️ Giới hạn của provider InMemory ảnh hưởng tới cách đọc kết quả ở đây:
///   • KHÔNG dựng khoá ngoại, nên seed được dòng SeatHold trỏ tới chuyến/ghế không tồn tại — không
///     ảnh hưởng tới endpoint này vì nó chỉ đọc SeatHolds/SeatHoldLogs/Seats.
///   • KHÔNG dựng unique index (SeatHoldId, Action) của SeatHoldLogs — chốt "gia hạn tối đa 1 lần"
///     ở tầng PostgreSQL không có test nào phủ ở đây, nhưng nhánh canExtend = false theo log
///     Extended thì phủ được (docs/26 §1).
/// </summary>
public class SeatHoldLookupApiTests
{
    /// <summary>Hạn giữ chỗ cố định — dùng DateTimeKind.Utc để chuỗi ISO trả về có hậu tố Z.</summary>
    private static readonly DateTime DefaultExpiresAt = new(2026, 10, 1, 1, 10, 0, DateTimeKind.Utc);

    // ---------------------------------------------------------------------------------------
    // Phân quyền
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Khong_gui_token_thi_tra_401()
    {
        using var factory = new TestAppFactory();

        var response = await factory.CreateClient().GetAsync(SessionUrl("PHIEN-BAT-KY"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------------
    // Quyền sở hữu — phiên của người khác là 404, không phải 403
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Phien_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsPassengerAsync(factory);

        var response = await client.GetAsync(SessionUrl("PHIEN-KHONG-TON-TAI"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("phiên", await MessageAsync(response), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Phien_cua_nguoi_khac_tra_404()
    {
        using var factory = new TestAppFactory();
        var owner = await SeedUserAsync(factory);
        var (trip, seat, _) = await SeedTripWithSeatAsync(factory);
        await SeedHoldAsync(factory, owner.Id, trip.Id, seat.Id, "PHIEN-CUA-NGUOI-KHAC");

        var client = await SignInAsPassengerAsync(factory);

        var response = await client.GetAsync(SessionUrl("PHIEN-CUA-NGUOI-KHAC"));

        // Không xác nhận sự tồn tại của mã phiên cho người không sở hữu — cùng lối phản ánh
        // của tôi (GET /feedbacks/me/{id}) và vé tháng của tôi.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------------
    // Nội dung phiên — thứ màn hình đếm ngược cần: còn sống không, còn bao lâu, còn lượt gia hạn không
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Phien_dang_giu_tra_du_hinh_dang()
    {
        using var factory = new TestAppFactory();
        var owner = await SeedUserAsync(factory);
        var (trip, seatA, seatB) = await SeedTripWithSeatAsync(factory);
        var sessionCode = "PHIEN-DANG-GIU";
        await SeedHoldAsync(factory, owner.Id, trip.Id, seatA.Id, sessionCode);
        await SeedHoldAsync(factory, owner.Id, trip.Id, seatB.Id, sessionCode);

        var client = ClientWith(factory, factory.CreateTokenFor(owner));
        var body = await ReadJsonAsync(await client.GetAsync(SessionUrl(sessionCode)));

        Assert.Equal(sessionCode, body.GetProperty("sessionCode").GetString());
        Assert.Equal(trip.Id, body.GetProperty("tripId").GetGuid());
        Assert.Equal(["A1", "A2"], SeatNumbersOf(body));
        Assert.Equal("Holding", body.GetProperty("status").GetString());
        Assert.Equal(DefaultExpiresAt, body.GetProperty("expiresAt").GetDateTime());
        Assert.True(body.GetProperty("canExtend").GetBoolean());
    }

    [Fact]
    public async Task Seat_numbers_xep_theo_toa_do_so_do_du_luu_nguoc()
    {
        using var factory = new TestAppFactory();
        var owner = await SeedUserAsync(factory);
        var trip = await SeedTripAsync(factory);
        var layoutId = Guid.NewGuid();
        var busId = Guid.NewGuid();

        // Seed cố ý NGƯỢC thứ tự vẽ: ghế tầng 2 nằm trước ghế tầng 1 trong CSDL. Nếu service quên
        // orderby thì số ghế hiển thị theo thứ tự chèn, không theo đúng chỗ trên sơ đồ.
        var seatFloor2 = await SeedSeatAsync(factory, busId, layoutId, floor: 2, row: 1, column: 1, "T2-A1");
        var seatB = await SeedSeatAsync(factory, busId, layoutId, floor: 1, row: 2, column: 1, "B1");
        var seatA = await SeedSeatAsync(factory, busId, layoutId, floor: 1, row: 1, column: 1, "A1");

        var sessionCode = "PHIEN-THU-TU-GHE";
        await SeedHoldAsync(factory, owner.Id, trip.Id, seatFloor2.Id, sessionCode);
        await SeedHoldAsync(factory, owner.Id, trip.Id, seatB.Id, sessionCode);
        await SeedHoldAsync(factory, owner.Id, trip.Id, seatA.Id, sessionCode);

        var client = ClientWith(factory, factory.CreateTokenFor(owner));
        var body = await ReadJsonAsync(await client.GetAsync(SessionUrl(sessionCode)));

        Assert.Equal(["A1", "B1", "T2-A1"], SeatNumbersOf(body));
    }

    [Theory]
    [InlineData(SeatHoldStatus.Expired)]
    [InlineData(SeatHoldStatus.Released)]
    [InlineData(SeatHoldStatus.Confirmed)]
    public async Task Phien_ket_thuc_tra_dung_trang_thai_va_het_luot_gia_han(SeatHoldStatus status)
    {
        using var factory = new TestAppFactory();
        var owner = await SeedUserAsync(factory);
        var (trip, seat, _) = await SeedTripWithSeatAsync(factory);
        var sessionCode = $"PHIEN-{status.ToString().ToUpperInvariant()}";
        await SeedHoldAsync(factory, owner.Id, trip.Id, seat.Id, sessionCode, status);

        var client = ClientWith(factory, factory.CreateTokenFor(owner));
        var body = await ReadJsonAsync(await client.GetAsync(SessionUrl(sessionCode)));

        Assert.Equal(status.ToString(), body.GetProperty("status").GetString());
        Assert.False(body.GetProperty("canExtend").GetBoolean());
    }

    [Fact]
    public async Task Co_log_Extended_thi_canExtend_false()
    {
        using var factory = new TestAppFactory();
        var owner = await SeedUserAsync(factory);
        var (trip, seat, _) = await SeedTripWithSeatAsync(factory);
        var sessionCode = "PHIEN-DA-GIA-HAN";
        var hold = await SeedHoldAsync(factory, owner.Id, trip.Id, seat.Id, sessionCode);

        // Chốt "gia hạn tối đa 1 lần" (US 3) nằm ở unique index (SeatHoldId, Action) của
        // SeatHoldLogs: dòng Extended chỉ ghi được một lần cho mỗi lượt giữ. canExtend suy từ
        // bảng log này, không phải cột đếm trên SeatHolds.
        await SeedLogAsync(factory, hold.Id, owner.Id, sessionCode, SeatHoldLogAction.Extended);

        var client = ClientWith(factory, factory.CreateTokenFor(owner));
        var body = await ReadJsonAsync(await client.GetAsync(SessionUrl(sessionCode)));

        Assert.Equal("Holding", body.GetProperty("status").GetString());
        Assert.False(body.GetProperty("canExtend").GetBoolean());
    }

    [Fact]
    public async Task Trang_thai_lech_thi_uu_tien_Holding()
    {
        using var factory = new TestAppFactory();
        var owner = await SeedUserAsync(factory);
        var (trip, seatA, seatB) = await SeedTripWithSeatAsync(factory);
        var sessionCode = "PHIEN-LECH-TRANG-THAI";

        // Dữ liệu lệch là khe hở có thật (CSDL không chặn) — ưu tiên báo trạng thái "đang chặn
        // ghế" trước, vì đó là điều người gọi cần biết sớm nhất.
        await SeedHoldAsync(factory, owner.Id, trip.Id, seatA.Id, sessionCode, SeatHoldStatus.Expired);
        await SeedHoldAsync(factory, owner.Id, trip.Id, seatB.Id, sessionCode, SeatHoldStatus.Holding);

        var client = ClientWith(factory, factory.CreateTokenFor(owner));
        var body = await ReadJsonAsync(await client.GetAsync(SessionUrl(sessionCode)));

        Assert.Equal("Holding", body.GetProperty("status").GetString());
        Assert.True(body.GetProperty("canExtend").GetBoolean());
    }

    // ---------------------------------------------------------------------------------------
    // Hình dạng response — khoá lại đúng những gì docs/api-contract.md đã chốt (⛔5)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Response_chi_tra_dung_cac_truong_trong_hop_dong()
    {
        using var factory = new TestAppFactory();
        var owner = await SeedUserAsync(factory);
        var (trip, seat, _) = await SeedTripWithSeatAsync(factory);
        await SeedHoldAsync(factory, owner.Id, trip.Id, seat.Id, "PHIEN-KHOA-HINH-DANG");

        var client = ClientWith(factory, factory.CreateTokenFor(owner));
        var body = await ReadJsonAsync(await client.GetAsync(SessionUrl("PHIEN-KHOA-HINH-DANG")));

        // Thêm một trường vào response là ĐỔI HÌNH DẠNG API: theo ⛔5 phải sửa api-contract.md
        // trước rồi báo người viết frontend. Ca này làm đổ test ngay lúc đó, để việc sửa hợp đồng
        // là một quyết định có ý thức chứ không phải một dòng code lỡ tay.
        Assert.Equal(
            ["canExtend", "expiresAt", "seatNumbers", "sessionCode", "status", "tripId"],
            PropertyNamesOf(body));
    }

    // ---------------------------------------------------------------------------------------
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // ---------------------------------------------------------------------------------------

    private static string SessionUrl(string sessionCode) => $"/api/seat-holds/{sessionCode}";

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        var body = await ReadJsonAsync(response);
        return body.GetProperty("message").GetString() ?? string.Empty;
    }

    private static string[] PropertyNamesOf(JsonElement element)
        => element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();

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
    /// Tài khoản hành khách kèm vai trò. Provider InMemory KHÔNG chạy <c>HasData</c> nên phải seed
    /// vai trò trước — không thì token hợp lệ mà người dùng trỏ tới vai trò không tồn tại.
    /// </summary>
    private static async Task<UserEntity> SeedUserAsync(TestAppFactory factory)
    {
        await factory.SeedAsync(db =>
        {
            if (!db.Roles.Any(r => r.Id == RoleIds.Passenger || r.Code == RoleCodes.Passenger))
            {
                db.Roles.Add(new Role { Id = RoleIds.Passenger, Code = RoleCodes.Passenger, Name = RoleCodes.Passenger });
            }
        });

        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "09" + Random.Shared.Next(10_000_000, 99_999_999),
            FullName = "Hành Khách Test",
            PasswordHash = "hash-khong-dung-toi-trong-test",
            IsActive = true,
            RoleId = RoleIds.Passenger,
            UserRoles = [new UserRole { RoleId = RoleIds.Passenger }],
        };

        await factory.SeedAsync(db => db.Users.Add(user));

        return user;
    }

    private static async Task<Trip> SeedTripAsync(TestAppFactory factory)
    {
        // Chỉ gán khoá ngoại, KHÔNG gán navigation — InMemory không cưỡng chế khoá ngoại nên
        // không cần seed Route/Bus cho ca này (đúng lệ "seed dòng mồ côi" của TripDetailApiTests).
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RouteId = Guid.NewGuid(),
            BusId = Guid.NewGuid(),
            DepartureTime = new DateTime(2026, 10, 1, 1, 0, 0, DateTimeKind.Utc),
        };

        await factory.SeedAsync(db => db.Trips.Add(trip));

        return trip;
    }

    private static async Task<Seat> SeedSeatAsync(
        TestAppFactory factory,
        Guid busId,
        Guid seatLayoutId,
        int floor,
        int row,
        int column,
        string seatNumber)
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
        };

        await factory.SeedAsync(db => db.Seats.Add(seat));

        return seat;
    }

    private static async Task<SeatHold> SeedHoldAsync(
        TestAppFactory factory,
        Guid userId,
        Guid tripId,
        Guid seatId,
        string sessionCode,
        SeatHoldStatus status = SeatHoldStatus.Holding)
    {
        var hold = new SeatHold
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            SeatId = seatId,
            UserId = userId,
            SessionCode = sessionCode,
            Status = status,
            ExpiresAt = DefaultExpiresAt,
        };

        await factory.SeedAsync(db => db.SeatHolds.Add(hold));

        return hold;
    }

    private static async Task SeedLogAsync(
        TestAppFactory factory,
        Guid seatHoldId,
        Guid userId,
        string sessionCode,
        SeatHoldLogAction action)
    {
        var log = new SeatHoldLog
        {
            Id = Guid.NewGuid(),
            SeatHoldId = seatHoldId,
            UserId = userId,
            SessionCode = sessionCode,
            Action = action,
        };

        await factory.SeedAsync(db => db.SeatHoldLogs.Add(log));
    }

    /// <summary>Chuyến + hai ghế A1/A2 (tầng 1, hàng 1, cột 1 và 2) — nền tối thiểu cho một phiên.</summary>
    private static async Task<(Trip Trip, Seat SeatA, Seat SeatB)> SeedTripWithSeatAsync(TestAppFactory factory)
    {
        var trip = await SeedTripAsync(factory);
        var layoutId = Guid.NewGuid();
        var busId = Guid.NewGuid();
        var seatA = await SeedSeatAsync(factory, busId, layoutId, floor: 1, row: 1, column: 1, "A1");
        var seatB = await SeedSeatAsync(factory, busId, layoutId, floor: 1, row: 1, column: 2, "A2");

        return (trip, seatA, seatB);
    }
}
