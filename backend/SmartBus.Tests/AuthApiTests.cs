using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test tích hợp cho API xác thực — luồng đăng ký / đăng nhập / phân quyền
/// (task story 22 — Giàng A Vàng).
///
/// Khác <see cref="JwtAuthTests"/> và <see cref="RbacTests"/>: hai file kia gọi thẳng endpoint
/// giả lập để kiểm chứng <em>middleware</em>, còn file này đi qua đúng các endpoint thật
/// <c>/api/auth/*</c> mà frontend sẽ gọi. Nhờ vậy nó bắt được cả lỗi ở tầng Controller
/// (mã trạng thái, định dạng body, cột nào ghi xuống CSDL) — thứ mà test middleware không chạm tới.
///
/// Dùng lại <see cref="TestAppFactory"/> của JwtAuthTests: chạy trên app thật, mỗi test một CSDL InMemory riêng.
/// </summary>
public class AuthApiTests
{
    private const string RegisterUrl = "/api/auth/register";

    private const string LoginUrl = "/api/auth/login";

    private const string RefreshUrl = "/api/auth/refresh-token";

    private const string LogoutUrl = "/api/auth/logout";

    /// <summary>Endpoint chỉ có trong project test — xem <see cref="TestProtectedController"/>.</summary>
    private const string ProtectedUrl = "/api/_test/protected";

    /// <summary>API quản trị, chỉ vai trò Admin đi qua được — dùng để kiểm chứng phân quyền đầu-cuối.</summary>
    private const string AdminUsersUrl = "/api/admin/users";

    /// <summary>Mật khẩu hợp lệ theo ràng buộc của <c>RegisterRequest</c>: từ 8 ký tự, gồm cả chữ và số.</summary>
    private const string ValidPassword = "matkhau123";

    /// <summary>Thông báo dùng chung cho hai ca thất bại — xem <c>AuthService.InvalidCredentials</c>.</summary>
    private const string InvalidCredentials = "Số điện thoại hoặc mật khẩu không đúng";

