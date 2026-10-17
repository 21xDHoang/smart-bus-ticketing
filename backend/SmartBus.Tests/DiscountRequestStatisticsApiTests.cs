using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test tích hợp cho API thống kê số lượng hồ sơ ưu đãi theo trạng thái — GET
/// /api/discount-requests/statistics. Task *"API thống kê số lượng hồ sơ ưu đãi theo trạng thái"*
/// (Sprint 4 dòng 55, US 17 — Vàng Thị Dăm).
///
/// Dùng lại <see cref="TestAppFactory"/> của JwtAuthTests: chạy trên app thật, mỗi test một CSDL
/// InMemory riêng dựng từ entity.
///
/// ⚠️ Sáu luật nền của hợp đồng mà bộ test này khoá lại:
///   • Chỉ Manager/Admin gọi được; hành khách và tài xế nhận 403, không token nhận 401.
///   • Bốn con số trạng thái đếm ĐỘC LẬP — đẳng thức <c>total == pending + approved + rejected</c>
///     là phép kiểm thật, không phải hằng đúng.
///   • <c>approvedActive + approvedExpired == approved</c>; hết hạn SUY RA từ <c>ExpiresAt</c>,
///     không có trạng thái <c>Expired</c> được lưu.
///   • Hồ sơ <c>Approved</c> có <c>ExpiresAt</c> null vẫn tính là CÒN HIỆU LỰC (thẻ người cao tuổi
///     không có ngày hết hạn).
///   • Hồ sơ Pending/Rejected có <c>ExpiresAt</c> trong quá khứ KHÔNG được tính vào
///     <c>approvedExpired</c>.
///   • Thống kê trên TOÀN HỆ THỐNG, không lọc theo người gọi.
/// </summary>
public class DiscountRequestStatisticsApiTests
{
    private const string Url = "/api/discount-requests/statistics";

    /// <summary>Mốc cố định cho CreatedAt — không dùng UtcNow để dữ liệu seed tất định.</summary>
    private static readonly DateTime MocTao = new(2026, 10, 1, 1, 0, 0, DateTimeKind.Utc);

    // ---------------------------------------------------------------------------------------
    // Phân quyền
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Khong_gui_token_thi_tra_401()
    {
        using var factory = new TestAppFactory();

        var response = await factory.CreateClient().GetAsync(Url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(RoleCodes.Passenger)]
    [InlineData(RoleCodes.Driver)]
    public async Task Hanh_khach_va_tai_xe_goi_thi_tra_403(string roleCode)
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIdsFor(roleCode), roleCode, "Người Dùng Test");

        // Đây là số liệu vận hành của HR/Quản lý — hồ sơ chờ duyệt và ưu đãi còn hiệu lực trên toàn
        // hệ thống, không phải dữ liệu của một hành khách. Body 403 là câu cố định của RbacMiddleware.
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
    // Hình dạng — chưa có dữ liệu, và hình dạng hợp đồng
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Chua_co_ho_so_nao_thi_tra_200_moi_con_dem_bang_0()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");

        var response = await client.GetAsync(Url);
        var body = await ReadJsonAsync(response);

