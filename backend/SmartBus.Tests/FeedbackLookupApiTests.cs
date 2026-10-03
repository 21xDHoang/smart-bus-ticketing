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
/// Test tích hợp cho API phản ánh của hành khách (US 24, Sprint 2) — /api/feedbacks/me…
/// (task "API danh sách phản ánh của hành khách + theo dõi trạng thái xử lý" — Nguyễn Duy Kiên).
///
/// Dùng lại <see cref="TestAppFactory"/> của JwtAuthTests: chạy trên app thật, mỗi test một CSDL
/// InMemory riêng dựng từ entity — KHÔNG cần migration của Dăm mới chạy được.
///
/// ⚠️ Năm luật nền của hợp đồng mà bộ test này khoá lại:
///   • Phạm vi "của tôi" nằm ở TRUY VẤN theo userId, không phải ở vai trò: Manager gọi endpoint này
///     cũng chỉ thấy phản ánh của chính mình.
///   • Phản ánh của người khác trả 404 với ĐÚNG câu của ca "không tồn tại" — không phải 403, không
///     phải câu khác: phân biệt hai ca là xác nhận id đó có thật trên hệ thống.
///   • Danh sách là MẢNG TRẦN, không phân trang, và có replyCount mà KHÔNG có replies; chi tiết thì
///     ngược lại — hai hình dạng khác nhau, đổi một bên là đổi hình dạng API (⛔5).
///   • Danh sách mang thêm routeCode/routeName/departureTime ghép từ Trips → Routes (hành khách
///     không tự tra được chuyến: GET /api/trips/{id} nằm sau policy ManagerOrAbove).
///   • status gõ sai trả mảng rỗng chứ không phải 400 (mã lạ có thể là giá trị hợp lệ tương lai).
/// </summary>
public class FeedbackLookupApiTests
{
    private const string ListUrl = "/api/feedbacks/me";

