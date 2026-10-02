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
/// Test tích hợp cho API gán tài xế vào chuyến theo lô —
/// <c>PATCH /routes/{routeId}/trips/driver-assignment</c> (US 14 "Phân công điều xe",
/// task story 14 — Nguyễn Duy Kiên).
///
/// Dùng lại <see cref="TestAppFactory"/> của JwtAuthTests: chạy trên app thật, mỗi test một CSDL
/// InMemory riêng.
///
/// ⚠️ Trùng lịch là CẢNH BÁO chứ không phải lỗi: các ca trùng lịch ở đây khẳng định 200 kèm
/// <c>conflicts.driverConflicts</c>, không phải 409. Luồng điều hành được phép cố ý chấp nhận
/// trùng (cùng triết lý với <c>PUT /routes/{routeId}/trips/{id}</c> cố ý không kiểm tra khung giờ).
/// </summary>
public class TripDriverAssignmentApiTests
{
    // ---------------------------------------------------------------------------------------
    // Phân quyền
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Khong_gui_token_thi_tra_401()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var route = await SeedRouteAsync(factory);

        var response = await client.PatchAsJsonAsync(
            AssignmentUrl(route.Id), AssignBody([Guid.NewGuid()], Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(RoleCodes.Driver)]
    [InlineData(RoleCodes.Passenger)]
    public async Task Vai_tro_khac_quan_ly_goi_thi_tra_403(string roleCode)
    {
        using var factory = new TestAppFactory();

        var client = await SignInAsync(factory, RoleIdsFor(roleCode), roleCode);
        var route = await SeedRouteAsync(factory);

        var response = await client.PatchAsJsonAsync(
            AssignmentUrl(route.Id), AssignBody([Guid.NewGuid()], Guid.NewGuid()));

        // Story 14 nói "Là quản lý, tôi muốn phân công chuyến cho tài xế" — tài xế tự gán chuyến
        // cho mình là quyền khác, chưa có trong backlog.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------------
    // Gán thành công
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Gan_mot_tai_xe_cho_nhieu_chuyen_tra_ve_so_luong_va_ten_tai_xe()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory, "29B-123.45");
        var first = await SeedTripAsync(factory, route.Id, bus.Id, Gio8);
        var second = await SeedTripAsync(factory, route.Id, bus.Id, Gio8.AddHours(3));
        var driver = await SeedDriverAsync(factory, "Nguyễn Văn An");

        var response = await client.PatchAsJsonAsync(
            AssignmentUrl(route.Id), AssignBody([first.Id, second.Id], driver.Id));
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(driver.Id, body.GetProperty("driverId").GetGuid());
        Assert.Equal("Nguyễn Văn An", body.GetProperty("driverName").GetString());
        Assert.Equal(2, body.GetProperty("assignedCount").GetInt32());

        // Thứ tự phần tử theo đúng thứ tự client gửi lên — màn hình đối chiếu được từng dòng với
        // ô đã chọn, không phải tự đi tìm lại theo id.
        var items = body.GetProperty("items");
        Assert.Equal(
            [first.Id, second.Id],
            items.EnumerateArray().Select(row => row.GetProperty("id").GetGuid()).ToArray());

        // Cách nhau 3 giờ nên không chuyến nào vướng chuyến nào.
        Assert.All(items.EnumerateArray(), row => Assert.Equal(
            0, row.GetProperty("conflicts").GetProperty("driverConflicts").GetArrayLength()));

        // Và màn hình phân công đọc LẠI danh sách chuyến cũng phải thấy tài xế: đây chính là lý do
        // TripResponse mang thêm driverId/driverName, không chỉ endpoint gán trả về. Thiếu
        // Include(t => t.Driver) ở RouteTripsService thì driverId có mà driverName null, và màn
        // hình (hiển thị theo driverName) lại hiện "Chưa phân công".
        var listBody = await ReadJsonAsync(await client.GetAsync(TripsUrl(route.Id)));
        var rows = listBody.GetProperty("items");

        Assert.Equal(2, rows.GetArrayLength());
        Assert.All(rows.EnumerateArray(), row =>
        {
            Assert.Equal(driver.Id, row.GetProperty("driverId").GetGuid());
            Assert.Equal("Nguyễn Văn An", row.GetProperty("driverName").GetString());
            Assert.Equal("29B-123.45", row.GetProperty("busLicensePlate").GetString());
        });
    }

