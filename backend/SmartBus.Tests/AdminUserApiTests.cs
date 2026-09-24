using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test tích hợp cho API quản trị người dùng — CRUD tài khoản, khóa/mở khóa, gán/thu hồi vai trò
/// (task story 22 — Nguyễn Duy Kiên).
/// Dùng lại <see cref="TestAppFactory"/> của JwtAuthTests: chạy trên app thật, mỗi test một CSDL InMemory riêng.
/// </summary>
public class AdminUserApiTests
{
    private const string UsersUrl = "/api/admin/users";

    private const string ProtectedUrl = "/api/_test/protected";

    private const string LoginUrl = "/api/auth/login";

    // ---------------------------------------------------------------------------------------
    // Phân quyền
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Khong_gui_token_thi_tra_401()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync(UsersUrl);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(RoleCodes.Manager)]
    [InlineData(RoleCodes.Driver)]
    [InlineData(RoleCodes.Passenger)]
    public async Task Khong_phai_Admin_thi_tra_403_kem_body_JSON(string roleCode)
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIdsFor(roleCode), roleCode);

        var response = await client.GetAsync(UsersUrl);

        // Đã đăng nhập nhưng thiếu quyền là 403, không phải 401 — frontend phân biệt hai ca này
        // để biết khi nào chuyển về trang đăng nhập, khi nào hiện "không có quyền".
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("không có quyền", await MessageAsync(response));
    }

    // ---------------------------------------------------------------------------------------
    // Danh sách — tìm kiếm, lọc, phân trang
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Danh_sach_tra_ve_dung_trang_va_tong_so()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsAdminAsync(factory);
        await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);
        await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);

        var response = await client.GetAsync($"{UsersUrl}?page=1&pageSize=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;

        Assert.Equal(2, root.GetProperty("items").GetArrayLength());

        // total là tổng số dòng khớp bộ lọc (3 tài khoản), KHÔNG phải số dòng của trang này.
        // Lẫn hai con số này thì phân trang trên bảng AntD hiện sai số trang.
        Assert.Equal(3, root.GetProperty("total").GetInt32());
        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.Equal(2, root.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task Loc_theo_vai_tro_chi_khop_vai_tro_chinh()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsAdminAsync(factory);

        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager);

        // Tài khoản Hành khách được gán thêm Manager ở bảng nối, nhưng vai trò CHÍNH vẫn là Passenger.
        var passenger = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);
        await factory.SeedAsync(db => db.UserRoles.Add(new UserRole { UserId = passenger.Id, RoleId = RoleIds.Manager }));

        var response = await client.GetAsync($"{UsersUrl}?role={RoleCodes.Manager}");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Bộ lọc phải khớp đúng cột "Vai trò" đang hiển thị trên bảng, nếu không người dùng lọc
        // Manager mà thấy dòng ghi "Hành khách" và tưởng bộ lọc hỏng.
        Assert.Equal(1, body.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(manager.Id.ToString(), FirstItemId(body.RootElement));
    }

    [Fact]
    public async Task Loc_theo_trang_thai_hoat_dong()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsAdminAsync(factory);
        var locked = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, isActive: false);

        var response = await client.GetAsync($"{UsersUrl}?isActive=false");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(1, body.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(locked.Id.ToString(), FirstItemId(body.RootElement));
        Assert.False(body.RootElement.GetProperty("items")[0].GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Tim_kiem_theo_so_dien_thoai_va_email_khong_phan_biet_hoa_thuong()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsAdminAsync(factory);

        var target = await SeedUserAsync(
            factory,
            RoleIds.Passenger,
            RoleCodes.Passenger,
            phoneNumber: "0987654321",
            email: "ngoc.an@gmail.com");

        var byPhone = await client.GetAsync($"{UsersUrl}?search=987654");
        using var phoneBody = JsonDocument.Parse(await byPhone.Content.ReadAsStringAsync());
        Assert.Equal(1, phoneBody.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(target.Id.ToString(), FirstItemId(phoneBody.RootElement));

        // Gõ hoa vào ô tìm kiếm vẫn phải ra — người dùng không phân biệt hoa thường khi gõ.
        var byEmail = await client.GetAsync($"{UsersUrl}?search=NGOC.AN@GMAIL");
        using var emailBody = JsonDocument.Parse(await byEmail.Content.ReadAsStringAsync());
        Assert.Equal(1, emailBody.RootElement.GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    public async Task Phan_trang_sai_tra_400(string query)
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsAdminAsync(factory);

        var response = await client.GetAsync($"{UsersUrl}?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------------
    // Tạo tài khoản
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Tao_tai_khoan_tra_201_va_dang_nhap_duoc_bang_mat_khau_vua_dat()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsAdminAsync(factory);

        var response = await client.PostAsJsonAsync(UsersUrl, new
        {
            fullName = "Nguyễn Văn An",
            phoneNumber = "0912345678",
            email = "an@gmail.com",
            password = "matkhau123",
            roleCode = RoleCodes.Manager,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var created = body.RootElement;

        Assert.Equal("Nguyễn Văn An", created.GetProperty("fullName").GetString());
        Assert.Equal(RoleCodes.Manager, created.GetProperty("role").GetString());
        Assert.True(created.GetProperty("isActive").GetBoolean());
        Assert.Equal(new[] { RoleCodes.Manager }, CodesOf(created));

        // Location phải trỏ tới GET chi tiết của chính tài khoản vừa tạo.
        var id = created.GetProperty("id").GetString();
        Assert.NotNull(response.Headers.Location);
        Assert.Contains(id!, response.Headers.Location!.ToString());

        // Đăng nhập thật bằng mật khẩu vừa đặt là cách kiểm tra mật khẩu đã được băm đúng:
        // nếu lưu mật khẩu thô hoặc băm sai, BCrypt.Verify ở AuthService sẽ trượt.
        var login = await factory.CreateClient().PostAsJsonAsync(LoginUrl, new
        {
            phoneNumber = "0912345678",
            password = "matkhau123",
        });

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Tao_tai_khoan_khong_co_email_van_tao_duoc()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsAdminAsync(factory);

        var response = await client.PostAsJsonAsync(UsersUrl, new
        {
            fullName = "Tài Xế Mới",
            phoneNumber = "0912345679",
            password = "matkhau123",
            roleCode = RoleCodes.Driver,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("email").ValueKind);
    }

    [Fact]
    public async Task Tao_trung_so_dien_thoai_tra_400_kem_loi_theo_truong()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsAdminAsync(factory);
        await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, phoneNumber: "0912345678");

        var response = await client.PostAsJsonAsync(UsersUrl, new
        {
            fullName = "Người Trùng SĐT",
            phoneNumber = "0912345678",
            password = "matkhau123",
            roleCode = RoleCodes.Passenger,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("phoneNumber", await ErrorFieldsAsync(response));
    }

    [Fact]
    public async Task Tao_trung_email_khong_phan_biet_hoa_thuong_tra_400()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsAdminAsync(factory);
        await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, email: "an@gmail.com");

        var response = await client.PostAsJsonAsync(UsersUrl, new
        {
            fullName = "Người Trùng Email",
            phoneNumber = "0912345678",
            email = "AN@Gmail.com",
            password = "matkhau123",
            roleCode = RoleCodes.Passenger,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("email", await ErrorFieldsAsync(response));
    }

    [Fact]
    public async Task Tao_voi_vai_tro_khong_ton_tai_tra_400()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsAdminAsync(factory);

        var response = await client.PostAsJsonAsync(UsersUrl, new
        {
            fullName = "Sai Vai Trò",
            phoneNumber = "0912345678",
            password = "matkhau123",
            roleCode = "SuperAdmin",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("roleCode", await ErrorFieldsAsync(response));
    }

    [Fact]
    public async Task Tao_thieu_du_lieu_tra_400_dung_dinh_dang_loi_cua_du_an()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsAdminAsync(factory);

        // Thiếu hẳn fullName và roleCode, mật khẩu quá yếu, SĐT sai định dạng.
        var response = await client.PostAsJsonAsync(UsersUrl, new
        {
            phoneNumber = "123",
            password = "abc",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;

        // Cấu trúc { message, errors } thống nhất — frontend chỉ cần xử lý lỗi một lần cho cả app.
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("message").GetString()));

        var fields = await ErrorFieldsAsync(response);
        Assert.Contains("fullName", fields);
        Assert.Contains("phoneNumber", fields);
        Assert.Contains("password", fields);
        Assert.Contains("roleCode", fields);
    }

    // ---------------------------------------------------------------------------------------
    // Sửa hồ sơ
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Sua_ho_so_tra_200_va_luu_thay_doi()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsAdminAsync(factory);
        var user = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);

        var response = await client.PutAsJsonAsync($"{UsersUrl}/{user.Id}", new
        {
            fullName = "Tên Đã Sửa",
            phoneNumber = "0900000001",
            email = "moi@gmail.com",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Tên Đã Sửa", body.RootElement.GetProperty("fullName").GetString());
        Assert.Equal("0900000001", body.RootElement.GetProperty("phoneNumber").GetString());
        Assert.Equal("moi@gmail.com", body.RootElement.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Sua_trung_so_dien_thoai_cua_nguoi_khac_tra_400()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsAdminAsync(factory);
        await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, phoneNumber: "0911111111");
        var target = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver);

        var response = await client.PutAsJsonAsync($"{UsersUrl}/{target.Id}", new
        {
            fullName = "Đổi Sang SĐT Người Khác",
            phoneNumber = "0911111111",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("phoneNumber", await ErrorFieldsAsync(response));
    }

    [Fact]
    public async Task Sua_ho_so_giu_nguyen_so_dien_thoai_cu_thi_khong_bao_trung()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsAdminAsync(factory);
        var user = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, phoneNumber: "0911111111");

        // Gửi lại đúng SĐT cũ của chính mình — bước kiểm tra trùng phải loại trừ chính tài khoản này,
        // nếu không thì sửa mỗi cái tên cũng bị chặn.
        var response = await client.PutAsJsonAsync($"{UsersUrl}/{user.Id}", new
        {
            fullName = "Chỉ Đổi Tên",
            phoneNumber = "0911111111",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Sua_tai_khoan_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsAdminAsync(factory);

        var response = await client.PutAsJsonAsync($"{UsersUrl}/{Guid.NewGuid()}", new
        {
            fullName = "Không Có Thật",
            phoneNumber = "0912345678",
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------------
    // Khóa / mở khóa
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Khoa_tai_khoan_thi_token_dang_dung_mat_hieu_luc_ngay()
    {
        using var factory = new TestAppFactory();
        var (admin, _) = await SignInAsAdminAsync(factory);

        var victim = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, password: "matkhau123");
        var victimClient = ClientWith(factory, factory.CreateTokenFor(victim));

        Assert.Equal(HttpStatusCode.OK, (await victimClient.GetAsync(ProtectedUrl)).StatusCode);

        var response = await admin.PatchAsJsonAsync(
            $"{UsersUrl}/{victim.Id}/status",
            new { isActive = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Không cần thu hồi refresh token: JwtMiddleware đọc lại IsActive ở mọi request,
        // nên access token đang cầm mất hiệu lực ngay chứ không phải chờ hết hạn 30 phút.
        Assert.Equal(HttpStatusCode.Unauthorized, (await victimClient.GetAsync(ProtectedUrl)).StatusCode);
    }

    [Fact]
    public async Task Mo_khoa_lai_thi_tai_khoan_dung_lai_duoc()
    {
        using var factory = new TestAppFactory();
        var (admin, _) = await SignInAsAdminAsync(factory);
        var user = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, isActive: false);

        var response = await admin.PatchAsJsonAsync($"{UsersUrl}/{user.Id}/status", new { isActive = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("isActive").GetBoolean());

        var client = ClientWith(factory, factory.CreateTokenFor(user));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(ProtectedUrl)).StatusCode);
    }

    [Fact]
    public async Task Gui_lai_dung_trang_thai_hien_tai_thi_khong_bao_loi()
    {
        using var factory = new TestAppFactory();
        var (admin, _) = await SignInAsAdminAsync(factory);
        var user = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);

        // Bấm hai lần, hoặc hai tab cùng gửi: thao tác này phải idempotent, không được thành 409.
        var first = await admin.PatchAsJsonAsync($"{UsersUrl}/{user.Id}/status", new { isActive = false });
        var second = await admin.PatchAsJsonAsync($"{UsersUrl}/{user.Id}/status", new { isActive = false });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    [Fact]
    public async Task Thieu_truong_isActive_tra_400()
    {
        using var factory = new TestAppFactory();
        var (admin, _) = await SignInAsAdminAsync(factory);
        var user = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);

        var response = await admin.PatchAsJsonAsync($"{UsersUrl}/{user.Id}/status", new { });

        // Body rỗng KHÔNG được hiểu thành isActive = false rồi khóa tài khoản —
        // một request hỏng sẽ biến thành thao tác phá hoại.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Admin_tu_khoa_chinh_minh_tra_409()
    {
        using var factory = new TestAppFactory();
        var (admin, adminUser) = await SignInAsAdminAsync(factory);

        var response = await admin.PatchAsJsonAsync(
            $"{UsersUrl}/{adminUser.Id}/status",
            new { isActive = false });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("tự khóa", await MessageAsync(response));
    }

    [Fact]
    public async Task Admin_tu_xoa_chinh_minh_tra_409()
    {
        using var factory = new TestAppFactory();
        var (admin, adminUser) = await SignInAsAdminAsync(factory);

        var response = await admin.DeleteAsync($"{UsersUrl}/{adminUser.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------------
    // Xoá mềm
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Xoa_la_khoa_tai_khoan_chu_khong_xoa_du_lieu()
    {
        using var factory = new TestAppFactory();
        var (admin, _) = await SignInAsAdminAsync(factory);
        var user = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);

        var response = await admin.DeleteAsync($"{UsersUrl}/{user.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("isActive").GetBoolean());

        // Dòng dữ liệu vẫn còn: GET chi tiết phải trả 200 chứ không phải 404.
        // Quy ước A4 cấm cột IsDeleted nên "xoá" ở đây là khóa, không phải xoá khỏi CSDL.
        var detail = await admin.GetAsync($"{UsersUrl}/{user.Id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
    }

    [Fact]
    public async Task Xoa_tai_khoan_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var (admin, _) = await SignInAsAdminAsync(factory);

        var response = await admin.DeleteAsync($"{UsersUrl}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------------
    // Gán / thu hồi vai trò
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Gan_va_thu_hoi_vai_tro_trong_cung_mot_request()
    {
        using var factory = new TestAppFactory();
        var (admin, _) = await SignInAsAdminAsync(factory);
        var user = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);

        var grant = await admin.PutAsJsonAsync($"{UsersUrl}/{user.Id}/roles", new
        {
            roleCodes = new[] { RoleCodes.Passenger, RoleCodes.Manager, RoleCodes.Driver },
        });

        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);

        using var grantBody = JsonDocument.Parse(await grant.Content.ReadAsStringAsync());
        Assert.Equal(
            new[] { RoleCodes.Driver, RoleCodes.Manager, RoleCodes.Passenger },
            CodesOf(grantBody.RootElement));

        // Vai trò chính vẫn là Passenger vì tài khoản còn giữ vai trò đó.
        Assert.Equal(RoleCodes.Passenger, grantBody.RootElement.GetProperty("role").GetString());

        var revoke = await admin.PutAsJsonAsync($"{UsersUrl}/{user.Id}/roles", new
        {
            roleCodes = new[] { RoleCodes.Manager },
        });

        using var revokeBody = JsonDocument.Parse(await revoke.Content.ReadAsStringAsync());
        Assert.Equal(new[] { RoleCodes.Manager }, CodesOf(revokeBody.RootElement));

        // Vai trò chính cũ (Passenger) đã bị thu hồi nên phải chuyển sang vai trò còn lại.
        Assert.Equal(RoleCodes.Manager, revokeBody.RootElement.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Gan_vai_tro_Admin_thi_token_dang_cam_co_quyen_ngay()
    {
        using var factory = new TestAppFactory();
        var (admin, _) = await SignInAsAdminAsync(factory);
        var user = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);

        var userClient = ClientWith(factory, factory.CreateTokenFor(user));
        Assert.Equal(HttpStatusCode.Forbidden, (await userClient.GetAsync(UsersUrl)).StatusCode);

        var grant = await admin.PutAsJsonAsync(
            $"{UsersUrl}/{user.Id}/roles",
            new { roleCodes = new[] { RoleCodes.Passenger, RoleCodes.Admin } });

        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);

        // Đây là chốt nối API gán vai trò với middleware phân quyền: vai trò ghi vào bảng nối
        // phải được đọc lại ở request kế tiếp, nếu không tính năng gán vai trò vô nghĩa.
        Assert.Equal(HttpStatusCode.OK, (await userClient.GetAsync(UsersUrl)).StatusCode);
    }

    [Fact]
    public async Task Gui_lai_cung_danh_sach_vai_tro_thi_khong_doi_gi()
    {
        using var factory = new TestAppFactory();
        var (admin, _) = await SignInAsAdminAsync(factory);
        var user = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);

        var body = new { roleCodes = new[] { RoleCodes.Passenger, RoleCodes.Driver } };

        var first = await admin.PutAsJsonAsync($"{UsersUrl}/{user.Id}/roles", body);
        var second = await admin.PutAsJsonAsync($"{UsersUrl}/{user.Id}/roles", body);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        using var secondBody = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal(new[] { RoleCodes.Driver, RoleCodes.Passenger }, CodesOf(secondBody.RootElement));
    }

    [Fact]
    public async Task Danh_sach_vai_tro_rong_tra_400()
    {
        using var factory = new TestAppFactory();
        var (admin, _) = await SignInAsAdminAsync(factory);
        var user = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);

        var response = await admin.PutAsJsonAsync($"{UsersUrl}/{user.Id}/roles", new { roleCodes = Array.Empty<string>() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Gan_vai_tro_khong_ton_tai_tra_400()
    {
        using var factory = new TestAppFactory();
        var (admin, _) = await SignInAsAdminAsync(factory);
        var user = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);

        var response = await admin.PutAsJsonAsync(
            $"{UsersUrl}/{user.Id}/roles",
            new { roleCodes = new[] { RoleCodes.Passenger, "SuperAdmin" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("roleCodes", await ErrorFieldsAsync(response));
    }

    [Fact]
    public async Task Admin_tu_thu_hoi_vai_tro_Admin_cua_chinh_minh_tra_409()
    {
        using var factory = new TestAppFactory();
        var (admin, adminUser) = await SignInAsAdminAsync(factory);

        var response = await admin.PutAsJsonAsync(
            $"{UsersUrl}/{adminUser.Id}/roles",
            new { roleCodes = new[] { RoleCodes.Manager } });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("thu hồi", await MessageAsync(response));
    }

    [Fact]
    public async Task Thu_hoi_vai_tro_cua_nguoi_khac_thi_duoc()
    {
        using var factory = new TestAppFactory();
        var (admin, _) = await SignInAsAdminAsync(factory);

        var other = await SeedUserAsync(factory, RoleIds.Admin, RoleCodes.Admin, phoneNumber: "0999999999");

        // Chốt an toàn chỉ áp cho chính mình — Admin vẫn thu hồi được vai trò của Admin khác,
        // nếu không thì không ai xử lý được một Admin khác bị mất kiểm soát.
        var response = await admin.PutAsJsonAsync(
            $"{UsersUrl}/{other.Id}/roles",
            new { roleCodes = new[] { RoleCodes.Passenger } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { RoleCodes.Passenger }, CodesOf(body.RootElement));
    }

    [Fact]
    public async Task Gan_vai_tro_cho_tai_khoan_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var (admin, _) = await SignInAsAdminAsync(factory);

        var response = await admin.PutAsJsonAsync(
            $"{UsersUrl}/{Guid.NewGuid()}/roles",
            new { roleCodes = new[] { RoleCodes.Manager } });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------------
    // Helper
    // ---------------------------------------------------------------------------------------

    /// <summary>Tạo client đã đăng nhập bằng tài khoản có vai trò chính là <paramref name="roleCode"/>.</summary>
    private static async Task<HttpClient> SignInAsync(TestAppFactory factory, Guid roleId, string roleCode)
    {
        var user = await SeedUserAsync(factory, roleId, roleCode);
        return ClientWith(factory, factory.CreateTokenFor(user));
    }

    private static async Task<(HttpClient Client, UserEntity Admin)> SignInAsAdminAsync(TestAppFactory factory)
    {
        var admin = await SeedUserAsync(factory, RoleIds.Admin, RoleCodes.Admin);
        return (ClientWith(factory, factory.CreateTokenFor(admin)), admin);
    }

    private static HttpClient ClientWith(TestAppFactory factory, string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    /// <summary>
    /// Bảo đảm vai trò có trong CSDL test để tài khoản trỏ tới được.
    /// Migration seed sẵn 4 vai trò bằng <c>HasData</c>; với provider InMemory việc seed có thể
    /// chưa chạy nên phải chịu được cả hai trường hợp — vì vậy luôn dùng Guid cố định ở
    /// <see cref="RoleIds"/>, dù seed hay không thì khoá ngoại vẫn trỏ đúng một vai trò.
    /// </summary>
    private static async Task EnsureRoleAsync(TestAppFactory factory, Guid id, string code)
    {
        await factory.SeedAsync(db =>
        {
            if (!db.Roles.Any(r => r.Id == id || r.Code == code))
            {
                db.Roles.Add(new Role { Id = id, Code = code, Name = code });
            }
        });
    }

    private static async Task<UserEntity> SeedUserAsync(
        TestAppFactory factory,
        Guid roleId,
        string roleCode,
        bool isActive = true,
        string? phoneNumber = null,
        string? email = null,
        string password = "matkhau123")
    {
        await EnsureRoleAsync(factory, roleId, roleCode);

        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            PhoneNumber = phoneNumber ?? RandomPhoneNumber(),
            Email = email,
            FullName = "Người Dùng Test",
            PasswordHash = PasswordService.HashPassword(password),
            IsActive = isActive,
            RoleId = roleId,
            // Vai trò chính phải luôn có mặt ở bảng nối — đúng bất biến mà AuthService và
            // AdminUserService đều giữ khi tạo tài khoản.
            UserRoles = [new UserRole { RoleId = roleId }],
        };

        await factory.SeedAsync(db => db.Users.Add(user));

        return user;
    }

    private static string RandomPhoneNumber() => "09" + Guid.NewGuid().ToString("N")[..8];

    private static Guid RoleIdsFor(string roleCode) => roleCode switch
    {
        RoleCodes.Admin => RoleIds.Admin,
        RoleCodes.Manager => RoleIds.Manager,
        RoleCodes.Driver => RoleIds.Driver,
        _ => RoleIds.Passenger,
    };

    private static string FirstItemId(JsonElement root)
        => root.GetProperty("items")[0].GetProperty("id").GetString() ?? string.Empty;

    private static string[] CodesOf(JsonElement root)
        => root.GetProperty("roles").EnumerateArray().Select(item => item.GetString()!).ToArray();

    private static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("message").GetString() ?? string.Empty;
    }

    private static async Task<string[]> ErrorFieldsAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement
            .GetProperty("errors")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
    }
}
