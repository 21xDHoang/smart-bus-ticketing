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
/// Test tích hợp cho API tra cứu vé tháng đang hoạt động — <c>GET /api/monthly-passes/me</c>
/// (task story 16 — Phùng Duy Hoàng).
///
/// Dùng lại <see cref="TestAppFactory"/> của JwtAuthTests: chạy trên app thật, mỗi test một
/// CSDL InMemory riêng.
///
/// ⚠️ Hai luật nền của vé tháng mà bộ test này khoá lại:
///   • "Đang hoạt động" suy từ cặp mốc ValidFrom/ValidTo (tính CẢ HAI mốc), KHÔNG đọc cột Status —
///     cột đó có độ trễ job quét (AppDbContext.MonthlyPass.cs nói rõ). Vì vậy có đủ cặp ca
///     "Status = Active nhưng hết hạn" và "Status = Expired nhưng còn hạn": cả hai theo NGÀY.
///   • Vé gia hạn trước cho kỳ sau là dòng có ValidFrom tương lai — hợp lệ trong CSDL (không có
///     unique (UserId, RouteId)), nhưng không phải vé "đang hoạt động".
/// </summary>
public class MonthlyPassLookupApiTests
{
    /// <summary>Mốc "bây giờ" dùng chung cho mọi phép cộng trừ ngày — test chạy trong vài giây.</summary>
    private static readonly DateTime BayGio = DateTime.UtcNow;