    // ---------------------------------------------------------------------------------------
    // Đăng ký — POST /api/auth/register
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Dang_ky_hop_le_tra_201_kem_cap_token_dung_duoc_ngay()
    {
        using var factory = new TestAppFactory();
        await SeedCanonicalRolesAsync(factory);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(RegisterUrl, RegisterBody());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var auth = await ReadAuthAsync(response);
        Assert.False(string.IsNullOrWhiteSpace(auth.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(auth.RefreshToken));
        Assert.Equal("Bearer", auth.TokenType);
        Assert.True(auth.ExpiresIn > 0);

        // Điểm của việc đăng ký trả luôn token: khách vào được hệ thống ngay, không phải
        // đăng nhập lại. Test chỉ so status code sẽ bỏ lọt ca token trả về nhưng không dùng được.
        var protectedClient = ClientWith(factory, auth.AccessToken);
        var protectedResponse = await protectedClient.GetAsync(ProtectedUrl);

        Assert.Equal(HttpStatusCode.OK, protectedResponse.StatusCode);

        using var body = JsonDocument.Parse(await protectedResponse.Content.ReadAsStringAsync());
        Assert.Equal(new[] { RoleCodes.Passenger }, RolesOf(body.RootElement));
    }

    [Fact]
    public async Task Dang_ky_luu_mat_khau_dang_bam_BCrypt_va_gan_vai_tro_Hanh_khach()
    {
        using var factory = new TestAppFactory();
        await SeedCanonicalRolesAsync(factory);
        var client = factory.CreateClient();
        var phoneNumber = RandomPhoneNumber();

        var response = await client.PostAsJsonAsync(RegisterUrl, RegisterBody(phoneNumber: phoneNumber));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var user = await QueryAsync(factory, db => db.Users.SingleAsync(u => u.PhoneNumber == phoneNumber));

        // Quy ước F: không bao giờ lưu mật khẩu thô. So mỗi "khác nhau" là chưa đủ —
        // phải khẳng định bản lưu kiểm tra được bằng BCrypt, nếu không thì một hàm băm
        // sai vẫn làm test xanh trong khi mọi lần đăng nhập đều đổ.
        Assert.NotEqual(ValidPassword, user.PasswordHash);
        Assert.True(PasswordService.Verify(ValidPassword, user.PasswordHash));

        // Tài khoản tự đăng ký luôn là Hành khách — không ai tự nâng quyền cho mình được.
        Assert.Equal(RoleIds.Passenger, user.RoleId);

        // Bảng nối cũng phải có dòng tương ứng, nếu không màn hình quản lý vai trò sẽ hiện
        // tài khoản này là "không có vai trò" dù đăng nhập vẫn chạy.
        var hasRoleLink = await QueryAsync(
            factory,
            db => db.UserRoles.AnyAsync(ur => ur.UserId == user.Id && ur.RoleId == RoleIds.Passenger));
        Assert.True(hasRoleLink);
    }

    [Fact]
    public async Task Dang_ky_luu_refresh_token_dang_bam_khong_luu_tho()
    {
        using var factory = new TestAppFactory();
        await SeedCanonicalRolesAsync(factory);
        var client = factory.CreateClient();
        var phoneNumber = RandomPhoneNumber();

        var response = await client.PostAsJsonAsync(RegisterUrl, RegisterBody(phoneNumber: phoneNumber));
        var auth = await ReadAuthAsync(response);

        var user = await QueryAsync(factory, db => db.Users.SingleAsync(u => u.PhoneNumber == phoneNumber));
        var stored = await QueryAsync(factory, db => db.RefreshTokens.SingleAsync(t => t.UserId == user.Id));

        // Refresh token là thứ chiếm được phiên đăng nhập, nên CSDL chỉ giữ bản băm —
        // rò rỉ CSDL không dùng lại được token.
        //
        // Vế thứ hai cố ý tính lại SHA256 ngay tại đây thay vì gọi TokenService.HashRefreshToken:
        // mượn chính hàm của production làm vật chuẩn thì test không còn kiểm được hàm đó nữa.
        // Assert.NotEqual một mình là CHƯA đủ — nó đúng với mọi giá trị khác token thô, kể cả một
        // hằng số, nên mới chỉ chứng minh được "không lưu thô" chứ chưa chứng minh "có băm".
        var expectedHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(auth.RefreshToken)));

