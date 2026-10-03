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
/// Test tích hợp cho API thống kê phản ánh (US 24, Sprint 2) — /api/admin/feedbacks/statistics
/// (task "API thống kê phản ánh theo loại và theo tuyến" — Nguyễn Duy Kiên).
///
/// Dùng lại <see cref="TestAppFactory"/> của JwtAuthTests: chạy trên app thật, mỗi test một CSDL
/// InMemory riêng dựng từ entity.
///
/// ⚠️ Sáu luật nền của hợp đồng mà bộ test này khoá lại:
///   • Thống kê trên TOÀN BỘ phản ánh, không lọc theo người gọi — khác hẳn /feedbacks/me.
///   • byType LUÔN đủ ba dòng theo thứ tự Complaint → Compliment → Suggestion, kể cả loại 0 phản ánh.
///   • byRoute chỉ có tuyến ĐÃ có phản ánh, sắp số lượng giảm dần, trùng số thì theo mã tuyến.
///   • Phản ánh không gắn chuyến nằm ở withoutTrip, KHÔNG thành dòng routeId null trong byRoute.
///   • Hai đẳng thức: sum(byType) == total và sum(byRoute) + withoutTrip == total.
///   • Quyền ManagerOrAbove, và đoạn literal "statistics" không che route {id:guid} của controller kia.
/// </summary>
public class FeedbackStatisticsApiTests
{
    private const string Url = "/api/admin/feedbacks/statistics";

    /// <summary>Mốc thời gian cố định — không dùng UtcNow để dữ liệu seed tất định.</summary>
    private static readonly DateTime Moc1 = new(2026, 10, 1, 1, 0, 0, DateTimeKind.Utc);