    /// <summary>Mốc thời gian cố định cho các ca sắp xếp — không dùng UtcNow để thứ tự tất định.</summary>
    private static readonly DateTime Moc1 = new(2026, 10, 1, 1, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime GioKhoiHanh = new(2026, 10, 1, 22, 0, 0, DateTimeKind.Utc);

    private const string NotFoundMessage = "Không tìm thấy phản ánh";

    // ---------------------------------------------------------------------------------------
    // Phân quyền
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Khong_gui_token_thi_tra_401()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync(ListUrl);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(RoleCodes.Passenger)]
    [InlineData(RoleCodes.Driver)]
    [InlineData(RoleCodes.Manager)]
    [InlineData(RoleCodes.Admin)]
    public async Task Moi_vai_tro_da_dang_nhap_deu_goi_duoc(string roleCode)
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIdsFor(roleCode), roleCode, "Người Dùng Test");

        // [Authorize] trần, KHÔNG policy: đây là dữ liệu của chính người gọi nên vai trò không quyết
        // định quyền — chỉ cần đăng nhập. Khác hẳn /admin/feedbacks (403 với Passenger/Driver).
        var response = await client.GetAsync(ListUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Quan_ly_cung_chi_thay_phan_anh_cua_chinh_minh()
    {
        using var factory = new TestAppFactory();
        var (client, quanLy) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var hanhKhach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");

        await SeedFeedbackAsync(factory, hanhKhach.Id, content: "Phản ánh của hành khách");
        await SeedFeedbackAsync(factory, quanLy.Id, content: "Phản ánh của quản lý");

        var body = await ReadJsonAsync(await client.GetAsync(ListUrl));

        // Phạm vi lọc theo userId chứ không theo vai trò: quản lý cũng là một người dùng, gọi endpoint
        // này chỉ thấy phản ánh do chính mình gửi. Nếu ai đó sau này "tối ưu" bằng cách bỏ điều kiện
        // UserId khi thấy vai trò quản lý, test này đỏ ngay.
        Assert.Equal(["Phản ánh của quản lý"], ContentsOfList(body));
    }

    // ---------------------------------------------------------------------------------------
    // Danh sách — hình dạng, phạm vi, sắp xếp, lọc
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Chua_co_phan_anh_nao_thi_tra_200_voi_mang_rong()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");

        var response = await client.GetAsync(ListUrl);
        var body = await ReadJsonAsync(response);

        // "Chưa gửi phản ánh nào" là câu trả lời hợp lệ — không phải 404. Mảng TRẦN, không bọc
        // { items, total, page, pageSize }: hành khách chỉ có vài phản ánh, phân trang là thừa.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.Equal(0, body.GetArrayLength());
    }

    [Fact]
    public async Task Danh_sach_chi_tra_phan_anh_cua_chinh_nguoi_goi()
    {
        using var factory = new TestAppFactory();
        var (clientA, hanhKhachA) = await SignInAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");
        var (clientB, hanhKhachB) = await SignInAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Trần Thị B");

        var cuaA1 = await SeedFeedbackAsync(factory, hanhKhachA.Id, content: "Của A — 1");
        var cuaA2 = await SeedFeedbackAsync(factory, hanhKhachA.Id, content: "Của A — 2");
        var cuaB = await SeedFeedbackAsync(factory, hanhKhachB.Id, content: "Của B");

        var bodyA = await ReadJsonAsync(await clientA.GetAsync(ListUrl));
        var bodyB = await ReadJsonAsync(await clientB.GetAsync(ListUrl));

        Assert.Equal(2, bodyA.GetArrayLength());
        Assert.Equal(1, bodyB.GetArrayLength());
        Assert.Empty(IdsOf(bodyA).Except([cuaA1.Id, cuaA2.Id]));
        Assert.Equal([cuaB.Id], IdsOf(bodyB));
    }

    [Fact]
    public async Task Dong_danh_sach_co_du_truong_chi_co_replyCount_khong_co_replies()
    {
        using var factory = new TestAppFactory();
        var (client, hanhKhach) = await SignInAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");
        var quanLy = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");

        var phanAnh = await SeedFeedbackAsync(
            factory, hanhKhach.Id, content: "Xe chạy trễ 30 phút so với giờ trên vé.", createdAt: Moc1);
        await SeedReplyAsync(factory, phanAnh.Id, quanLy.Id, "Nhà xe xin lỗi vì sự cố.", Moc1.AddHours(1));
        await SeedReplyAsync(factory, phanAnh.Id, quanLy.Id, "Đã nhắc nhở tài xế.", Moc1.AddHours(2));

        var body = await ReadJsonAsync(await client.GetAsync(ListUrl));

        var item = Assert.Single(body.EnumerateArray().ToArray());
        Assert.Equal(phanAnh.Id, item.GetProperty("id").GetGuid());
        Assert.Equal("Complaint", item.GetProperty("type").GetString());
        Assert.Equal("New", item.GetProperty("status").GetString());
        Assert.Equal(2, item.GetProperty("replyCount").GetInt32());

        // Hình dạng khoá lại đúng hợp đồng: có replyCount, KHÔNG có replies, KHÔNG có
        // userId/userFullName (với phản ánh của chính mình thì hai trường đó là hằng số), và CÓ ba
        // trường chuyến dù ca này tripId là null.
        Assert.Equal(
            [
                "attachmentUrl", "content", "createdAt", "departureTime", "id", "rating",
                "replyCount", "routeCode", "routeName", "status", "tripId", "type", "updatedAt",
            ],
            PropertyNamesOf(item));
    }

    [Fact]
    public async Task Danh_sach_sap_moi_nhat_truoc()
    {
        using var factory = new TestAppFactory();
        var (client, hanhKhach) = await SignInAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");

        // Seed cố ý lộn xộn: cũ nhất trước.
        await SeedFeedbackAsync(factory, hanhKhach.Id, content: "Cũ nhất", createdAt: Moc1);
        await SeedFeedbackAsync(factory, hanhKhach.Id, content: "Mới nhất", createdAt: Moc1.AddHours(2));
        await SeedFeedbackAsync(factory, hanhKhach.Id, content: "Ở giữa", createdAt: Moc1.AddHours(1));

        var body = await ReadJsonAsync(await client.GetAsync(ListUrl));

        Assert.Equal(["Mới nhất", "Ở giữa", "Cũ nhất"], ContentsOfList(body));
    }

    [Fact]
    public async Task Loc_theo_trang_thai_va_bo_qua_phan_anh_cua_nguoi_khac()
    {
        using var factory = new TestAppFactory();
        var (client, hanhKhach) = await SignInAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");
        var nguoiKhac = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Trần Thị B");

        await SeedFeedbackAsync(
            factory, hanhKhach.Id, content: "Đang xử lý", status: FeedbackStatus.InProgress);
        await SeedFeedbackAsync(factory, hanhKhach.Id, content: "Mới", status: FeedbackStatus.New);
        // Cùng trạng thái nhưng của người khác — bộ lọc không được nới phạm vi.
        await SeedFeedbackAsync(
            factory, nguoiKhac.Id, content: "Của người khác", status: FeedbackStatus.InProgress);

        var body = await ReadJsonAsync(await client.GetAsync($"{ListUrl}?status=InProgress"));

        Assert.Equal(["Đang xử lý"], ContentsOfList(body));
    }

    [Fact]
    public async Task Loc_theo_ma_la_tra_mang_rong_chu_khong_phai_loi()
    {
        using var factory = new TestAppFactory();
        var (client, hanhKhach) = await SignInAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");
        await SeedFeedbackAsync(factory, hanhKhach.Id);

        // Mã lạ có thể là trạng thái hợp lệ trong tương lai — 400 ở đây làm màn hình cũ vỡ khi
        // backend thêm giá trị mới. Cùng lối GET /routes?status= và GET /admin/feedbacks?status=.
        var response = await client.GetAsync($"{ListUrl}?status=KhongCo");
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.Equal(0, body.GetArrayLength());
    }

    [Fact]
    public async Task Status_qua_dai_thi_tra_400_errors_status()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");

        var response = await client.GetAsync($"{ListUrl}?status={new string('x', 21)}");
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Dữ liệu đầu vào không hợp lệ", body.GetProperty("message").GetString());
        Assert.NotEmpty(ErrorsOf(body, "status"));
    }

    // ---------------------------------------------------------------------------------------
    // Thông tin chuyến — ghép từ Trips → Routes
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Tra_thong_tin_chuyen_tu_tripId_de_hanh_khach_khong_phai_tu_tra()
    {
        using var factory = new TestAppFactory();
        var (client, hanhKhach) = await SignInAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");

        var tuyen = await SeedRouteAsync(factory, "01", "Bến Thành — Chợ Lớn");
        var xe = await SeedBusAsync(factory);
        var chuyen = await SeedTripAsync(factory, tuyen.Id, xe.Id, GioKhoiHanh);

        await SeedFeedbackAsync(factory, hanhKhach.Id, content: "Có gắn chuyến", tripId: chuyen.Id);
        await SeedFeedbackAsync(factory, hanhKhach.Id, content: "Không gắn chuyến", tripId: null);

        var body = await ReadJsonAsync(await client.GetAsync(ListUrl));
        var items = body.EnumerateArray().ToDictionary(i => i.GetProperty("content").GetString()!);

        // Hành khách KHÔNG gọi được GET /api/trips/{id} (policy ManagerOrAbove) nên nếu danh sách chỉ
        // trả tripId thô thì họ không có cách nào biết phản ánh nói về tuyến nào — ba trường này là
        // lý do tồn tại của chúng.
        var coChuyen = items["Có gắn chuyến"];
        Assert.Equal(chuyen.Id, coChuyen.GetProperty("tripId").GetGuid());
        Assert.Equal("01", coChuyen.GetProperty("routeCode").GetString());
        Assert.Equal("Bến Thành — Chợ Lớn", coChuyen.GetProperty("routeName").GetString());
        Assert.Equal(GioKhoiHanh, coChuyen.GetProperty("departureTime").GetDateTime());

        // Phản ánh không gắn chuyến: cả ba trường null, không phải chuỗi rỗng hay lỗi.
        var khongChuyen = items["Không gắn chuyến"];
        Assert.Equal(JsonValueKind.Null, khongChuyen.GetProperty("tripId").ValueKind);
        Assert.Equal(JsonValueKind.Null, khongChuyen.GetProperty("routeCode").ValueKind);
        Assert.Equal(JsonValueKind.Null, khongChuyen.GetProperty("routeName").ValueKind);
        Assert.Equal(JsonValueKind.Null, khongChuyen.GetProperty("departureTime").ValueKind);
    }

    // ---------------------------------------------------------------------------------------
    // Chi tiết
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Chi_tiet_tra_kem_luong_phan_hoi_cu_toi_moi()
    {
        using var factory = new TestAppFactory();
        var (client, hanhKhach) = await SignInAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");
        var quanLy = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");

        var phanAnh = await SeedFeedbackAsync(
            factory, hanhKhach.Id,
            content: "Xe chạy trễ 30 phút.",
            status: FeedbackStatus.InProgress,
            createdAt: Moc1,
            updatedAt: null);
        await SeedReplyAsync(factory, phanAnh.Id, quanLy.Id, "Đã nhắc nhở tài xế.", Moc1.AddHours(2));
        await SeedReplyAsync(factory, phanAnh.Id, quanLy.Id, "Nhà xe xin lỗi vì sự cố.", Moc1.AddHours(1));

        var body = await ReadJsonAsync(await client.GetAsync($"{ListUrl}/{phanAnh.Id}"));

        Assert.Equal(phanAnh.Id, body.GetProperty("id").GetGuid());
        Assert.Equal("InProgress", body.GetProperty("status").GetString());

        // Chi tiết có replies và KHÔNG có replyCount — ngược với dòng danh sách. Luồng sắp cũ → mới
        // dù seed lộn xộn, và mang userFullName của quản lý đã trả lời.
        Assert.Equal(
            [
                "attachmentUrl", "content", "createdAt", "departureTime", "id", "rating",
                "replies", "routeCode", "routeName", "status", "tripId", "type", "updatedAt",
            ],
            PropertyNamesOf(body));

        var replies = body.GetProperty("replies").EnumerateArray().ToArray();
        Assert.Equal(2, replies.Length);
        Assert.Equal("Nhà xe xin lỗi vì sự cố.", replies[0].GetProperty("content").GetString());
        Assert.Equal("Đã nhắc nhở tài xế.", replies[1].GetProperty("content").GetString());
        Assert.Equal("Quản Lý Test", replies[0].GetProperty("userFullName").GetString());
        Assert.Equal(
            ["content", "createdAt", "id", "userFullName", "userId"],
            PropertyNamesOf(replies[0]));
    }

    [Fact]
    public async Task Chi_tiet_khong_ton_tai_thi_tra_404()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");

        var response = await client.GetAsync($"{ListUrl}/{Guid.NewGuid()}");
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(NotFoundMessage, body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Chi_tiet_phan_anh_cua_nguoi_khac_tra_404_chu_khong_phai_403()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");
        var nguoiKhac = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Trần Thị B");

        var cuaNguoiKhac = await SeedFeedbackAsync(
            factory, nguoiKhac.Id, content: "Phản ánh của B", createdAt: Moc1);
        await SeedReplyAsync(factory, cuaNguoiKhac.Id, nguoiKhac.Id, "Nội dung riêng của B", Moc1.AddHours(1));

        var response = await client.GetAsync($"{ListUrl}/{cuaNguoiKhac.Id}");
        var body = await ReadJsonAsync(response);

        // 404 chứ không 403, và ĐÚNG câu của ca "không tồn tại": 403 hay một câu khác là xác nhận với
        // người đang dò rằng id đó có thật trên hệ thống. Nội dung phản hồi của B cũng không được rò.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(NotFoundMessage, body.GetProperty("message").GetString());
        Assert.DoesNotContain("Nội dung riêng của B", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Chi_tiet_id_sai_dinh_dang_khong_khop_route_thi_tra_404()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");

        // Ràng buộc {id:guid} khiến chuỗi sai định dạng không khớp template → 404, không phải 500.
        var response = await client.GetAsync($"{ListUrl}/khong-phai-guid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------------
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // ---------------------------------------------------------------------------------------

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string[] PropertyNamesOf(JsonElement element)
        => element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();

    private static string[] ContentsOfList(JsonElement body)
        => body.EnumerateArray()
            .Select(item => item.GetProperty("content").GetString() ?? string.Empty)
            .ToArray();

    private static Guid[] IdsOf(JsonElement body)
        => body.EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToArray();

    private static string[] ErrorsOf(JsonElement body, string field)
        => body.GetProperty("errors").GetProperty(field)
            .EnumerateArray()
            .Select(error => error.GetString() ?? string.Empty)
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

    /// <summary>Phản ánh seed sẵn ở trạng thái mặc định New.</summary>
    private static async Task<Feedback> SeedFeedbackAsync(
        TestAppFactory factory,
        Guid userId,
        FeedbackType type = FeedbackType.Complaint,
        FeedbackStatus status = FeedbackStatus.New,
        string content = "Xe chạy trễ 30 phút so với giờ trên vé.",
        Guid? tripId = null,
        int? rating = null,
        string? attachmentUrl = null,
        DateTime? createdAt = null,
        DateTime? updatedAt = null)
    {
        var feedback = new Feedback
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TripId = tripId,
            Type = type,
            Content = content,
            AttachmentUrl = attachmentUrl,
            Rating = rating,
            Status = status,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            UpdatedAt = updatedAt,
        };

        await factory.SeedAsync(db => db.Feedbacks.Add(feedback));

        return feedback;
    }

    private static async Task<FeedbackReply> SeedReplyAsync(
        TestAppFactory factory,
        Guid feedbackId,
        Guid userId,
        string content = "Nhà xe xin lỗi vì sự cố.",
        DateTime? createdAt = null)
    {
        var reply = new FeedbackReply
        {
            Id = Guid.NewGuid(),
            FeedbackId = feedbackId,
            UserId = userId,
            Content = content,
            CreatedAt = createdAt ?? DateTime.UtcNow,
        };

        await factory.SeedAsync(db => db.FeedbackReplies.Add(reply));

        return reply;
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