        Assert.NotEqual(auth.RefreshToken, stored.TokenHash);
        Assert.Equal(expectedHash, stored.TokenHash);
        Assert.Null(stored.RevokedAt);
        Assert.True(stored.ExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task Dang_ky_trung_so_dien_thoai_tra_409_kem_loi_theo_truong()
    {
        using var factory = new TestAppFactory();
        await SeedCanonicalRolesAsync(factory);
        var client = factory.CreateClient();
        var phoneNumber = RandomPhoneNumber();

        var first = await client.PostAsJsonAsync(RegisterUrl, RegisterBody(phoneNumber: phoneNumber));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync(RegisterUrl, RegisterBody(phoneNumber: phoneNumber));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        // Quy ước D3: lỗi trả kèm tên trường để form đăng ký gắn thẳng vào ô SĐT.
        // Chỉ khẳng định "có lỗi" thì frontend vẫn không biết hiện lỗi ở đâu.
        using var body = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal("Số điện thoại đã được đăng ký", body.RootElement.GetProperty("message").GetString());
        Assert.Equal(
            "Số điện thoại đã được đăng ký",
            body.RootElement.GetProperty("errors").GetProperty("phoneNumber")[0].GetString());
    }

    [Fact]
    public async Task Dang_ky_trung_email_khong_phan_biet_hoa_thuong_tra_409()
    {
        using var factory = new TestAppFactory();
        await SeedCanonicalRolesAsync(factory);
        var client = factory.CreateClient();

        var first = await client.PostAsJsonAsync(RegisterUrl, RegisterBody(email: "ngoc.an@gmail.com"));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // Người dùng gõ hoa vào ô email là chuyện thường — coi hai địa chỉ này khác nhau
        // sẽ tạo ra hai tài khoản cho cùng một người.
        var second = await client.PostAsJsonAsync(RegisterUrl, RegisterBody(email: "NGOC.AN@GMAIL.COM"));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        using var body = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal("Email đã được sử dụng", body.RootElement.GetProperty("errors").GetProperty("email")[0].GetString());
    }

    [Theory]
    [InlineData("fullName", "A")] // ngắn hơn 2 ký tự
    [InlineData("email", "khong-phai-email")] // sai định dạng
    [InlineData("phoneNumber", "123456789")] // không bắt đầu bằng 0[35789]
    [InlineData("phoneNumber", "091234567")] // thiếu một chữ số
    [InlineData("password", "abc123")] // ngắn hơn 8 ký tự
    [InlineData("password", "matkhaumot")] // toàn chữ, thiếu chữ số
    public async Task Dang_ky_du_lieu_sai_tra_400_kem_loi_dung_truong(string field, string value)
    {
        using var factory = new TestAppFactory();
        await SeedCanonicalRolesAsync(factory);
        var client = factory.CreateClient();

        var payload = RegisterBody();
        payload[field] = value;

        var response = await client.PostAsJsonAsync(RegisterUrl, payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Dữ liệu đầu vào không hợp lệ", body.RootElement.GetProperty("message").GetString());
        Assert.Contains(field, ErrorFieldsOf(body.RootElement));
    }

    [Fact]
    public async Task Dang_ky_thieu_du_lieu_tra_400_chu_khong_phai_500()
    {
        using var factory = new TestAppFactory();
        await SeedCanonicalRolesAsync(factory);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(RegisterUrl, new Dictionary<string, string>());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.NotEmpty(ErrorFieldsOf(body.RootElement));
    }

    [Fact]
    public async Task Dang_ky_qua_5_lan_trong_mot_phut_tra_429_kem_Retry_After()
    {
        using var factory = new TestAppFactory();
        await SeedCanonicalRolesAsync(factory);
        var client = factory.CreateClient();

        // Hạn mức là 5 lần/phút cho mỗi IP — xem AuthController.RegisterMaxRequestsPerMinute.
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var allowed = await client.PostAsJsonAsync(RegisterUrl, RegisterBody());
            Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
        }

        var blocked = await client.PostAsJsonAsync(RegisterUrl, RegisterBody());

        // Không có chốt này thì một script đăng ký hàng loạt tài khoản ảo chỉ mất vài giây.
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Contains("Quá nhiều lần đăng ký", await MessageAsync(blocked));
        Assert.Equal("60", blocked.Headers.RetryAfter?.Delta?.TotalSeconds.ToString());
    }

    // ---------------------------------------------------------------------------------------
    // Đăng nhập — POST /api/auth/login
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Dang_nhap_dung_tra_200_kem_token_dung_duoc()
    {
        using var factory = new TestAppFactory();
        var user = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);

        var auth = await LoginAsync(factory.CreateClient(), user.PhoneNumber, ValidPassword);

        var protectedClient = ClientWith(factory, auth.AccessToken);
        var protectedResponse = await protectedClient.GetAsync(ProtectedUrl);

        Assert.Equal(HttpStatusCode.OK, protectedResponse.StatusCode);

        using var body = JsonDocument.Parse(await protectedResponse.Content.ReadAsStringAsync());
        Assert.Equal(user.Id.ToString(), body.RootElement.GetProperty("userId").GetString());
        Assert.Equal(new[] { RoleCodes.Passenger }, RolesOf(body.RootElement));
    }

    [Fact]
    public async Task Dang_nhap_sai_mat_khau_va_so_dien_thoai_khong_ton_tai_tra_cung_mot_thong_bao()
    {
        using var factory = new TestAppFactory();
        var user = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);
        var client = factory.CreateClient();