    // ---------------------------------------------------------------------------------------
    // Phân quyền
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Khong_gui_token_thi_tra_401()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(RoleCodes.Passenger)]
    [InlineData(RoleCodes.Driver)]
    public async Task Hanh_khach_va_tai_xe_goi_thi_tra_403(string roleCode)
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIdsFor(roleCode), roleCode, "Người Dùng Test");

        // Thống kê là số liệu vận hành trên TOÀN BỘ phản ánh — cùng quyền với /admin/feedbacks, khác
        // hẳn /feedbacks/me. Body 403 là câu cố định của RbacMiddleware.
        var response = await client.GetAsync(Url);
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Bạn không có quyền truy cập tính năng này.", body.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData(RoleCodes.Manager)]
    [InlineData(RoleCodes.Admin)]
    public async Task Quan_ly_va_admin_goi_duoc(string roleCode)
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIdsFor(roleCode), roleCode, "Quản Lý Test");

        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------------
    // Hình dạng — chưa có dữ liệu
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Chua_co_phan_anh_nao_thi_tra_200_moi_con_dem_bang_0_va_van_du_ba_loai()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");

        var response = await client.GetAsync(Url);
        var body = await ReadJsonAsync(response);

        // Hệ thống chưa có phản ánh nào KHÔNG phải lỗi: 200 với mọi con đếm bằng 0. byType vẫn đủ ba
        // dòng — biểu đồ theo loại có trục cố định ba giá trị, tháng không có khiếu nại nào thì màn
        // hình phải vẽ cột 0 chứ không phải thiếu mất một cột.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            ["byRoute", "byType", "total", "withoutTrip"],
            PropertyNamesOf(body));

        Assert.Equal(0, body.GetProperty("total").GetInt32());
        Assert.Equal(0, body.GetProperty("withoutTrip").GetInt32());
        Assert.Equal(0, body.GetProperty("byRoute").GetArrayLength());

        var byType = body.GetProperty("byType").EnumerateArray().ToArray();
        Assert.Equal(["Complaint", "Compliment", "Suggestion"], TypesOf(body));
        Assert.All(byType, row => Assert.Equal(0, row.GetProperty("count").GetInt32()));
    }

    [Fact]
    public async Task Hinh_dang_mot_dong_khoa_lai_dung_hop_dong()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var hanhKhach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);
        var tuyen = await SeedRouteAsync(factory);
        var xe = await SeedBusAsync(factory);
        var chuyen = await SeedTripAsync(factory, tuyen.Id, xe.Id, Moc1);

        await SeedFeedbackAsync(factory, hanhKhach.Id, tripId: chuyen.Id);

        var body = await ReadJsonAsync(await client.GetAsync(Url));

        var dongLoai = body.GetProperty("byType").EnumerateArray().First();
        Assert.Equal(["count", "type"], PropertyNamesOf(dongLoai));

        var dongTuyen = body.GetProperty("byRoute").EnumerateArray().First();
        Assert.Equal(["count", "routeCode", "routeId", "routeName"], PropertyNamesOf(dongTuyen));
    }

    // ---------------------------------------------------------------------------------------
    // Đếm theo loại
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Dem_theo_loai_luon_du_ba_dong_theo_thu_tu_hop_dong()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var hanhKhach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);

        await SeedFeedbackAsync(factory, hanhKhach.Id, type: FeedbackType.Complaint);
        await SeedFeedbackAsync(factory, hanhKhach.Id, type: FeedbackType.Complaint);
        await SeedFeedbackAsync(factory, hanhKhach.Id, type: FeedbackType.Suggestion);
        // Cố ý KHÔNG seed Compliment nào — loại vắng mặt phải hiện thành dòng count 0.

        var body = await ReadJsonAsync(await client.GetAsync(Url));

        Assert.Equal(["Complaint", "Compliment", "Suggestion"], TypesOf(body));
        Assert.Equal([2, 0, 1], CountsOf(body.GetProperty("byType")));
        Assert.Equal(3, body.GetProperty("total").GetInt32());
    }

    // ---------------------------------------------------------------------------------------
    // Đếm theo tuyến
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Dem_theo_tuyen_chi_gom_tuyen_da_co_phan_anh_va_sap_giam_dan()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var hanhKhach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);
        var xe = await SeedBusAsync(factory);

        var tuyen01 = await SeedRouteAsync(factory, "01", "Bến Thành — Chợ Lớn");
        var tuyenB10 = await SeedRouteAsync(factory, "B10", "Cầu Giấy — Bờ Hồ");
        // Tuyến thứ ba KHÔNG có phản ánh nào — không được xuất hiện trong byRoute.
        await SeedRouteAsync(factory, "99", "Tuyến sạch");

        var chuyen01 = await SeedTripAsync(factory, tuyen01.Id, xe.Id, Moc1);
        var chuyenB10 = await SeedTripAsync(factory, tuyenB10.Id, xe.Id, Moc1.AddHours(1));

        await SeedFeedbackAsync(factory, hanhKhach.Id, tripId: chuyen01.Id);
        await SeedFeedbackAsync(factory, hanhKhach.Id, tripId: chuyen01.Id);
        await SeedFeedbackAsync(factory, hanhKhach.Id, tripId: chuyen01.Id);
        await SeedFeedbackAsync(factory, hanhKhach.Id, tripId: chuyenB10.Id);

        var body = await ReadJsonAsync(await client.GetAsync(Url));
        var byRoute = body.GetProperty("byRoute").EnumerateArray().ToArray();

        // Chỉ hai tuyến có phản ánh, tuyến bị phản ánh nhiều nhất lên đầu — đó chính là điều bảng này
        // để trả lời. routeCode/routeName ghép sẵn để màn hình không phải gọi GET /routes/{id}.
        Assert.Equal(2, byRoute.Length);
        Assert.Equal("01", byRoute[0].GetProperty("routeCode").GetString());
        Assert.Equal("Bến Thành — Chợ Lớn", byRoute[0].GetProperty("routeName").GetString());
        Assert.Equal(tuyen01.Id, byRoute[0].GetProperty("routeId").GetGuid());
        Assert.Equal(3, byRoute[0].GetProperty("count").GetInt32());
        Assert.Equal("B10", byRoute[1].GetProperty("routeCode").GetString());
        Assert.Equal(1, byRoute[1].GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Tuyen_trung_so_thi_sap_theo_ma_tuyen_de_thu_tu_tat_dinh()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var hanhKhach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);
        var xe = await SeedBusAsync(factory);

        // Seed cố ý ngược thứ tự mã: nếu chỉ sắp theo count thì hai dòng bằng điểm sẽ ra thứ tự tuỳ
        // trình tự CSDL trả về, hai lần gọi cho hai kết quả khác nhau.
        var tuyenB10 = await SeedRouteAsync(factory, "B10", "Cầu Giấy — Bờ Hồ");
        var tuyen01 = await SeedRouteAsync(factory, "01", "Bến Thành — Chợ Lớn");

        var chuyenB10 = await SeedTripAsync(factory, tuyenB10.Id, xe.Id, Moc1);
        var chuyen01 = await SeedTripAsync(factory, tuyen01.Id, xe.Id, Moc1.AddHours(1));

        await SeedFeedbackAsync(factory, hanhKhach.Id, tripId: chuyenB10.Id);
        await SeedFeedbackAsync(factory, hanhKhach.Id, tripId: chuyen01.Id);

        var body = await ReadJsonAsync(await client.GetAsync(Url));

        // Cùng 1 phản ánh mỗi tuyến → xếp theo mã tăng dần (ordinal: "01" < "B10").
        Assert.Equal(["01", "B10"], RouteCodesOf(body));
    }

    [Fact]
    public async Task Phan_anh_khong_gan_chuyen_vao_withoutTrip_chu_khong_vao_byRoute()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var hanhKhach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);
        var xe = await SeedBusAsync(factory);
        var tuyen = await SeedRouteAsync(factory);
        var chuyen = await SeedTripAsync(factory, tuyen.Id, xe.Id, Moc1);

        await SeedFeedbackAsync(factory, hanhKhach.Id, tripId: chuyen.Id);
        // Phản ánh về giá vé / ứng dụng / dịch vụ chung: tripId null theo A9 #20.
        await SeedFeedbackAsync(factory, hanhKhach.Id, tripId: null);
        await SeedFeedbackAsync(factory, hanhKhach.Id, tripId: null);

        var body = await ReadJsonAsync(await client.GetAsync(Url));

        // Không quy được về tuyến nào nên đếm riêng, và KHÔNG thành dòng routeId null trong byRoute:
        // trộn hai loại khác nhau vào một mảng thì màn hình phải tự đoán cách vẽ.
        Assert.Equal(2, body.GetProperty("withoutTrip").GetInt32());
        Assert.Single(body.GetProperty("byRoute").EnumerateArray().ToArray());
        Assert.All(
            body.GetProperty("byRoute").EnumerateArray(),
            row => Assert.NotEqual(JsonValueKind.Null, row.GetProperty("routeId").ValueKind));
    }

    [Fact]
    public async Task Hai_dang_thuc_cong_lai_bang_total()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var hanhKhach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);
        var xe = await SeedBusAsync(factory);
        var tuyenA = await SeedRouteAsync(factory, "01", "Bến Thành — Chợ Lớn");
        var tuyenB = await SeedRouteAsync(factory, "B10", "Cầu Giấy — Bờ Hồ");
        var chuyenA = await SeedTripAsync(factory, tuyenA.Id, xe.Id, Moc1);
        var chuyenB = await SeedTripAsync(factory, tuyenB.Id, xe.Id, Moc1.AddHours(1));

        // Dữ liệu trộn đủ ca: đủ ba loại, hai tuyến, và phản ánh không gắn chuyến.
        await SeedFeedbackAsync(factory, hanhKhach.Id, type: FeedbackType.Complaint, tripId: chuyenA.Id);
        await SeedFeedbackAsync(factory, hanhKhach.Id, type: FeedbackType.Compliment, tripId: chuyenA.Id);
        await SeedFeedbackAsync(factory, hanhKhach.Id, type: FeedbackType.Suggestion, tripId: chuyenB.Id);
        await SeedFeedbackAsync(factory, hanhKhach.Id, type: FeedbackType.Complaint, tripId: null);
        await SeedFeedbackAsync(factory, hanhKhach.Id, type: FeedbackType.Compliment, tripId: null);

        var body = await ReadJsonAsync(await client.GetAsync(Url));
        var total = body.GetProperty("total").GetInt32();

        // Hai đẳng thức hợp đồng. Đây là loại sai sót màn hình không tự phát hiện được: nó chỉ vẽ ra
        // một biểu đồ thiếu một mẩu mà không báo gì.
        Assert.Equal(5, total);
        Assert.Equal(total, CountsOf(body.GetProperty("byType")).Sum());
        Assert.Equal(total, CountsOf(body.GetProperty("byRoute")).Sum() + body.GetProperty("withoutTrip").GetInt32());
    }

    // ---------------------------------------------------------------------------------------
    // Phạm vi — toàn hệ thống, không theo người gọi
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Thong_ke_tren_toan_bo_phan_anh_khong_theo_nguoi_goi()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var hanhKhachA = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");
        var hanhKhachB = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Trần Thị B");

        await SeedFeedbackAsync(factory, hanhKhachA.Id, type: FeedbackType.Complaint);
        await SeedFeedbackAsync(factory, hanhKhachB.Id, type: FeedbackType.Complaint);
        await SeedFeedbackAsync(factory, hanhKhachB.Id, type: FeedbackType.Suggestion);

        var body = await ReadJsonAsync(await client.GetAsync(Url));

        // Người gọi (quản lý) KHÔNG có phản ánh nào, nhưng thống kê vẫn đếm của mọi hành khách — đây
        // là điểm khác căn bản so với /feedbacks/me, nơi phạm vi lọc theo userId. Nếu ai đó dán nhầm
        // điều kiện UserId vào service này, test đỏ ngay.
        Assert.Equal(3, body.GetProperty("total").GetInt32());
        Assert.Equal([2, 0, 1], CountsOf(body.GetProperty("byType")));
    }

    // ---------------------------------------------------------------------------------------
    // Không đụng route của controller khác
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Them_doan_statistics_khong_che_route_chi_tiet_theo_id()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var hanhKhach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);
        var phanAnh = await SeedFeedbackAsync(factory, hanhKhach.Id, content: "Xe chạy trễ 30 phút.");

        // Hai route cùng bề mặt api/admin/feedbacks: literal "statistics" và tham số {id:guid}. Chúng
        // chỉ chung sống được vì GUID sai định dạng không khớp template — test này khoá lại, vì nếu
        // có ngày ai đó nới {id:guid} thành {id} thì "statistics" sẽ bị nuốt và endpoint chi tiết
        // nhận một chuỗi không phải GUID.
        var chiTiet = await client.GetAsync($"/api/admin/feedbacks/{phanAnh.Id}");
        var body = await ReadJsonAsync(chiTiet);

        Assert.Equal(HttpStatusCode.OK, chiTiet.StatusCode);
        Assert.Equal("Xe chạy trễ 30 phút.", body.GetProperty("content").GetString());
    }

    // ---------------------------------------------------------------------------------------
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // ---------------------------------------------------------------------------------------

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string[] PropertyNamesOf(JsonElement element)
        => element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();

    /// <summary>Mã loại của bảng byType, GIỮ NGUYÊN thứ tự trả về (không sắp) — thứ tự là một phần của hợp đồng.</summary>
    private static string[] TypesOf(JsonElement body)
        => body.GetProperty("byType").EnumerateArray()
            .Select(row => row.GetProperty("type").GetString() ?? string.Empty)
            .ToArray();

    private static int[] CountsOf(JsonElement mang)
        => mang.EnumerateArray().Select(row => row.GetProperty("count").GetInt32()).ToArray();

    private static string[] RouteCodesOf(JsonElement body)
        => body.GetProperty("byRoute").EnumerateArray()
            .Select(row => row.GetProperty("routeCode").GetString() ?? string.Empty)
            .ToArray();

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

    private static async Task<(HttpClient Client, UserEntity User)> SignInAsync(
        TestAppFactory factory,
        Guid roleId,
        string roleCode,
        string fullName)
    {
        await EnsureAllRolesAsync(factory);
        var user = await SeedUserAsync(factory, roleId, roleCode, fullName);

        return (ClientWith(factory, factory.CreateTokenFor(user)), user);
    }

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
        string fullName = "Người Dùng Test")
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
            IsActive = true,
            RoleId = roleId,
            // Vai trò chính phải luôn có mặt ở bảng nối — đúng bất biến mà AuthService giữ.
            UserRoles = [new UserRole { RoleId = roleId }],
        };

        await factory.SeedAsync(db => db.Users.Add(user));

        return user;
    }

    private static async Task<Feedback> SeedFeedbackAsync(
        TestAppFactory factory,
        Guid userId,
        FeedbackType type = FeedbackType.Complaint,
        FeedbackStatus status = FeedbackStatus.New,
        string content = "Xe chạy trễ 30 phút so với giờ trên vé.",
        Guid? tripId = null,
        int? rating = null,
        DateTime? createdAt = null)
    {
        var feedback = new Feedback
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TripId = tripId,
            Type = type,
            Content = content,
            Rating = rating,
            Status = status,
            CreatedAt = createdAt ?? Moc1,
        };

        await factory.SeedAsync(db => db.Feedbacks.Add(feedback));

        return feedback;
    }

    private static async Task<RouteEntity> SeedRouteAsync(
        TestAppFactory factory,
        string code = "01",
        string name = "Bến Thành — Chợ Lớn")
    {
        var route = new RouteEntity
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            Origin = "Bến Thành",
            Destination = "Chợ Lớn",
        };

        await factory.SeedAsync(db => db.Routes.Add(route));

        return route;
    }

    private static async Task<Bus> SeedBusAsync(TestAppFactory factory, string licensePlate = "29B-123.45")
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
        DateTime departureTime)
    {
        // Chỉ gán khoá ngoại, KHÔNG gán navigation: Route/Bus được seed ở scope khác, gán navigation
        // vào đây sẽ khiến EF tưởng chúng là bản ghi mới và chèn trùng khoá chính.
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            BusId = busId,
            DepartureTime = departureTime,
            Status = TripStatus.Scheduled,
        };

        await factory.SeedAsync(db => db.Trips.Add(trip));

        return trip;
    }
}
