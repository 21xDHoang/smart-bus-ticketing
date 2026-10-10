using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test tích hợp cho API gia hạn thời gian giữ chỗ theo mã phiên —
/// <c>POST /api/seat-holds/{sessionCode}/extend</c> (task *"API gia hạn thời gian giữ chỗ (tối đa
/// 1 lần)"* — Trần Trung Hiếu, story 3).
///
/// Dùng lại <see cref="TestAppFactory"/> của JwtAuthTests: chạy trên app thật, mỗi test một
/// CSDL InMemory riêng. Endpoint yêu cầu đăng nhập nên mọi ca đều sign in trước (trừ ca 401).
///
/// ⚠️ Giới hạn của provider InMemory ảnh hưởng tới cách đọc kết quả ở đây:
///   • KHÔNG dựng khoá ngoại, nên seed được dòng SeatHold trỏ tới chuyến/ghế không tồn tại — không
///     ảnh hưởng tới endpoint này vì nó chỉ đọc/ghi SeatHolds/SeatHoldLogs/Seats.
///   • KHÔNG dựng unique index (SeatHoldId, Action) của SeatHoldLogs — chốt "gia hạn tối đa 1 lần"
///     ở tầng PostgreSQL không có test nào phủ ở đây, nhưng nhánh kiểm tra log Extended ở tầng
///     service thì phủ được (docs/26 §1).
/// </summary>
public class SeatHoldExtendApiTests
{
    /// <summary>Hạn giữ chỗ cũ cố định — dùng DateTimeKind.Utc để chuỗi ISO trả về có hậu tố Z.</summary>
    private static readonly DateTime DefaultExpiresAt = new(2026, 10, 1, 1, 10, 0, DateTimeKind.Utc);

