using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SmartBus.Api.Entities;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test tích hợp cho middleware phân quyền RBAC (task story 22 — Vàng Thị Dăm).
/// Dùng lại <see cref="TestAppFactory"/> của JwtAuthTests: chạy trên app thật, CSDL InMemory riêng
/// cho mỗi test.
/// </summary>
public class RbacTests
{
    private const string AdminOnlyUrl = "/api/_test/rbac/admin-only";

    private const string ManagerOrAboveUrl = "/api/_test/rbac/manager-or-above";

    private const string AdminByRolesUrl = "/api/_test/rbac/admin-by-roles";

    [Fact]
    public async Task Khong_gui_token_thi_tra_401_khong_phai_403()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync(AdminOnlyUrl);

        // Chưa đăng nhập là "chưa biết bạn là ai" (401), không phải "biết rồi nhưng không cho" (403).
        // Lẫn hai mã này thì frontend sẽ hiện "không có quyền" thay vì chuyển về trang đăng nhập.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // Khẳng định cả BODY của 401, không chỉ status code: đây mới là chốt chặn cho việc "thay
        // handler mặc định có làm hỏng thông báo 401 của JwtMiddleware hay không". Nếu chỉ so status
        // code thì một handler tự viết 401 với nội dung khác vẫn lọt qua test.
        Assert.Contains("Chưa đăng nhập", await MessageAsync(response));
    }

    [Fact]
    public async Task Dung_vai_tro_thi_duoc_phep()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleCodes.Admin);

        var response = await client.GetAsync(AdminOnlyUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Sai_vai_tro_thi_tra_403_kem_body_JSON_dung_quy_uoc()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleCodes.Passenger);

        var response = await client.GetAsync(AdminOnlyUrl);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // Mặc định framework trả 403 với body rỗng. Cả điểm của task này là body phải có nội dung
        // để frontend hiển thị lý do cho người dùng.
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var message = body.RootElement.GetProperty("message").GetString();
        Assert.False(string.IsNullOrWhiteSpace(message));
        Assert.Contains("không có quyền", message);
    }

    [Fact]
    public async Task Policy_ManagerOrAbove_cho_ca_Admin_lan_Quan_ly()
    {
        using var factory = new TestAppFactory();

        var admin = await SignInAsync(factory, RoleCodes.Admin);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(ManagerOrAboveUrl)).StatusCode);

        var manager = await SignInAsync(factory, RoleCodes.Manager);
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync(ManagerOrAboveUrl)).StatusCode);
    }

    [Fact]
    public async Task Policy_ManagerOrAbove_chan_Tai_xe_va_Hanh_khach()
    {
        using var factory = new TestAppFactory();

        var driver = await SignInAsync(factory, RoleCodes.Driver);
        Assert.Equal(HttpStatusCode.Forbidden, (await driver.GetAsync(ManagerOrAboveUrl)).StatusCode);

        var passenger = await SignInAsync(factory, RoleCodes.Passenger);
        Assert.Equal(HttpStatusCode.Forbidden, (await passenger.GetAsync(ManagerOrAboveUrl)).StatusCode);
    }

    [Fact]
    public async Task Vai_tro_gan_qua_bang_UserRoles_cung_duoc_phep()
    {
        using var factory = new TestAppFactory();
        var admin = NewRole(RoleCodes.Admin);
        var passenger = NewRole(RoleCodes.Passenger);
        var user = NewUser(passenger);
        await factory.SeedAsync(db =>
        {
            db.Roles.AddRange(admin, passenger);
            db.Users.Add(user);
        });

        // Token phát ra khi tài khoản mới chỉ có vai trò Hành khách.
        var token = factory.CreateTokenFor(user);

        // Gán thêm vai trò Admin qua bảng nối — đúng cách API "gán/thu hồi vai trò" sẽ ghi.
        await factory.SeedAsync(db => db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = admin.Id }));

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(AdminOnlyUrl);

        // Phân quyền phải xét TOÀN BỘ vai trò của tài khoản, không chỉ vai trò chính.
        // Nếu chỉ đọc User.RoleId thì ca này đổ, và tính năng gán vai trò ở Sprint 1 vô nghĩa.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Authorize_Roles_khai_bao_thu_cong_cung_duoc_phan_quyen_giong_nhu_policy()
    {
        using var factory = new TestAppFactory();

        var admin = await SignInAsync(factory, RoleCodes.Admin);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(AdminByRolesUrl)).StatusCode);

        var passenger = await SignInAsync(factory, RoleCodes.Passenger);
        var response = await passenger.GetAsync(AdminByRolesUrl);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("không có quyền", await MessageAsync(response));
    }

    [Fact]
    public async Task Tai_khoan_bi_khoa_thi_van_tra_401_khong_phai_403()
    {
        using var factory = new TestAppFactory();
        var role = NewRole(RoleCodes.Admin);
        var user = NewUser(role);
        await factory.SeedAsync(db =>
        {
            db.Roles.Add(role);
            db.Users.Add(user);
        });

        // Token phát ra lúc tài khoản còn hoạt động, sau đó mới bị khóa.
        var token = factory.CreateTokenFor(user);
        await factory.SeedAsync(db => db.Users.Find(user.Id)!.IsActive = false);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(AdminOnlyUrl);

        // Tài khoản bị khóa là "không còn phiên hợp lệ" — JwtMiddleware chặn ở tầng xác thực (401),
        // không được để lọt xuống tầng phân quyền rồi thành 403.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Tạo client đã đăng nhập bằng tài khoản có <paramref name="roleCode"/> làm vai trò chính.</summary>
    private static async Task<HttpClient> SignInAsync(TestAppFactory factory, string roleCode)
    {
        var role = NewRole(roleCode);
        var user = NewUser(role);
        await factory.SeedAsync(db =>
        {
            db.Roles.Add(role);
            db.Users.Add(user);
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", factory.CreateTokenFor(user));

        return client;
    }

    private static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("message").GetString() ?? string.Empty;
    }

    private static Role NewRole(string code) => new() { Id = Guid.NewGuid(), Code = code, Name = code };

    private static UserEntity NewUser(Role role) => new()
    {
        Id = Guid.NewGuid(),
        PhoneNumber = "09" + Guid.NewGuid().ToString("N")[..8],
        FullName = "Người Dùng Test",
        PasswordHash = "hash-khong-dung-toi-trong-test",
        IsActive = true,
        RoleId = role.Id,
        Role = role,
    };
}
