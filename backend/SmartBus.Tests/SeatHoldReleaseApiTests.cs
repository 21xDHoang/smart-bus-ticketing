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
/// Test tích hợp cho API nhả ghế khi khách huỷ thao tác giữ chỗ theo mã phiên —
/// <c>POST /api/seat-holds/{sessionCode}/release</c> (task *"API nhả ghế khi hết hạn hoặc khách
/// huỷ thao tác"* — Phùng Duy Hoàng, story 3).
///
/// Dùng lại <see cref="TestAppFactory"/> của JwtAuthTests: chạy trên app thật, mỗi test một
/// CSDL InMemory riêng. Endpoint yêu cầu đăng nhập nên mọi ca đều sign in trước (trừ ca 401).
///
/// Nhả KHÁC gia hạn ở chủ đích "thao tác kết thúc": phiên đã Expired / Released từ trước trả 200
/// (idempotent) chứ không 409, và endpoint không kiểm ExpiresAt nên nhận cả phiên vừa quá hạn mà
/// job nền chưa quét — hai hành vi đó được ghim riêng ở đây. Ghép với
/// <c>TripSeatMapApiTests.Hold_het_han_hoac_da_nha_khong_chan_ghe</c>: dòng Released trả ghế về
/// "Available" trên sơ đồ, không cần seed lại toàn bộ dữ liệu ở lớp này.
///
/// ⚠️ Giới hạn của provider InMemory ảnh hưởng tới cách đọc kết quả ở đây:
///   • KHÔNG dựng khoá ngoại, nên seed được dòng SeatHold trỏ tới chuyến/ghế không tồn tại — không
///     ảnh hưởng tới endpoint này vì nó chỉ đọc/ghi SeatHolds/SeatHoldLogs/Seats.
///   • KHÔNG dựng unique index (SeatHoldId, Action) của SeatHoldLogs — nhánh hai request nhả đồng
///     thời (bắt DbUpdateException, trả 200) chỉ chạy ở PostgreSQL, không phủ được ở đây; các nhánh
///     còn lại của service thì phủ đủ (docs/26 §1).
/// </summary>
public class SeatHoldReleaseApiTests
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

        var response = await factory.CreateClient().PostAsync(ReleaseUrl("PHIEN-BAT-KY"), content: null);

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

        var response = await client.PostAsync(ReleaseUrl("PHIEN-KHONG-TON-TAI"), content: null);

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

        var response = await client.PostAsync(ReleaseUrl("PHIEN-CUA-NGUOI-KHAC"), content: null);

        // Không xác nhận sự tồn tại của mã phiên cho người không sở hữu — cùng lối mục GET và extend.
        // Và cũng không được NHẢ HỘ: kiểm luôn dòng còn Holding sau lời gọi.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.SeatHolds.SingleAsync(h => h.SessionCode == "PHIEN-CUA-NGUOI-KHAC");
        Assert.Equal(SeatHoldStatus.Holding, stored.Status);
    }

    // ---------------------------------------------------------------------------------------
    // Nhả thành công — cả nhóm dòng cùng phiên sang Released, hạn cũ giữ nguyên
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Nha_thanh_cong_tra_dung_hinh_dang_va_giu_nguyen_han()
    {
        using var factory = new TestAppFactory();
        var owner = await SeedUserAsync(factory);
        var (trip, seatA, seatB) = await SeedTripWithSeatAsync(factory);
        var sessionCode = "PHIEN-NHA-THANH-CONG";

        // Hạn còn hiệu lực (tương lai) — ca này ghim vế "khách chủ động huỷ giữa lúc còn hạn";
        // vế "vừa quá hạn" có ca riêng bên dưới. Tròn về giây để mốc qua JSON khớp lại nguyên vẹn.
        var conHan = TruncateToSeconds(DateTime.UtcNow.AddMinutes(5));
        await SeedHoldAsync(factory, owner.Id, trip.Id, seatA.Id, sessionCode, expiresAt: conHan);
        await SeedHoldAsync(factory, owner.Id, trip.Id, seatB.Id, sessionCode, expiresAt: conHan);

        var client = ClientWith(factory, factory.CreateTokenFor(owner));
        var body = await ReadJsonAsync(await client.PostAsync(ReleaseUrl(sessionCode), content: null));

        Assert.Equal(sessionCode, body.GetProperty("sessionCode").GetString());
        Assert.Equal(trip.Id, body.GetProperty("tripId").GetGuid());
        Assert.Equal(["A1", "A2"], SeatNumbersOf(body));
        Assert.Equal("Released", body.GetProperty("status").GetString());

        // Nhả KHÔNG đổi ExpiresAt (docs/26 §1) — hạn cũ trả về nguyên vẹn, không gia hạn, không rút ngắn.
        Assert.Equal(conHan, body.GetProperty("expiresAt").GetDateTime());

        // Phiên đã kết thúc thì không còn gì để gia hạn — nút "Gia hạn" trên màn hình phải tắt.
        Assert.False(body.GetProperty("canExtend").GetBoolean());
    }

    [Fact]
    public async Task Nha_cap_nhat_ca_nhom_dong_cung_phien()
    {
        using var factory = new TestAppFactory();
        var owner = await SeedUserAsync(factory);
        var (trip, seatA, seatB) = await SeedTripWithSeatAsync(factory);
        var sessionCode = "PHIEN-NHA-CA-NHOM";
        await SeedHoldAsync(factory, owner.Id, trip.Id, seatA.Id, sessionCode);
        await SeedHoldAsync(factory, owner.Id, trip.Id, seatB.Id, sessionCode);

        var client = ClientWith(factory, factory.CreateTokenFor(owner));
        var response = await client.PostAsync(ReleaseUrl(sessionCode), content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.SeatHolds.Where(h => h.SessionCode == sessionCode).ToListAsync();

        // docs/26 §1: nhả đổi theo nhóm — giữ nửa này nhả nửa kia là ghế mồ côi trong phiên.
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(SeatHoldStatus.Released, r.Status));

        // A4: lượt giữ có sửa dữ liệu nên có UpdatedAt.
        Assert.All(rows, r => Assert.NotNull(r.UpdatedAt));
    }

    [Fact]
    public async Task Nha_ghi_log_Released_cho_moi_luot_giu()
    {
        using var factory = new TestAppFactory();
        var owner = await SeedUserAsync(factory);
        var (trip, seatA, seatB) = await SeedTripWithSeatAsync(factory);
        var sessionCode = "PHIEN-NHA-GHI-LOG";
        await SeedHoldAsync(factory, owner.Id, trip.Id, seatA.Id, sessionCode);
        await SeedHoldAsync(factory, owner.Id, trip.Id, seatB.Id, sessionCode);

        var client = ClientWith(factory, factory.CreateTokenFor(owner));
        await client.PostAsync(ReleaseUrl(sessionCode), content: null);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logs = await db.SeatHoldLogs.Where(l => l.SessionCode == sessionCode).ToListAsync();

        // docs/26 §6: một dòng Released cho MỖI lượt giữ vừa nhả — bảng nhật ký chỉ có giá trị
        // nếu mọi mốc vòng đời đều được ghi.
        Assert.Equal(2, logs.Count);
        Assert.All(logs, l =>
        {
            Assert.Equal(SeatHoldLogAction.Released, l.Action);
            Assert.Equal(owner.Id, l.UserId);
            Assert.Equal(sessionCode, l.SessionCode);
            Assert.NotEqual(default, l.CreatedAt);
        });
    }

    // ---------------------------------------------------------------------------------------
    // Vế "hết hạn" của tên task: phiên vừa quá hạn mà job nền chưa quét vẫn nhả được ngay
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Phien_vua_qua_han_ma_job_chua_quet_van_nha_duoc()
    {
        using var factory = new TestAppFactory();
        var owner = await SeedUserAsync(factory);
        var (trip, seat, _) = await SeedTripWithSeatAsync(factory);
        var sessionCode = "PHIEN-VUA-QUA-HAN";

        // Hạn đã qua nhưng status vẫn Holding — đúng khe hở dưới một phút giữa hai lượt quét của
        // SeatHoldExpiryBackgroundService (docs/26 §4). Endpoint CỐ Ý không kiểm ExpiresAt: từ chối
        // ở đây là giữ ghế thêm tới lượt quét kế tiếp, mất đúng thứ thao tác nhả sinh ra để trả lại.
        var vuaQua = TruncateToSeconds(DateTime.UtcNow.AddMinutes(-1));
        await SeedHoldAsync(factory, owner.Id, trip.Id, seat.Id, sessionCode, expiresAt: vuaQua);

        var client = ClientWith(factory, factory.CreateTokenFor(owner));
        var response = await client.PostAsync(ReleaseUrl(sessionCode), content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);
        Assert.Equal("Released", body.GetProperty("status").GetString());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.SeatHolds.SingleAsync(h => h.SessionCode == sessionCode);

        // Người ghi log là KHÁCH (Released), không phải job (Expired) — đúng ý nghĩa "khách chủ động huỷ".
        Assert.Equal(SeatHoldStatus.Released, stored.Status);
        Assert.Equal(1, await db.SeatHoldLogs.CountAsync(
            l => l.SessionCode == sessionCode && l.Action == SeatHoldLogAction.Released));

        // Hạn cũ (đã qua) giữ nguyên trong response lẫn CSDL — nhả không sửa ExpiresAt.
        Assert.Equal(vuaQua, stored.ExpiresAt);
        Assert.Equal(vuaQua, body.GetProperty("expiresAt").GetDateTime());
    }

    // ---------------------------------------------------------------------------------------
    // Phiên đã kết thúc từ trước — 200 idempotent, không ghi gì thêm
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Phien_da_Released_roi_thi_tra_200_va_khong_ghi_them()
    {
        using var factory = new TestAppFactory();
        var owner = await SeedUserAsync(factory);
        var (trip, seat, _) = await SeedTripWithSeatAsync(factory);
        var sessionCode = "PHIEN-DA-NHA-ROI";
        var hold = await SeedHoldAsync(factory, owner.Id, trip.Id, seat.Id, sessionCode, SeatHoldStatus.Released);
        await SeedLogAsync(factory, hold.Id, owner.Id, sessionCode, SeatHoldLogAction.Released);

        var client = ClientWith(factory, factory.CreateTokenFor(owner));
        var response = await client.PostAsync(ReleaseUrl(sessionCode), content: null);

        // Gọi lại lần hai (khách bấm "Huỷ" hai lần, màn hình tự gọi khi đồng hồ về 0, mạng chập
        // chờn phải retry): đích đến — ghế tự do — đã đạt từ trước nên câu trả lời đúng là "xong rồi".
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);
        Assert.Equal("Released", body.GetProperty("status").GetString());
        Assert.False(body.GetProperty("canExtend").GetBoolean());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // 200 nhưng KHÔNG ghi thêm dòng Released thứ hai — log cũ vẫn là 1.
        Assert.Equal(1, await db.SeatHoldLogs.CountAsync(
            l => l.SessionCode == sessionCode && l.Action == SeatHoldLogAction.Released));
    }

    [Fact]
    public async Task Phien_da_Expired_roi_thi_tra_200_va_giu_nguyen_trang_thai()
    {
        using var factory = new TestAppFactory();
        var owner = await SeedUserAsync(factory);
        var (trip, seat, _) = await SeedTripWithSeatAsync(factory);
        var sessionCode = "PHIEN-DA-HET-HAN-ROI";
        var hold = await SeedHoldAsync(factory, owner.Id, trip.Id, seat.Id, sessionCode, SeatHoldStatus.Expired);
        await SeedLogAsync(factory, hold.Id, owner.Id, sessionCode, SeatHoldLogAction.Expired);

        var client = ClientWith(factory, factory.CreateTokenFor(owner));
        var response = await client.PostAsync(ReleaseUrl(sessionCode), content: null);

        // Job nền đã quét trước đó vài giây (đồng hồ đếm ngược về 0, modal tự gọi release): phiên
        // mang trạng thái THẬT của nó là Expired — không lật thành Released để chiều người gọi,
        // lịch sử nói job đã kết thúc phiên này.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);
        Assert.Equal("Expired", body.GetProperty("status").GetString());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.SeatHolds.SingleAsync(h => h.Id == hold.Id);
        Assert.Equal(SeatHoldStatus.Expired, stored.Status);
        Assert.Null(stored.UpdatedAt);

        // Không ghi thêm: log duy nhất vẫn là dòng Expired của job, không sinh dòng Released nào.
        Assert.Equal(1, await db.SeatHoldLogs.CountAsync(l => l.SessionCode == sessionCode));
        Assert.Equal(0, await db.SeatHoldLogs.CountAsync(
            l => l.SessionCode == sessionCode && l.Action == SeatHoldLogAction.Released));
    }

    // ---------------------------------------------------------------------------------------
    // Phiên đã chốt thành vé — 409, không nhả phần đã bán
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Phien_da_Confirmed_thi_tra_409()
    {
        using var factory = new TestAppFactory();
        var owner = await SeedUserAsync(factory);
        var (trip, seat, _) = await SeedTripWithSeatAsync(factory);
        var sessionCode = "PHIEN-DA-CHOT-VE";
        await SeedHoldAsync(factory, owner.Id, trip.Id, seat.Id, sessionCode, SeatHoldStatus.Confirmed);

        var client = ClientWith(factory, factory.CreateTokenFor(owner));
        var response = await client.PostAsync(ReleaseUrl(sessionCode), content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("chốt thành vé", await MessageAsync(response), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Phien_da_Confirmed_thi_khong_ghi_gi()
    {
        using var factory = new TestAppFactory();
        var owner = await SeedUserAsync(factory);
        var (trip, seat, _) = await SeedTripWithSeatAsync(factory);
        var sessionCode = "PHIEN-DA-CHOT-VE-KHONG-GHI";
        var hold = await SeedHoldAsync(factory, owner.Id, trip.Id, seat.Id, sessionCode, SeatHoldStatus.Confirmed);

        var client = ClientWith(factory, factory.CreateTokenFor(owner));
        await client.PostAsync(ReleaseUrl(sessionCode), content: null);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.SeatHolds.SingleAsync(h => h.Id == hold.Id);

        // Trả 409 là chưa đủ — phải chắc chắn không có gì bị ghi trước khi trả lỗi: dòng Confirmed
        // là ghế đã bán (có vé, có tiền), lật nó sang Released là viết sai lịch sử giao dịch.
        Assert.Equal(SeatHoldStatus.Confirmed, stored.Status);
        Assert.Null(stored.UpdatedAt);
        Assert.Equal(0, await db.SeatHoldLogs.CountAsync(l => l.SessionCode == sessionCode));
    }

    [Fact]
    public async Task Phien_lo_giu_lan_Confirmed_thi_tra_409_va_khong_dung_dong_Holding()
    {
        using var factory = new TestAppFactory();
        var owner = await SeedUserAsync(factory);
        var (trip, seatA, seatB) = await SeedTripWithSeatAsync(factory);
        var sessionCode = "PHIEN-LO-GIU";

        // Dữ liệu lệch: một phiên vừa có dòng đã chốt thành vé vừa còn dòng Holding. Kiểm Confirmed
        // TRƯỚC cả dòng Holding nên ca này bị chặn NGUYÊN PHIÊN, không nhả nửa vời rồi mới trả lỗi.
        var dongHolding = await SeedHoldAsync(factory, owner.Id, trip.Id, seatA.Id, sessionCode);
        await SeedHoldAsync(factory, owner.Id, trip.Id, seatB.Id, sessionCode, SeatHoldStatus.Confirmed);

        var client = ClientWith(factory, factory.CreateTokenFor(owner));
        var response = await client.PostAsync(ReleaseUrl(sessionCode), content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.SeatHolds.SingleAsync(h => h.Id == dongHolding.Id);

        Assert.Equal(SeatHoldStatus.Holding, stored.Status);
        Assert.Null(stored.UpdatedAt);
        Assert.Equal(0, await db.SeatHoldLogs.CountAsync(l => l.SessionCode == sessionCode));
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
        var body = await ReadJsonAsync(await client.PostAsync(ReleaseUrl("PHIEN-KHOA-HINH-DANG"), content: null));

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

    private static string ReleaseUrl(string sessionCode) => $"/api/seat-holds/{sessionCode}/release";

    /// <summary>Bỏ phần dưới giây của mốc thời gian — mốc đi qua JSON so lại khớp nguyên vẹn.</summary>
    private static DateTime TruncateToSeconds(DateTime value)
        => new(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond, value.Kind);

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
        SeatHoldStatus status = SeatHoldStatus.Holding,
        DateTime? expiresAt = null)
    {
        var hold = new SeatHold
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            SeatId = seatId,
            UserId = userId,
            SessionCode = sessionCode,
            Status = status,
            ExpiresAt = expiresAt ?? DefaultExpiresAt,
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