    // ---------------------------------------------------------------------------------------
    // Phân quyền
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Khong_gui_token_thi_tra_401()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync(MeUrl);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(RoleCodes.Driver)]
    [InlineData(RoleCodes.Manager)]
    [InlineData(RoleCodes.Admin)]
    public async Task Vai_tro_khac_hanh_khach_khong_bi_chan(string roleCode)
    {
        using var factory = new TestAppFactory();
        await EnsureAllRolesAsync(factory);
        var user = await SeedUserAsync(factory, RoleIdsFor(roleCode), roleCode);
        var client = ClientWith(factory, factory.CreateTokenFor(user));

        var response = await client.GetAsync(MeUrl);

        // RBAC của dự án chỉ có AdminOnly/ManagerOrAbove — endpoint này CỐ Ý không gắn policy: ai
        // đăng nhập cũng chỉ thấy vé của CHÍNH MÌNH (lọc theo UserId trong truy vấn), nên không có
        // lý do chặn vai trò nào. Ca này khoá quyết định đó lại — gắn policy vào sau này là đổ test.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, (await ReadJsonAsync(response)).GetArrayLength());
    }

    // ---------------------------------------------------------------------------------------
    // Nội dung — vé đang hoạt động
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Chua_co_ve_nao_thi_tra_200_va_mang_rong()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsPassengerAsync(factory);

        var response = await client.GetAsync(MeUrl);
        var body = await ReadJsonAsync(response);

        // "Chưa có vé nào" là câu trả lời hợp lệ — mảng rỗng, KHÔNG phải 404: người gọi có thật,
        // chỉ là chưa mua vé. 404 ở đây khiến màn hình hiểu nhầm thành lỗi.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.Equal(0, body.GetArrayLength());
    }

    [Fact]
    public async Task Tra_ve_ve_dang_hoat_dong_voi_du_cac_truong_hop_dong()
    {
        using var factory = new TestAppFactory();
        var chuVe = await SeedUserAsync(
            factory, RoleIds.Passenger, RoleCodes.Passenger, phoneNumber: "0911111111");
        var client = ClientWith(factory, factory.CreateTokenFor(chuVe));

        var route = await SeedRouteAsync(factory, "01");
        var passType = await SeedPassTypeAsync(factory, "OneMonth", durationMonths: 1, price: 200_000m);
        var ve = await SeedMonthlyPassAsync(
            factory, chuVe.Id, route.Id, passType.Id,
            code: "MP-01-ABC123",
            validFrom: BayGio.AddDays(-10),
            validTo: BayGio.AddDays(20));

        var body = await ReadJsonAsync(await client.GetAsync(MeUrl));

        var item = Assert.Single(body.EnumerateArray().ToArray());
        Assert.Equal(ve.Id, item.GetProperty("id").GetGuid());
        Assert.Equal("MP-01-ABC123", item.GetProperty("code").GetString());
        Assert.Equal(route.Id, item.GetProperty("routeId").GetGuid());
        Assert.Equal("OneMonth", item.GetProperty("passTypeCode").GetString());
        Assert.Equal(200_000m, item.GetProperty("price").GetDecimal());
        Assert.Equal(ve.ValidFrom, item.GetProperty("validFrom").GetDateTime());
        Assert.Equal(ve.ValidTo, item.GetProperty("validTo").GetDateTime());
        Assert.Equal("Active", item.GetProperty("status").GetString());
        Assert.Equal(ve.CreatedAt, item.GetProperty("createdAt").GetDateTime());

        // Hình dạng khoá lại đúng hợp đồng: thêm/bớt trường là ĐỔI HÌNH DẠNG API (⛔5) — phải sửa
        // api-contract.md trước rồi báo người viết frontend. KHÔNG có userId (người gọi chỉ thấy
        // vé của mình, trả id chủ vé là mời dò người khác) và KHÔNG có passTypeId (màn hình cần
        // mã loại vé, không cần id nội bộ).
        Assert.Equal(
            ["code", "createdAt", "id", "passTypeCode", "price", "routeId", "status", "validFrom", "validTo"],
            PropertyNamesOf(item));
    }

    [Fact]
    public async Task Ve_het_han_nhung_Status_con_Active_thi_khong_tra()
    {
        using var factory = new TestAppFactory();
        var chuVe = await SeedUserAsync(
            factory, RoleIds.Passenger, RoleCodes.Passenger, phoneNumber: "0911111111");
        var client = ClientWith(factory, factory.CreateTokenFor(chuVe));

        var route = await SeedRouteAsync(factory, "01");
        var passType = await SeedPassTypeAsync(factory, "OneMonth", durationMonths: 1, price: 200_000m);

        // Mô phỏng đúng khe hở có thật: ValidTo đã trôi qua nhưng job quét của Kiên chưa chạy →
        // cột Status vẫn là "Active". Lọc theo Status là vé hết hạn lọt vào màn hình — ca này
        // chính là lưới bắt lỗi đó.
        await SeedMonthlyPassAsync(
            factory, chuVe.Id, route.Id, passType.Id,
            validFrom: BayGio.AddDays(-40),
            validTo: BayGio.AddDays(-10),
            status: MonthlyPassStatus.Active);

        var body = await ReadJsonAsync(await client.GetAsync(MeUrl));

        Assert.Equal(0, body.GetArrayLength());
    }

    [Fact]
    public async Task Ve_Status_Expired_nhung_con_hieu_luc_thi_van_tra()
    {
        using var factory = new TestAppFactory();
        var chuVe = await SeedUserAsync(
            factory, RoleIds.Passenger, RoleCodes.Passenger, phoneNumber: "0911111111");
        var client = ClientWith(factory, factory.CreateTokenFor(chuVe));

        var route = await SeedRouteAsync(factory, "01");
        var passType = await SeedPassTypeAsync(factory, "OneMonth", durationMonths: 1, price: 200_000m);

        // Chiều ngược lại: Status lật sớm (job chạy lệch) nhưng theo NGÀY thì vé còn hạn. Ngày là
        // nguồn sự thật — vé vẫn phải trả về, và trường status trả đúng giá trị đang lưu
        // ("Expired"), không tự suy lại thành "Active".
        await SeedMonthlyPassAsync(
            factory, chuVe.Id, route.Id, passType.Id,
            validFrom: BayGio.AddDays(-5),
            validTo: BayGio.AddDays(25),
            status: MonthlyPassStatus.Expired);

        var body = await ReadJsonAsync(await client.GetAsync(MeUrl));

        var item = Assert.Single(body.EnumerateArray().ToArray());
        Assert.Equal("Expired", item.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Ve_chua_toi_ngay_hieu_luc_thi_khong_tra()
    {
        using var factory = new TestAppFactory();
        var chuVe = await SeedUserAsync(
            factory, RoleIds.Passenger, RoleCodes.Passenger, phoneNumber: "0911111111");
        var client = ClientWith(factory, factory.CreateTokenFor(chuVe));

        var route = await SeedRouteAsync(factory, "01");
        var passType = await SeedPassTypeAsync(factory, "OneMonth", durationMonths: 1, price: 200_000m);

        // Vé đã gia hạn trước cho kỳ sau: dòng ValidFrom tương lai là hợp lệ trong CSDL (không có
        // unique (UserId, RouteId)); nó CHỈ không phải vé đang hoạt động. Trả về là màn hình soát
        // vé cho qua cửa trước ngày hiệu lực.
        await SeedMonthlyPassAsync(
            factory, chuVe.Id, route.Id, passType.Id,
            validFrom: BayGio.AddDays(5),
            validTo: BayGio.AddDays(35));

        var body = await ReadJsonAsync(await client.GetAsync(MeUrl));

        Assert.Equal(0, body.GetArrayLength());
    }

    [Fact]
    public async Task Ve_cua_nguoi_khac_thi_khong_lan_vao_ket_qua()
    {
        using var factory = new TestAppFactory();
        var chuVe = await SeedUserAsync(
            factory, RoleIds.Passenger, RoleCodes.Passenger, phoneNumber: "0911111111");
        var nguoiKhac = await SeedUserAsync(
            factory, RoleIds.Passenger, RoleCodes.Passenger, phoneNumber: "0922222222");
        var client = ClientWith(factory, factory.CreateTokenFor(chuVe));

        var route = await SeedRouteAsync(factory, "01");
        var passType = await SeedPassTypeAsync(factory, "OneMonth", durationMonths: 1, price: 200_000m);

        // Vé của người khác seed trước để nếu truy vấn quên điều kiện UserId thì nó nổi lên đầu.
        await SeedMonthlyPassAsync(
            factory, nguoiKhac.Id, route.Id, passType.Id, code: "MP-01-XXXXXX",
            validFrom: BayGio.AddDays(-10), validTo: BayGio.AddDays(20));
        var veCuaMinh = await SeedMonthlyPassAsync(
            factory, chuVe.Id, route.Id, passType.Id, code: "MP-01-YYYYYY",
            validFrom: BayGio.AddDays(-10), validTo: BayGio.AddDays(20));

        var body = await ReadJsonAsync(await client.GetAsync(MeUrl));

        var item = Assert.Single(body.EnumerateArray().ToArray());
        Assert.Equal(veCuaMinh.Id, item.GetProperty("id").GetGuid());
        Assert.Equal("MP-01-YYYYYY", item.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Nhieu_ve_dang_hoat_dong_tra_het_va_sap_theo_ValidTo_tang_dan()
    {
        using var factory = new TestAppFactory();
        var chuVe = await SeedUserAsync(
            factory, RoleIds.Passenger, RoleCodes.Passenger, phoneNumber: "0911111111");
        var client = ClientWith(factory, factory.CreateTokenFor(chuVe));

        var route = await SeedRouteAsync(factory, "01");
        var passType = await SeedPassTypeAsync(factory, "OneMonth", durationMonths: 1, price: 200_000m);

        // Seed cố ý lộn xộn, và cố ý để HAI vé cùng ValidTo (+10 ngày) khác Code: thứ tự trả về
        // phải là ValidTo tăng dần, cùng ValidTo thì theo Code (tie-break cho ổn định giữa các
        // lần gọi — cùng lối TripDetailApiTests xếp tiếp theo stopId).
        await SeedMonthlyPassAsync(
            factory, chuVe.Id, route.Id, passType.Id, code: "MP-01-BBBBB",
            validFrom: BayGio.AddDays(-5), validTo: BayGio.AddDays(30));
        await SeedMonthlyPassAsync(
            factory, chuVe.Id, route.Id, passType.Id, code: "MP-01-CCCCC",
            validFrom: BayGio.AddDays(-5), validTo: BayGio.AddDays(10));
        await SeedMonthlyPassAsync(
            factory, chuVe.Id, route.Id, passType.Id, code: "MP-01-AAAAA",
            validFrom: BayGio.AddDays(-5), validTo: BayGio.AddDays(10));

        var body = await ReadJsonAsync(await client.GetAsync(MeUrl));

        Assert.Equal(["MP-01-AAAAA", "MP-01-CCCCC", "MP-01-BBBBB"], CodesOf(body));
    }

    // ---------------------------------------------------------------------------------------
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // ---------------------------------------------------------------------------------------

    private const string MeUrl = "/api/monthly-passes/me";

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string[] PropertyNamesOf(JsonElement element)
        => element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();

    private static string[] CodesOf(JsonElement body)
        => body.EnumerateArray()
            .Select(item => item.GetProperty("code").GetString() ?? string.Empty)
            .ToArray();

    private static async Task<HttpClient> SignInAsPassengerAsync(TestAppFactory factory)
    {
        await EnsureAllRolesAsync(factory);
        var user = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);

        return ClientWith(factory, factory.CreateTokenFor(user));
    }

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

    /// <summary>Bốn vai trò migration seed sẵn — InMemory KHÔNG chạy <c>HasData</c>.</summary>
    private static readonly (Guid Id, string Code)[] CanonicalRoles =
    [
        (RoleIds.Admin, RoleCodes.Admin),
        (RoleIds.Manager, RoleCodes.Manager),
        (RoleIds.Driver, RoleCodes.Driver),
        (RoleIds.Passenger, RoleCodes.Passenger),
    ];

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

    private static async Task<UserEntity> SeedUserAsync(
        TestAppFactory factory,
        Guid roleId,
        string roleCode,
        string? phoneNumber = null)
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
            PhoneNumber = phoneNumber ?? "09" + Random.Shared.Next(10_000_000, 99_999_999),
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

    private static async Task<PassType> SeedPassTypeAsync(
        TestAppFactory factory,
        string code,
        int durationMonths,
        decimal price)
    {
        var passType = new PassType
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = $"Vé {code}",
            DurationMonths = durationMonths,
            Price = price,
        };

        await factory.SeedAsync(db => db.PassTypes.Add(passType));

        return passType;
    }

    /// <summary>
    /// Vé tháng với đủ cặp mốc để cài đúng ca kiểm: mốc ngày và Status cài ĐỘC LẬP nhau —
    /// hợp đồng nói hiệu lực suy từ ngày, nên test phải dựng được cả hai thế lệch.
    /// </summary>
    private static async Task<MonthlyPass> SeedMonthlyPassAsync(
        TestAppFactory factory,
        Guid userId,
        Guid routeId,
        Guid passTypeId,
        string code = "MP-01-ABC123",
        DateTime? validFrom = null,
        DateTime? validTo = null,
        MonthlyPassStatus status = MonthlyPassStatus.Active)
    {
        var pass = new MonthlyPass
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RouteId = routeId,
            PassTypeId = passTypeId,
            Price = 200_000m,
            Code = code,
            ValidFrom = validFrom ?? BayGio.AddDays(-10),
            ValidTo = validTo ?? BayGio.AddDays(20),
            Status = status,
        };

        await factory.SeedAsync(db => db.MonthlyPasses.Add(pass));

        return pass;
    }
}