        // Hệ thống chưa có hồ sơ nào KHÔNG phải lỗi — "chưa có dữ liệu" là một câu trả lời. Endpoint
        // không nhận tham số nên không có nhánh 400/404 nào.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            ["approved", "approvedActive", "approvedExpired", "pending", "rejected", "total"],
            PropertyNamesOf(body));

        Assert.Equal(0, body.GetProperty("total").GetInt32());
        Assert.Equal(0, body.GetProperty("pending").GetInt32());
        Assert.Equal(0, body.GetProperty("approved").GetInt32());
        Assert.Equal(0, body.GetProperty("rejected").GetInt32());
        Assert.Equal(0, body.GetProperty("approvedActive").GetInt32());
        Assert.Equal(0, body.GetProperty("approvedExpired").GetInt32());
    }

    // ---------------------------------------------------------------------------------------
    // 🔴 Đếm đúng theo trạng thái + hai đẳng thức hợp đồng
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Dem_dung_theo_tung_trang_thai_va_hai_dang_thuc_luon_dung()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");

        var khach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);
        var now = DateTime.UtcNow;

        // 2 chờ duyệt. Một hồ sơ cố ý có ExpiresAt đã qua (dữ liệu cũ/nhập nhầm) — nó vẫn là
        // Pending, KHÔNG được lọt sang approvedExpired.
        await SeedRequestAsync(factory, khach.Id, DiscountRequestStatus.Pending, expiresAt: now.AddDays(-5));
        await SeedRequestAsync(factory, khach.Id, DiscountRequestStatus.Pending);

        // 3 đã duyệt: 1 còn hạn, 1 hết hạn, 1 không có ngày hết hạn (thẻ người cao tuổi).
        await SeedRequestAsync(
            factory, khach.Id, DiscountRequestStatus.Approved,
            expiresAt: now.AddDays(30), passengerType: PassengerType.Student);
        await SeedRequestAsync(
            factory, khach.Id, DiscountRequestStatus.Approved,
            expiresAt: now.AddDays(-1), passengerType: PassengerType.Student);
        await SeedRequestAsync(
            factory, khach.Id, DiscountRequestStatus.Approved,
            expiresAt: null, passengerType: PassengerType.Senior);

        // 1 bị từ chối, cũng có ExpiresAt đã qua — cùng lý do: không phải "đã duyệt và hết hạn".
        await SeedRequestAsync(factory, khach.Id, DiscountRequestStatus.Rejected, expiresAt: now.AddDays(-10));

        var body = await ReadJsonAsync(await client.GetAsync(Url));

        Assert.Equal(6, body.GetProperty("total").GetInt32());
        Assert.Equal(2, body.GetProperty("pending").GetInt32());
        Assert.Equal(3, body.GetProperty("approved").GetInt32());
        Assert.Equal(1, body.GetProperty("rejected").GetInt32());
        Assert.Equal(2, body.GetProperty("approvedActive").GetInt32());
        Assert.Equal(1, body.GetProperty("approvedExpired").GetInt32());

        // Hai đẳng thức hợp đồng. Chúng là phép kiểm THẬT vì bốn con số trạng thái đến từ bốn truy
        // vấn đếm RIÊNG, không cộng lại từ nhau — cùng lý do đã ghi ở VoucherStatisticsApiTests.
        Assert.Equal(
            body.GetProperty("total").GetInt32(),
            body.GetProperty("pending").GetInt32()
                + body.GetProperty("approved").GetInt32()
                + body.GetProperty("rejected").GetInt32());
        Assert.Equal(
            body.GetProperty("approved").GetInt32(),
            body.GetProperty("approvedActive").GetInt32()
                + body.GetProperty("approvedExpired").GetInt32());
    }

    [Fact]
    public async Task Ho_so_da_duyet_khong_co_ngay_het_han_van_con_hieu_luc()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var khach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);

        // Entity ghi rõ: người cao tuổi không có thẻ hết hạn, nên ExpiresAt null. Đọc null thành
        // "hết hạn" sẽ làm mọi ưu đãi người cao tuổi biến mất khỏi số đang hiệu lực — đúng lỗi mà
        // màn hình HR không tự phát hiện được.
        await SeedRequestAsync(
            factory, khach.Id, DiscountRequestStatus.Approved,
            expiresAt: null, passengerType: PassengerType.Senior);

        var body = await ReadJsonAsync(await client.GetAsync(Url));

        Assert.Equal(1, body.GetProperty("approved").GetInt32());
        Assert.Equal(1, body.GetProperty("approvedActive").GetInt32());
        Assert.Equal(0, body.GetProperty("approvedExpired").GetInt32());
    }

    [Fact]
    public async Task Chi_ho_so_da_duyet_moi_tinh_vao_nhom_het_han()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var khach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);

        var now = DateTime.UtcNow;

        // Cả hai hồ sơ đều có mốc quá khứ, nhưng KHÔNG phải Approved — nếu service bỏ điều kiện
        // Status thì approvedExpired sẽ thành 2 trong khi approved = 0, phá đẳng thức thứ hai.
        await SeedRequestAsync(factory, khach.Id, DiscountRequestStatus.Pending, expiresAt: now.AddDays(-1));
        await SeedRequestAsync(factory, khach.Id, DiscountRequestStatus.Rejected, expiresAt: now.AddDays(-1));

        var body = await ReadJsonAsync(await client.GetAsync(Url));

        Assert.Equal(0, body.GetProperty("approved").GetInt32());
        Assert.Equal(0, body.GetProperty("approvedActive").GetInt32());
        Assert.Equal(0, body.GetProperty("approvedExpired").GetInt32());
        Assert.Equal(1, body.GetProperty("pending").GetInt32());
        Assert.Equal(1, body.GetProperty("rejected").GetInt32());
    }

    [Fact]
    public async Task Thong_ke_tren_toan_he_thong_khong_theo_nguoi_goi()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var khachA = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");
        var khachB = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Trần Thị B");

        await SeedRequestAsync(factory, khachA.Id, DiscountRequestStatus.Pending);
        await SeedRequestAsync(factory, khachB.Id, DiscountRequestStatus.Approved, expiresAt: DateTime.UtcNow.AddDays(30));
        await SeedRequestAsync(factory, khachB.Id, DiscountRequestStatus.Rejected);

        var body = await ReadJsonAsync(await client.GetAsync(Url));

        // Người gọi (quản lý) không nộp hồ sơ nào, nhưng thống kê vẫn đếm của mọi hành khách. Nếu ai
        // đó dán nhầm điều kiện UserId vào service này, test đỏ ngay.
        Assert.Equal(3, body.GetProperty("total").GetInt32());
        Assert.Equal(1, body.GetProperty("pending").GetInt32());
        Assert.Equal(1, body.GetProperty("approved").GetInt32());
        Assert.Equal(1, body.GetProperty("rejected").GetInt32());
    }

    // ---------------------------------------------------------------------------------------
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // ---------------------------------------------------------------------------------------

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string[] PropertyNamesOf(JsonElement element)
        => element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();

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

    private static async Task<DiscountRequest> SeedRequestAsync(
        TestAppFactory factory,
        Guid userId,
        DiscountRequestStatus status,
        DateTime? expiresAt = null,
        PassengerType passengerType = PassengerType.Student)
    {
        var request = new DiscountRequest
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PassengerType = passengerType,
            Status = status,
            ExpiresAt = expiresAt,
            CreatedAt = MocTao,
        };

        await factory.SeedAsync(db => db.DiscountRequests.Add(request));

        return request;
    }
}