        var wrongPassword = await client.PostAsJsonAsync(LoginUrl, LoginBody(user.PhoneNumber, "saibetmatkhau1"));
        var unknownPhone = await client.PostAsJsonAsync(LoginUrl, LoginBody("0999999999", ValidPassword));

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownPhone.StatusCode);

        // Cả hai ca phải trả ĐÚNG một câu. Nếu thông báo khác nhau thì kẻ tấn công dò được
        // số điện thoại nào đã đăng ký bằng cách đọc lỗi trả về.
        Assert.Equal(InvalidCredentials, await MessageAsync(wrongPassword));
        Assert.Equal(InvalidCredentials, await MessageAsync(unknownPhone));
    }

    [Fact]
    public async Task Dang_nhap_tai_khoan_bi_khoa_tra_401_kem_ly_do_ro_rang()
    {
        using var factory = new TestAppFactory();
        var user = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, isActive: false);

        var response = await factory.CreateClient()
            .PostAsJsonAsync(LoginUrl, LoginBody(user.PhoneNumber, ValidPassword));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // Khác hai ca trên: người gọi đã nhập đúng mật khẩu, tức đã chứng minh sở hữu tài khoản,
        // nên báo rõ "bị khóa" không lộ thêm thông tin gì mà còn giúp họ biết phải liên hệ ai.
        Assert.Contains("bị khóa", await MessageAsync(response));
    }

    [Fact]
    public async Task Dang_nhap_sai_dinh_dang_tra_400_chu_khong_phai_401()
    {
        using var factory = new TestAppFactory();

        var response = await factory.CreateClient()
            .PostAsJsonAsync(LoginUrl, LoginBody("091", "123"));

        // 400 (dữ liệu sai) khác 401 (sai thông tin đăng nhập) — frontend dựa vào đó để biết
        // khi nào báo lỗi ngay trên ô input, khi nào hiện "sai tài khoản hoặc mật khẩu".
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("phoneNumber", ErrorFieldsOf(body.RootElement));
    }

    // ---------------------------------------------------------------------------------------
    // Làm mới token — POST /api/auth/refresh-token
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Refresh_token_doi_duoc_cap_token_moi()
    {
        using var factory = new TestAppFactory();
        var user = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);
        var client = factory.CreateClient();
        var login = await LoginAsync(client, user.PhoneNumber, ValidPassword);

        var response = await client.PostAsJsonAsync(RefreshUrl, RefreshBody(login.RefreshToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var refreshed = await ReadAuthAsync(response);
        Assert.False(string.IsNullOrWhiteSpace(refreshed.AccessToken));

        // Refresh token mới phải KHÁC token cũ, nếu không thì việc xoay vòng không xảy ra
        // và một token bị lộ sẽ dùng được mãi.
        Assert.NotEqual(login.RefreshToken, refreshed.RefreshToken);

        var protectedClient = ClientWith(factory, refreshed.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await protectedClient.GetAsync(ProtectedUrl)).StatusCode);
    }

    [Fact]
    public async Task Refresh_token_chi_dung_duoc_mot_lan()
    {
        using var factory = new TestAppFactory();
        var user = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);
        var client = factory.CreateClient();
        var login = await LoginAsync(client, user.PhoneNumber, ValidPassword);

        var first = await client.PostAsJsonAsync(RefreshUrl, RefreshBody(login.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsJsonAsync(RefreshUrl, RefreshBody(login.RefreshToken));

        // Xoay vòng token: token cũ bị thu hồi ngay khi dùng. Dùng lại được nghĩa là một
        // refresh token bị đánh cắp sẽ sống song song với phiên thật mà không ai phát hiện.
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Contains("không hợp lệ hoặc đã hết hạn", await MessageAsync(second));
    }

    [Fact]
    public async Task Refresh_token_khong_hop_le_tra_401()
    {
        using var factory = new TestAppFactory();

        var response = await factory.CreateClient()
            .PostAsJsonAsync(RefreshUrl, RefreshBody("day-khong-phai-refresh-token"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------------
    // Đăng xuất — POST /api/auth/logout
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Dang_xuat_thu_hoi_refresh_token_va_goi_lai_van_tra_204()
    {
        using var factory = new TestAppFactory();
        var user = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);
        var client = factory.CreateClient();
        var login = await LoginAsync(client, user.PhoneNumber, ValidPassword);

        var logout = await client.PostAsJsonAsync(LogoutUrl, RefreshBody(login.RefreshToken));
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var stored = await QueryAsync(factory, db => db.RefreshTokens.SingleAsync(t => t.UserId == user.Id));
        Assert.NotNull(stored.RevokedAt);

        // Phiên đã kết thúc thì refresh token không được hồi sinh.
        var refresh = await client.PostAsJsonAsync(RefreshUrl, RefreshBody(login.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);

        // Đăng xuất phải idempotent: mạng chập chờn rồi người dùng bấm lần hai không được
        // hiện lỗi — họ đã đăng xuất rồi, đó mới là điều họ muốn.
        var again = await client.PostAsJsonAsync(LogoutUrl, RefreshBody(login.RefreshToken));
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
    }

    // ---------------------------------------------------------------------------------------
    // Phân quyền đầu-cuối — đăng ký rồi đăng nhập qua đúng API thật
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Tai_khoan_tu_dang_ky_bi_chan_403_o_api_quan_tri()
    {
        using var factory = new TestAppFactory();
        await SeedCanonicalRolesAsync(factory);
        var client = factory.CreateClient();

        var registered = await ReadAuthAsync(await client.PostAsJsonAsync(RegisterUrl, RegisterBody()));
        var passengerClient = ClientWith(factory, registered.AccessToken);

        var response = await passengerClient.GetAsync(AdminUsersUrl);

        // Chốt quan trọng nhất của luồng đăng ký: tài khoản mới toanh không được chạm vào
        // API quản trị. 403 (không đủ quyền) chứ không phải 401 — đã đăng nhập rồi.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("không có quyền", await MessageAsync(response));
    }

    [Fact]
    public async Task Quan_tri_vien_dang_nhap_qua_api_that_thi_vao_duoc_api_quan_tri()
    {
        using var factory = new TestAppFactory();
        var admin = await SeedUserAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        var login = await LoginAsync(factory.CreateClient(), admin.PhoneNumber, ValidPassword);

        var adminClient = ClientWith(factory, login.AccessToken);
        var response = await adminClient.GetAsync(AdminUsersUrl);

        // Vế còn lại của phép so: nếu chỉ có test 403 ở trên thì một hệ thống chặn
        // TẤT CẢ mọi người vẫn làm test xanh.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------------
    // Dữ liệu dựng cho test
    // ---------------------------------------------------------------------------------------

    /// <summary>Body đăng ký hợp lệ; tham số nào bỏ trống thì sinh giá trị ngẫu nhiên hợp lệ.</summary>
    private static Dictionary<string, string> RegisterBody(
        string? fullName = null,
        string? email = null,
        string? phoneNumber = null,
        string? password = null) => new()
        {
            ["fullName"] = fullName ?? "Giàng A Vàng",
            ["email"] = email ?? $"nguoidung{Random.Shared.Next(100_000, 999_999)}@gmail.com",
            ["phoneNumber"] = phoneNumber ?? RandomPhoneNumber(),
            ["password"] = password ?? ValidPassword,
        };

    private static Dictionary<string, string> LoginBody(string phoneNumber, string password) => new()
    {
        ["phoneNumber"] = phoneNumber,
        ["password"] = password,
    };

    private static Dictionary<string, string> RefreshBody(string refreshToken) => new()
    {
        ["refreshToken"] = refreshToken,
    };

    /// <summary>
    /// SĐT hợp lệ theo ràng buộc của API. Cố ý KHÔNG lấy từ <c>Guid.ToString("N")</c>:
    /// chuỗi đó là hệ 16 nên chứa cả chữ a-f, ghép ra "09a3f2b1c4" — sai định dạng SĐT.
    /// </summary>
    private static string RandomPhoneNumber() => "09" + Random.Shared.Next(10_000_000, 99_999_999);

    /// <summary>Bốn vai trò migration seed sẵn — khớp <c>AppDbContext.UserRoles.cs</c>.</summary>
    private static readonly (Guid Id, string Code)[] CanonicalRoles =
    [
        (RoleIds.Admin, RoleCodes.Admin),
        (RoleIds.Manager, RoleCodes.Manager),
        (RoleIds.Driver, RoleCodes.Driver),
        (RoleIds.Passenger, RoleCodes.Passenger),
    ];

    /// <summary>
    /// Dựng sẵn 4 vai trò như migration làm ở production. Cần thiết vì JwtMiddleware đọc lại
    /// vai trò từ CSDL chứ không tin claim trong token: thiếu dòng Roles thì tài khoản đăng nhập
    /// được nhưng không có vai trò nào, và mọi test phân quyền sẽ đổ một cách khó hiểu.
    /// </summary>
    private static async Task SeedCanonicalRolesAsync(TestAppFactory factory)
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

    /// <summary>
    /// Tạo tài khoản đã băm mật khẩu như luồng đăng ký thật, để test đăng nhập đi qua
    /// đúng đường BCrypt.Verify thay vì một nhánh tắt nào đó.
    /// </summary>
    private static async Task<UserEntity> SeedUserAsync(
        TestAppFactory factory,
        Guid roleId,
        string roleCode,
        bool isActive = true)
    {
        await SeedCanonicalRolesAsync(factory);

        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            PhoneNumber = RandomPhoneNumber(),
            Email = $"nguoidung{Random.Shared.Next(100_000, 999_999)}@gmail.com",
            FullName = "Người Dùng Test",
            PasswordHash = PasswordService.HashPassword(ValidPassword),
            IsActive = isActive,
            RoleId = roleId,
            UserRoles = [new UserRole { RoleId = roleId }],
        };

        await factory.SeedAsync(db => db.Users.Add(user));

        return user;
    }

    /// <summary>Đọc dữ liệu đã ghi bằng một scope mới — middleware và controller ghi ở scope khác.</summary>
    private static async Task<T> QueryAsync<T>(TestAppFactory factory, Func<AppDbContext, Task<T>> query)
    {
        using var scope = factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static HttpClient ClientWith(TestAppFactory factory, string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    /// <summary>Bốn trường của <c>AuthResponse</c> — đọc đủ để bắt được đổi tên field ngoài ý muốn.</summary>
    private sealed record AuthTokens(string AccessToken, string RefreshToken, string TokenType, int ExpiresIn);

    /// <summary>
    /// Đăng nhập và khẳng định thành công. Dùng cho các test chỉ cần token làm dữ liệu đầu vào:
    /// nếu để chúng tự đọc body, một lần đăng nhập đổ sẽ hiện lỗi "thiếu property accessToken"
    /// thay vì chỉ đúng vào bước đăng nhập.
    /// </summary>
    private static async Task<AuthTokens> LoginAsync(HttpClient client, string phoneNumber, string password)
    {
        var response = await client.PostAsJsonAsync(LoginUrl, LoginBody(phoneNumber, password));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await ReadAuthAsync(response);
    }

    private static async Task<AuthTokens> ReadAuthAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;

        return new AuthTokens(
            root.GetProperty("accessToken").GetString() ?? string.Empty,
            root.GetProperty("refreshToken").GetString() ?? string.Empty,
            root.GetProperty("tokenType").GetString() ?? string.Empty,
            root.GetProperty("expiresIn").GetInt32());
    }

    private static string[] RolesOf(JsonElement root)
        => root.GetProperty("roles").EnumerateArray().Select(item => item.GetString()!).ToArray();

    /// <summary>Tên các trường có lỗi trong body lỗi theo quy ước D3.</summary>
    private static string[] ErrorFieldsOf(JsonElement root)
        => root.GetProperty("errors").EnumerateObject().Select(property => property.Name).ToArray();

    private static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("message").GetString() ?? string.Empty;
    }
}