    [Fact]
    public async Task Chuyen_chua_phan_cong_tra_driverId_null_va_driverName_null()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory, "29B-123.45");
        await SeedTripAsync(factory, route.Id, bus.Id, Gio8);

        var rows = (await ReadJsonAsync(await client.GetAsync(TripsUrl(route.Id)))).GetProperty("items");

        // Màn hình lọc chuyến thiếu tài xế bằng driverId === null, nên hai trường phải cùng null —
        // không được là chuỗi rỗng hay "null".
        Assert.Equal(JsonValueKind.Null, rows[0].GetProperty("driverId").ValueKind);
        Assert.Equal(JsonValueKind.Null, rows[0].GetProperty("driverName").ValueKind);
    }

    [Fact]
    public async Task Goi_lai_y_het_thi_idempotent_va_khong_doi_updatedAt()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory, "29B-123.45");
        var trip = await SeedTripAsync(factory, route.Id, bus.Id, Gio8);
        var driver = await SeedDriverAsync(factory, "Nguyễn Văn An");
        var body = AssignBody([trip.Id], driver.Id);

        var first = await ReadJsonAsync(await client.PatchAsJsonAsync(AssignmentUrl(route.Id), body));
        var updatedAtLanDau = first.GetProperty("items")[0].GetProperty("updatedAt").GetDateTime();

        var response = await client.PatchAsJsonAsync(AssignmentUrl(route.Id), body);
        var second = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Lần hai không đổi gì: assignedCount về 0 (để màn hình không báo "đã phân công 1 chuyến"
        // khi thực ra không có gì thay đổi) và updatedAt giữ nguyên giá trị của lần gán thật.
        Assert.Equal(0, second.GetProperty("assignedCount").GetInt32());
        Assert.Equal(updatedAtLanDau, second.GetProperty("items")[0].GetProperty("updatedAt").GetDateTime());
    }

    // ---------------------------------------------------------------------------------------
    // Dữ liệu không hợp lệ / không tìm thấy
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Tuyen_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var driver = await SeedDriverAsync(factory, "Nguyễn Văn An");

        var response = await client.PatchAsJsonAsync(
            AssignmentUrl(Guid.NewGuid()), AssignBody([Guid.NewGuid()], driver.Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            "Không tìm thấy tuyến đường",
            (await ReadJsonAsync(response)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Chuyen_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var driver = await SeedDriverAsync(factory, "Nguyễn Văn An");

        var response = await client.PatchAsJsonAsync(
            AssignmentUrl(route.Id), AssignBody([Guid.NewGuid()], driver.Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            "Không tìm thấy chuyến xe",
            (await ReadJsonAsync(response)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Chuyen_cua_tuyen_khac_tra_404_va_ca_lo_bi_chan()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory, code: "01");
        var otherRoute = await SeedRouteAsync(factory, code: "02");
        var bus = await SeedBusAsync(factory, "29B-123.45");
        var cuaTuyen = await SeedTripAsync(factory, route.Id, bus.Id, Gio8);
        var cuaTuyenKhac = await SeedTripAsync(factory, otherRoute.Id, bus.Id, Gio8.AddHours(3));
        var driver = await SeedDriverAsync(factory, "Nguyễn Văn An");

        // Một chuyến của tuyến khác là CẢ LÔ bị chặn — không âm thầm bỏ qua rồi gán phần còn lại.
        var response = await client.PatchAsJsonAsync(
            AssignmentUrl(route.Id), AssignBody([cuaTuyen.Id, cuaTuyenKhac.Id], driver.Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // Và chuyến hợp lệ trong lô vẫn chưa bị gán: validate hết trước, sửa sau.
        var rows = (await ReadJsonAsync(await client.GetAsync(TripsUrl(route.Id)))).GetProperty("items");
        Assert.Equal(JsonValueKind.Null, rows[0].GetProperty("driverId").ValueKind);
    }

    [Fact]
    public async Task Chuyen_da_huy_tra_409_va_ca_lo_khong_bi_gan()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory, "29B-123.45");
        var binhThuong = await SeedTripAsync(factory, route.Id, bus.Id, Gio8);
        var daHuy = await SeedTripAsync(factory, route.Id, bus.Id, Gio8.AddHours(3), status: TripStatus.Cancelled);
        var driver = await SeedDriverAsync(factory, "Nguyễn Văn An");

        var response = await client.PatchAsJsonAsync(
            AssignmentUrl(route.Id), AssignBody([binhThuong.Id, daHuy.Id], driver.Id));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "Chuyến đã hoàn thành hoặc đã huỷ, không thể phân công tài xế",
            (await ReadJsonAsync(response)).GetProperty("message").GetString());

        // Chuyến hợp lệ đứng TRƯỚC chuyến hỏng trong lô vẫn phải nguyên trạng — nếu service vừa
        // kiểm tra vừa sửa thì nó đã kịp gán trước khi phát hiện chuyến đã huỷ.
        var rows = (await ReadJsonAsync(await client.GetAsync(TripsUrl(route.Id)))).GetProperty("items");
        Assert.Equal(JsonValueKind.Null, rows[0].GetProperty("driverId").ValueKind);
    }

    [Fact]
    public async Task Chuyen_da_hoan_thanh_tra_409()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory, "29B-123.45");
        var daXong = await SeedTripAsync(factory, route.Id, bus.Id, Gio8, status: TripStatus.Completed);
        var driver = await SeedDriverAsync(factory, "Nguyễn Văn An");

        var response = await client.PatchAsJsonAsync(
            AssignmentUrl(route.Id), AssignBody([daXong.Id], driver.Id));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Guid_khong_phai_tai_xe_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory, "29B-123.45");
        var trip = await SeedTripAsync(factory, route.Id, bus.Id, Gio8);

        // Hành khách có thật trong CSDL nhưng không mang vai trò Driver — gán vào chuyến là vô
        // nghĩa. Ca GUID không tồn tại cũng rơi vào đúng nhánh này (IDriverService lọc theo vai
        // trò nên không phân biệt được, và cũng không cần phân biệt).
        var hanhKhach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);

        var response = await client.PatchAsJsonAsync(
            AssignmentUrl(route.Id), AssignBody([trip.Id], hanhKhach.Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            "Không tìm thấy tài xế",
            (await ReadJsonAsync(response)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Tai_khoan_tai_xe_bi_khoa_tra_409()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory, "29B-123.45");
        var trip = await SeedTripAsync(factory, route.Id, bus.Id, Gio8);
        var biKhoa = await SeedDriverAsync(factory, "Nguyễn Văn An", isActive: false);

        var response = await client.PatchAsJsonAsync(
            AssignmentUrl(route.Id), AssignBody([trip.Id], biKhoa.Id));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "Tài khoản tài xế đang bị khoá nên không thể phân công vào chuyến",
            (await ReadJsonAsync(response)).GetProperty("message").GetString());
    }

    // ---------------------------------------------------------------------------------------
    // Trùng lịch — cảnh báo, KHÔNG chặn
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Trung_lich_voi_chuyen_khac_cua_tai_xe_thi_van_200_kem_canh_bao()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory, code: "01");
        var otherRoute = await SeedRouteAsync(factory, code: "02");
        var bus = await SeedBusAsync(factory, "29B-123.45");
        var driver = await SeedDriverAsync(factory, "Nguyễn Văn An");

        // Chuyến tài xế ĐANG chạy ở tuyến khác, khung giờ chồng lên chuyến sắp gán.
        var dangChay = await SeedTripAsync(
            factory, otherRoute.Id, bus.Id, Gio8.AddMinutes(30), Gio8.AddHours(1), driverId: driver.Id);
        var sapGan = await SeedTripAsync(factory, route.Id, bus.Id, Gio8, Gio8.AddHours(1));

        var response = await client.PatchAsJsonAsync(
            AssignmentUrl(route.Id), AssignBody([sapGan.Id], driver.Id));
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, body.GetProperty("assignedCount").GetInt32());

        var conflicts = body.GetProperty("items")[0].GetProperty("conflicts");
        var canhBao = Assert.Single(conflicts.GetProperty("driverConflicts").EnumerateArray());

        Assert.Equal(dangChay.Id, canhBao.GetProperty("id").GetGuid());
        Assert.Equal("02", canhBao.GetProperty("routeCode").GetString());

        // Vế xe LUÔN rỗng: endpoint này chỉ đổi tài xế, soi thêm vế xe sẽ dội lại những lần trùng
        // xe có sẵn (do PUT .../trips/{id} cố ý không kiểm tra) thành tiếng ồn không liên quan.
        Assert.Equal(0, conflicts.GetProperty("busConflicts").GetArrayLength());
    }

    [Fact]
    public async Task Trung_lich_giua_hai_chuyen_trong_cung_lo_thi_van_200_kem_canh_bao()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory, "29B-123.45");

        // Cả hai chuyến đều CHƯA có tài xế trong CSDL, nên TripConflictService không thấy chúng —
        // đây đúng là nửa mà nó không soi được, phải so tại bộ nhớ.
        var chuyenA = await SeedTripAsync(factory, route.Id, bus.Id, Gio8, Gio8.AddHours(1));
        var chuyenB = await SeedTripAsync(factory, route.Id, bus.Id, Gio8.AddMinutes(30), Gio8.AddHours(2));
        var driver = await SeedDriverAsync(factory, "Nguyễn Văn An");

        var body = await ReadJsonAsync(await client.PatchAsJsonAsync(
            AssignmentUrl(route.Id), AssignBody([chuyenA.Id, chuyenB.Id], driver.Id)));

        Assert.Equal(2, body.GetProperty("assignedCount").GetInt32());

        // Mỗi chuyến báo chuyến kia — cảnh báo có đối xứng thì màn hình mới hiện đủ cho cả hai dòng.
        var items = body.GetProperty("items");
        Assert.Equal(
            chuyenB.Id,
            Assert.Single(items[0].GetProperty("conflicts").GetProperty("driverConflicts").EnumerateArray())
                .GetProperty("id").GetGuid());
        Assert.Equal(
            chuyenA.Id,
            Assert.Single(items[1].GetProperty("conflicts").GetProperty("driverConflicts").EnumerateArray())
                .GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Hai_chuyen_noi_duoi_nhau_khong_tinh_la_trung()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory, "29B-123.45");

        // Chuyến A kết thúc đúng lúc chuyến B khởi hành — chạy nối đuôi là chuyện bình thường,
        // báo trùng ở đây là báo sai.
        var chuyenA = await SeedTripAsync(factory, route.Id, bus.Id, Gio8, Gio8.AddHours(1));
        var chuyenB = await SeedTripAsync(factory, route.Id, bus.Id, Gio8.AddHours(1), Gio8.AddHours(2));
        var driver = await SeedDriverAsync(factory, "Nguyễn Văn An");

        var body = await ReadJsonAsync(await client.PatchAsJsonAsync(
            AssignmentUrl(route.Id), AssignBody([chuyenA.Id, chuyenB.Id], driver.Id)));

        Assert.All(body.GetProperty("items").EnumerateArray(), row => Assert.Equal(
            0, row.GetProperty("conflicts").GetProperty("driverConflicts").GetArrayLength()));
    }

    // ---------------------------------------------------------------------------------------
    // Kiểm tra hình dạng lô
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Thieu_driverId_tra_400_kem_loi_theo_truong()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory, "29B-123.45");
        var trip = await SeedTripAsync(factory, route.Id, bus.Id, Gio8);

        var response = await client.PatchAsJsonAsync(
            AssignmentUrl(route.Id), new { tripIds = new[] { trip.Id } });
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(body.GetProperty("errors").TryGetProperty("driverId", out _));
    }

    [Fact]
    public async Task Danh_sach_chuyen_rong_tra_400()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var driver = await SeedDriverAsync(factory, "Nguyễn Văn An");

        var response = await client.PatchAsJsonAsync(
            AssignmentUrl(route.Id), AssignBody([], driver.Id));
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(body.GetProperty("errors").TryGetProperty("tripIds", out _));
    }

    [Fact]
    public async Task Trung_id_trong_lo_tra_400()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory, "29B-123.45");
        var trip = await SeedTripAsync(factory, route.Id, bus.Id, Gio8);
        var driver = await SeedDriverAsync(factory, "Nguyễn Văn An");

        // Chặn hẳn thay vì âm thầm bỏ dòng lặp: client gửi lên một lô có chuyến lặp là client hiểu
        // sai dữ liệu của chính nó, và im lặng sửa hộ thì lỗi đó không bao giờ lộ ra.
        var response = await client.PatchAsJsonAsync(
            AssignmentUrl(route.Id), AssignBody([trip.Id, trip.Id], driver.Id));
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Danh sách chuyến có chuyến bị lặp lại", body.GetProperty("message").GetString());
        Assert.True(body.GetProperty("errors").TryGetProperty("tripIds", out _));
    }

    [Fact]
    public async Task Qua_200_chuyen_tra_400()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var driver = await SeedDriverAsync(factory, "Nguyễn Văn An");

        // Trần 200 lấy đúng trần số chuyến tối đa của một tuyến trong một ngày — một lô gán vượt
        // quá con số đó là vô nghĩa. Không cần seed chuyến thật: DTO đã chặn ở tầng bind.
        var quaNhieu = Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()).ToArray();

        var response = await client.PatchAsJsonAsync(
            AssignmentUrl(route.Id), AssignBody(quaNhieu, driver.Id));
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(body.GetProperty("errors").TryGetProperty("tripIds", out _));
    }

    // ---------------------------------------------------------------------------------------
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // ---------------------------------------------------------------------------------------

    private static string TripsUrl(Guid routeId) => $"/api/routes/{routeId}/trips";

    private static string AssignmentUrl(Guid routeId) => $"{TripsUrl(routeId)}/driver-assignment";

    private static object AssignBody(Guid[] tripIds, Guid driverId) => new { tripIds, driverId };

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

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

    private static async Task<UserEntity> SeedUserAsync(
        TestAppFactory factory,
        Guid roleId,
        string roleCode,
        string fullName = "Người Dùng Test",
        bool isActive = true)
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
            FullName = fullName,
            PasswordHash = PasswordService.HashPassword("matkhau123"),
            IsActive = isActive,
            RoleId = roleId,
            // Vai trò chính phải luôn có mặt ở bảng nối — đúng bất biến mà AuthService giữ, và cũng
            // là vế thứ hai trong truy vấn lọc tài xế của DriverService (quy ước A8.4).
            UserRoles = [new UserRole { RoleId = roleId }],
        };

        await factory.SeedAsync(db => db.Users.Add(user));

        return user;
    }

    private static async Task<UserEntity> SeedDriverAsync(
        TestAppFactory factory,
        string fullName,
        bool isActive = true)
        => await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, fullName, isActive);

    private static async Task<RouteEntity> SeedRouteAsync(TestAppFactory factory, string code = "01")
    {
        var route = new RouteEntity
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = $"Tuyến {code}",
            Origin = "Bến Thành",
            Destination = "Chợ Lớn",
        };

        await factory.SeedAsync(db => db.Routes.Add(route));

        return route;
    }

    private static async Task<Bus> SeedBusAsync(TestAppFactory factory, string licensePlate)
    {
        var bus = new Bus
        {
            Id = Guid.NewGuid(),
            LicensePlate = licensePlate,
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
        DateTime departureTime,
        DateTime? arrivalTime = null,
        TripStatus status = TripStatus.Scheduled,
        Guid? driverId = null)
    {
        // Chỉ gán khoá ngoại, KHÔNG gán navigation: Route/Bus/User được seed ở scope khác, gán
        // navigation vào đây sẽ khiến EF tưởng chúng là bản ghi mới và chèn trùng khoá chính.
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            BusId = busId,
            DriverId = driverId,
            DepartureTime = departureTime,
            ArrivalTime = arrivalTime,
            Status = status,
        };

        await factory.SeedAsync(db => db.Trips.Add(trip));

        return trip;
    }

    /// <summary>Giờ khởi hành gốc của các chuyến trong lớp này — UTC, như mọi cột thời gian (A3).</summary>
    private static readonly DateTime Gio8 = new(2026, 10, 2, 8, 0, 0, DateTimeKind.Utc);
}