    // ---------------------------------------------------------------------------------------
    // Phân quyền
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Khong_gui_token_thi_tra_401()
    {
        using var factory = new TestAppFactory();

        var response = await factory.CreateClient().PostAsync(ExtendUrl("PHIEN-BAT-KY"), content: null);

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

        var response = await client.PostAsync(ExtendUrl("PHIEN-KHONG-TON-TAI"), content: null);

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

        var response = await client.PostAsync(ExtendUrl("PHIEN-CUA-NGUOI-KHAC"), content: null);

        // Không xác nhận sự tồn tại của mã phiên cho người không sở hữu — cùng lối mục GET.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------------
    // Gia hạn thành công — hạn mới = lúc gia hạn + 10 phút, cả nhóm dòng cùng phiên đổi theo
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Gia_han_thanh_cong_tra_dung_hinh_dang_va_han_moi()
    {
        using var factory = new TestAppFactory();
        var owner = await SeedUserAsync(factory);
        var (trip, seatA, seatB) = await SeedTripWithSeatAsync(factory);
        var sessionCode = "PHIEN-GIA-HAN-THANH-CONG";
        await SeedHoldAsync(factory, owner.Id, trip.Id, seatA.Id, sessionCode);
        await SeedHoldAsync(factory, owner.Id, trip.Id, seatB.Id, sessionCode);

        var beforeCall = DateTime.UtcNow;
        var client = ClientWith(factory, factory.CreateTokenFor(owner));
        var body = await ReadJsonAsync(await client.PostAsync(ExtendUrl(sessionCode), content: null));

        Assert.Equal(sessionCode, body.GetProperty("sessionCode").GetString());
        Assert.Equal(trip.Id, body.GetProperty("tripId").GetGuid());
        Assert.Equal(["A1", "A2"], SeatNumbersOf(body));
        Assert.Equal("Holding", body.GetProperty("status").GetString());
        Assert.False(body.GetProperty("canExtend").GetBoolean());

        // Hạn mới = thời điểm gia hạn + 10 phút, KHÔNG phải hạn cũ + 10 phút (hợp đồng đã chốt).
        // Chừa dung sai vài giây cho thời gian chạy giữa lúc ghi nhận beforeCall và lúc service tính hạn.
        var newExpiresAt = body.GetProperty("expiresAt").GetDateTime();
        Assert.InRange(newExpiresAt, beforeCall.AddMinutes(10).AddSeconds(-5), beforeCall.AddMinutes(10).AddSeconds(60));
        Assert.NotEqual(DefaultExpiresAt, newExpiresAt);
    }

    [Fact]
    public async Task Gia_han_cap_nhat_ca_nhom_dong_cung_phien()
    {
        using var factory = new TestAppFactory();
        var owner = await SeedUserAsync(factory);
        var (trip, seatA, seatB) = await SeedTripWithSeatAsync(factory);
        var sessionCode = "PHIEN-DOI-CA-NHOM";
        await SeedHoldAsync(factory, owner.Id, trip.Id, seatA.Id, sessionCode);
        await SeedHoldAsync(factory, owner.Id, trip.Id, seatB.Id, sessionCode);

        var client = ClientWith(factory, factory.CreateTokenFor(owner));
        var response = await client.PostAsync(ExtendUrl(sessionCode), content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.SeatHolds.Where(h => h.SessionCode == sessionCode).ToListAsync();

        // docs/26 §1: gia hạn đổi theo nhóm — mọi dòng cùng phiên cùng hạn mới, không dòng nào bỏ sót.
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.NotEqual(DefaultExpiresAt, r.ExpiresAt));
        Assert.Equal(rows[0].ExpiresAt, rows[1].ExpiresAt);

        // A4: lượt giữ có sửa dữ liệu nên có UpdatedAt.
        Assert.All(rows, r => Assert.NotNull(r.UpdatedAt));
    }

    [Fact]
    public async Task Gia_han_ghi_log_Extended_cho_moi_luot_giu()
    {
        using var factory = new TestAppFactory();
        var owner = await SeedUserAsync(factory);
        var (trip, seatA, seatB) = await SeedTripWithSeatAsync(factory);
        var sessionCode = "PHIEN-GHI-LOG";
        await SeedHoldAsync(factory, owner.Id, trip.Id, seatA.Id, sessionCode);
        await SeedHoldAsync(factory, owner.Id, trip.Id, seatB.Id, sessionCode);

        var client = ClientWith(factory, factory.CreateTokenFor(owner));
        await client.PostAsync(ExtendUrl(sessionCode), content: null);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logs = await db.SeatHoldLogs.Where(l => l.SessionCode == sessionCode).ToListAsync();

        // docs/26 §6: một dòng Extended cho MỖI lượt giữ vừa gia hạn — bảng nhật ký chỉ có giá trị
        // nếu mọi mốc vòng đời đều được ghi.
        Assert.Equal(2, logs.Count);
        Assert.All(logs, l =>
        {
            Assert.Equal(SeatHoldLogAction.Extended, l.Action);
            Assert.Equal(owner.Id, l.UserId);
            Assert.Equal(sessionCode, l.SessionCode);
        });
    }

    // ---------------------------------------------------------------------------------------
    // Hai lớp chặn của luật "tối đa 1 lần": phiên đã kết thúc và phiên đã gia hạn rồi
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(SeatHoldStatus.Expired)]
    [InlineData(SeatHoldStatus.Released)]
    [InlineData(SeatHoldStatus.Confirmed)]
    public async Task Phien_ket_thuc_thi_tra_409(SeatHoldStatus status)
    {
        using var factory = new TestAppFactory();
        var owner = await SeedUserAsync(factory);
        var (trip, seat, _) = await SeedTripWithSeatAsync(factory);
        var sessionCode = $"PHIEN-{status.ToString().ToUpperInvariant()}";
        await SeedHoldAsync(factory, owner.Id, trip.Id, seat.Id, sessionCode, status);

        var client = ClientWith(factory, factory.CreateTokenFor(owner));
        var response = await client.PostAsync(ExtendUrl(sessionCode), content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("kết thúc", await MessageAsync(response), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Da_gia_han_roi_thi_tra_409()
    {
        using var factory = new TestAppFactory();
        var owner = await SeedUserAsync(factory);
        var (trip, seat, _) = await SeedTripWithSeatAsync(factory);
        var sessionCode = "PHIEN-DA-GIA-HAN";
        var hold = await SeedHoldAsync(factory, owner.Id, trip.Id, seat.Id, sessionCode);

        // Chốt "gia hạn tối đa 1 lần" (US 3) nằm ở unique index (SeatHoldId, Action) của
        // SeatHoldLogs: dòng Extended chỉ ghi được một lần cho mỗi lượt giữ. Tầng service hỏi
        // bảng log này trước khi ghi để trả 409 có thông báo, không để lỗi unique thô lọt ra.
        await SeedLogAsync(factory, hold.Id, owner.Id, sessionCode, SeatHoldLogAction.Extended);

        var client = ClientWith(factory, factory.CreateTokenFor(owner));
        var response = await client.PostAsync(ExtendUrl(sessionCode), content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("tối đa 1 lần", await MessageAsync(response), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Da_gia_han_roi_thi_khong_sua_han_va_khong_ghi_them_log()
    {
        using var factory = new TestAppFactory();
        var owner = await SeedUserAsync(factory);
        var (trip, seat, _) = await SeedTripWithSeatAsync(factory);
        var sessionCode = "PHIEN-CHAN-LUOT-THU-HAI";
        var hold = await SeedHoldAsync(factory, owner.Id, trip.Id, seat.Id, sessionCode);
        await SeedLogAsync(factory, hold.Id, owner.Id, sessionCode, SeatHoldLogAction.Extended);

        var client = ClientWith(factory, factory.CreateTokenFor(owner));
        await client.PostAsync(ExtendUrl(sessionCode), content: null);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.SeatHolds.SingleAsync(h => h.Id == hold.Id);
        var extendedLogs = await db.SeatHoldLogs
            .CountAsync(l => l.SeatHoldId == hold.Id && l.Action == SeatHoldLogAction.Extended);

        // Trả 409 là chưa đủ — phải chắc chắn không có gì bị ghi đè trước khi trả lỗi.
        Assert.Equal(DefaultExpiresAt, stored.ExpiresAt);
        Assert.Equal(1, extendedLogs);
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
        var body = await ReadJsonAsync(await client.PostAsync(ExtendUrl("PHIEN-KHOA-HINH-DANG"), content: null));

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

    private static string ExtendUrl(string sessionCode) => $"/api/seat-holds/{sessionCode}/extend";

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
