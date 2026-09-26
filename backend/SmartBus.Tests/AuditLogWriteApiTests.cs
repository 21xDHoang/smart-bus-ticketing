using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;
using RouteEntity = SmartBus.Api.Entities.Route;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test tích hợp cho ĐƯỜNG GHI nhật ký hoạt động (US 23, task 36 — Giàng A Vàng).
///
/// Vì sao có file này dù đã có ba file test nhật ký:
///   • <c>AuditLogTests</c> (Vàng Thị Dăm) kiểm middleware qua controller tổng hợp
///     <c>_test/audit/widgets</c> — một bảng không tồn tại trong dự án.
///   • <c>AuditLogQueryTests</c> (Nguyễn Duy Kiên) và <c>AuditLogExportTests</c> (Phùng Duy Hoàng)
///     kiểm đường ĐỌC, và gieo thẳng entity <see cref="AuditLog"/> — cố ý không đi qua đường ghi.
///
/// Ba khoảng trống còn lại, chính là phạm vi file này:
///   1. Đăng nhập / đăng xuất / đăng nhập thất bại (AuthController tự gọi
///      <see cref="IAuditLogService"/>, không qua middleware) chưa từng được kiểm là CÓ ghi.
///      <c>AuditAction.Login</c> trước file này không xuất hiện ở bất kỳ test nào.
///   2. Endpoint thật của dự án (tuyến, trạm, giá vé, tài khoản) chưa lần nào được kiểm là
///      có ghi nhật ký — đặc biệt ca lồng <c>/api/routes/{routeId}/fares/{id}</c>.
///   3. Chưa có test nào nối hai nửa với nhau: thao tác thật rồi ĐỌC LẠI bằng API truy vấn.
///
/// ⚠️ Provider InMemory KHÔNG dựng unique index, khoá ngoại hay HasData. Nên không seed sẵn
/// 4 vai trò thì mọi request đều 403 dù token hợp lệ — xem <c>EnsureAllRolesAsync</c>.
/// </summary>
public class AuditLogWriteApiTests
{
    private const string LoginUrl = "/api/auth/login";

    private const string LogoutUrl = "/api/auth/logout";

    private const string RefreshUrl = "/api/auth/refresh-token";

    private const string RegisterUrl = "/api/auth/register";

    private const string RoutesUrl = "/api/routes";

    private const string StopsUrl = "/api/stops";

    private const string UsersUrl = "/api/admin/users";

    private const string AuditLogsUrl = "/api/audit-logs";

    private const string MatKhau = "matkhau123";

    private const string SoDienThoai = "0912345678";

    // =======================================================================================
    // A. Đăng nhập / đăng xuất — AuthController tự ghi, middleware không được ghi thêm
    // =======================================================================================

    [Fact]
    public async Task Dang_nhap_thanh_cong_ghi_dung_mot_ban_ghi_Login()
    {
        using var factory = new TestAppFactory();
        var user = await SeedUserAsync(
            factory, RoleIds.Passenger, RoleCodes.Passenger, phoneNumber: SoDienThoai);

        var response = await LoginAsync(factory, SoDienThoai, MatKhau);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Assert.Single chứ không phải Assert.Contains: khẳng định ĐÚNG MỘT bản ghi vừa bắt
        // được việc AuthController ghi (Login) vừa bắt được việc middleware ghi thêm lần nữa.
        // Middleware bỏ qua /api/auth (IgnoredPaths); nếu ai xoá dòng đó, mỗi lần đăng nhập sẽ
        // có hai bản ghi và test này đỏ ngay.
        var log = Assert.Single(await LogsAsync(factory));

        Assert.Equal(AuditAction.Login, log.Action);
        Assert.Equal(user.Id, log.UserId);
        // Đăng nhập không nhắm vào bản ghi nào — Target NULL là đúng hợp đồng của AuditLog.
        Assert.Null(log.Target);
    }

    [Fact]
    public async Task Dang_nhap_sai_mat_khau_ghi_LoginFailed_kem_UserId_cua_tai_khoan()
    {
        using var factory = new TestAppFactory();
        var user = await SeedUserAsync(
            factory, RoleIds.Passenger, RoleCodes.Passenger, phoneNumber: SoDienThoai);

        var response = await LoginAsync(factory, SoDienThoai, "matkhaust");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var log = Assert.Single(await LogsAsync(factory));

        Assert.Equal(AuditAction.LoginFailed, log.Action);
        // Tra ra được tài khoản (chỉ sai mật khẩu) thì nhật ký phải có người thực hiện —
        // đây là dấu vết để phát hiện dò mật khẩu nhắm vào một tài khoản cụ thể.
        Assert.Equal(user.Id, log.UserId);
        Assert.Null(log.Target);
    }

    [Fact]
    public async Task Dang_nhap_voi_sdt_chua_dang_ky_ghi_LoginFailed_voi_UserId_null()
    {
        using var factory = new TestAppFactory();

        var response = await LoginAsync(factory, "0987654321", MatKhau);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var log = Assert.Single(await LogsAsync(factory));

        Assert.Equal(AuditAction.LoginFailed, log.Action);
        // Không tra ra tài khoản nào thì không có ai để gán — NULL, không phải Guid.Empty.
        // Guid.Empty sẽ trỏ tới một người dùng không tồn tại và làm hỏng luôn phép JOIN khi đọc.
        Assert.Null(log.UserId);
    }

    [Fact]
    public async Task Dang_nhap_vao_tai_khoan_bi_khoa_ghi_LoginFailed_kem_UserId()
    {
        using var factory = new TestAppFactory();
        var user = await SeedUserAsync(
            factory, RoleIds.Driver, RoleCodes.Driver, phoneNumber: SoDienThoai, isActive: false);

        var response = await LoginAsync(factory, SoDienThoai, MatKhau);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var log = Assert.Single(await LogsAsync(factory));

        Assert.Equal(AuditAction.LoginFailed, log.Action);
        // Tài khoản bị khoá đã tra ra được trước khi kiểm mật khẩu, nên vẫn ghi được người thực hiện.
        Assert.Equal(user.Id, log.UserId);
    }

    [Fact]
    public async Task Hai_nhanh_dang_nhap_that_bai_de_do_duoc_thi_dung_chung_mot_thong_bao()
    {
        using var factory = new TestAppFactory();
        var saiMatKhau = await SeedUserAsync(
            factory, RoleIds.Passenger, RoleCodes.Passenger, phoneNumber: "0922222222");

        var khongTonTai = await LoginAsync(factory, "0933333333", MatKhau);
        var saiMatKhauResponse = await LoginAsync(factory, "0922222222", "matkhaust");

        var thongBaoChung = await MessageOfAsync(khongTonTai);

        // Hai nhánh này là hai câu trả lời cho cùng một câu hỏi "số điện thoại này có tài khoản
        // không". Để khác nhau thì người ngoài dò được SĐT nào đã đăng ký, chỉ bằng cách gõ bừa
        // mật khẩu — nên chúng BẮT BUỘC phải giống nhau từng chữ.
        Assert.Equal(thongBaoChung, await MessageOfAsync(saiMatKhauResponse));

        // Nhưng nhật ký thì PHẢI phân biệt được: người ngoài chỉ thấy một câu, còn kiểm toán viên
        // phải biết lần thất bại này nhắm vào tài khoản có thật hay chỉ là dò số điện thoại.
        // Không có khác biệt này thì task B28 không giải quyết được gì.
        var logs = await LogsAsync(factory);

        Assert.Equal(2, logs.Count);
        Assert.All(logs, log => Assert.Equal(AuditAction.LoginFailed, log.Action));
        // Không khẳng định theo thứ tự: hai bản ghi sinh trong cùng một mili giây nên CreatedAt
        // bằng nhau, sắp xếp tiếp theo Id (Guid ngẫu nhiên) là thứ tự bất kỳ.
        Assert.Single(logs, log => log.UserId is null);
        Assert.Single(logs, log => log.UserId == saiMatKhau.Id);
    }

    [Fact]
    public async Task Tai_khoan_bi_khoa_bao_ro_ly_do_vi_da_chung_minh_so_huu_tai_khoan()
    {
        using var factory = new TestAppFactory();
        var biKhoa = await SeedUserAsync(
            factory, RoleIds.Driver, RoleCodes.Driver, phoneNumber: "0911111111", isActive: false);

        var biKhoaResponse = await LoginAsync(factory, "0911111111", MatKhau);
        var khongTonTai = await LoginAsync(factory, "0933333333", MatKhau);

        // Khác hai nhánh dò ở trên là CHỦ Ý, không phải thiếu nhất quán — AuthService kiểm tra
        // khoá SAU khi đã xác thực mật khẩu (xem doc-comment ở AuthService.LoginAsync): người gọi
        // đã chứng minh sở hữu tài khoản, nên nói rõ "bị khoá" không lộ thêm thông tin gì, mà lại
        // tránh được cảnh người dùng gõ đúng mật khẩu rồi vẫn bị báo "sai mật khẩu" và đi đổi mật
        // khẩu vô ích. Ca này khoá hành vi đó lại để không ai "sửa" cho nó giống hai nhánh kia.
        //
        // So với thông báo của nhánh không tra ra tài khoản chứ không so với chuỗi cứng: đổi câu
        // chữ không làm đỏ test, chỉ đổi hành vi mới làm đỏ.
        Assert.NotEqual(await MessageOfAsync(khongTonTai), await MessageOfAsync(biKhoaResponse));

        // Vẫn phải ghi nhật ký như mọi lần thất bại khác, và vẫn kèm được người thực hiện —
        // tài khoản bị khoá đã tra ra trước khi kiểm mật khẩu nên UserId có giá trị.
        var logs = await LogsAsync(factory);

        Assert.Equal(2, logs.Count);
        Assert.All(logs, log => Assert.Equal(AuditAction.LoginFailed, log.Action));
        Assert.Single(logs, log => log.UserId == biKhoa.Id);
        Assert.Single(logs, log => log.UserId is null);
    }

    [Fact]
    public async Task Dang_xuat_ghi_Logout_kem_UserId_va_khong_co_doi_tuong()
    {
        using var factory = new TestAppFactory();
        var user = await SeedUserAsync(
            factory, RoleIds.Passenger, RoleCodes.Passenger, phoneNumber: SoDienThoai);

        var refreshToken = await RefreshTokenAfterLoginAsync(factory);

        var response = await factory.CreateClient()
            .PostAsJsonAsync(LogoutUrl, new { refreshToken });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var log = Assert.Single(await LogsAsync(factory), item => item.Action == AuditAction.Logout);

        Assert.Equal(user.Id, log.UserId);
        Assert.Null(log.Target);

        // Đăng nhập + đăng xuất = đúng hai bản ghi, không có bản ghi thứ ba nào chen vào.
        Assert.Equal(2, (await LogsAsync(factory)).Count);
    }

    [Fact]
    public async Task Dang_xuat_bang_token_khong_ton_tai_thi_khong_ghi_nhat_ky()
    {
        using var factory = new TestAppFactory();

        var response = await factory.CreateClient()
            .PostAsJsonAsync(LogoutUrl, new { refreshToken = "token-khong-ton-tai" });

        // Vẫn 204: không tiết lộ token có thật hay không, và với người gọi thì phiên đã kết thúc.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Không có gì đổi trên hệ thống thì không ghi — nhất quán với middleware (chỉ ghi 2xx
        // của thao tác thật sự làm dữ liệu đổi). Log đầy token rác sẽ làm ngập nhật ký kiểm toán.
        Assert.Empty(await LogsAsync(factory));
    }

    [Fact]
    public async Task Lam_moi_token_khong_ghi_nhat_ky()
    {
        using var factory = new TestAppFactory();
        await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, phoneNumber: SoDienThoai);

        var refreshToken = await RefreshTokenAfterLoginAsync(factory);

        var response = await factory.CreateClient()
            .PostAsJsonAsync(RefreshUrl, new { refreshToken });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Đúng một bản ghi: của lần đăng nhập. Làm mới token không phải hành động kiểm toán —
        // nó không đổi dữ liệu và xảy ra vài phút một lần, ghi vào chỉ tổ nhiễu.
        var log = Assert.Single(await LogsAsync(factory));
        Assert.Equal(AuditAction.Login, log.Action);
    }

    [Fact]
    public async Task Dang_ky_tai_khoan_khong_ghi_nhat_ky()
    {
        using var factory = new TestAppFactory();
        await EnsureAllRolesAsync(factory);

        var response = await factory.CreateClient().PostAsJsonAsync(RegisterUrl, new
        {
            fullName = "Hành Khách Mới",
            email = "hanhkhach@example.com",
            phoneNumber = SoDienThoai,
            password = MatKhau,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // Đánh đổi đã biết, ghi ở doc-comment của AuditLogMiddleware.IgnoredPaths: /api/auth nằm
        // trong nhóm tự ghi log nên middleware bỏ qua, mà Register thì không tự gọi
        // IAuditLogService. Kết quả: tạo tài khoản Hành khách KHÔNG có dấu vết trong nhật ký.
        //
        // Test này khoá hành vi đó lại để nó là quyết định đã biết chứ không phải lỗi âm thầm:
        // ngày nào có người bổ sung ghi nhật ký cho đăng ký, test đỏ và người sửa sẽ đọc được
        // đúng lý do ở đây. Đường tạo tài khoản qua màn hình quản trị thì VẪN được ghi — xem
        // Tao_tai_khoan_qua_man_hinh_quan_tri_van_duoc_ghi.
        Assert.Empty(await LogsAsync(factory));
    }

    // =======================================================================================
    // B. Endpoint thật của dự án — middleware ghi thay vì controller
    // =======================================================================================

    [Fact]
    public async Task Tao_tuyen_ghi_Create_voi_Target_Routes_kem_id()
    {
        using var factory = new TestAppFactory();
        var (client, admin) = await SignInAsAdminAsync(factory);

        var response = await client.PostAsJsonAsync(RoutesUrl, RouteBody("01"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // Id lấy từ chính response: khẳng định thêm rằng middleware đọc đúng trường "id" trong
        // body mà KHÔNG làm hỏng body trả về cho client (nó phải đệm rồi trả lại nguyên vẹn).
        var createdId = (await BodyAsync(response)).GetProperty("id").GetGuid();

        var log = Assert.Single(await LogsAsync(factory));

        Assert.Equal(AuditAction.Create, log.Action);
        Assert.Equal(admin.Id, log.UserId);
        Assert.Equal($"Routes:{createdId}", log.Target);
    }

    [Fact]
    public async Task Sua_tuyen_ghi_Update_voi_Target_Routes_kem_id()
    {
        using var factory = new TestAppFactory();
        var (client, admin) = await SignInAsAdminAsync(factory);
        var route = await SeedRouteAsync(factory, "01");

        var response = await client.PutAsJsonAsync($"{RoutesUrl}/{route.Id}", RouteBody("01", ten: "Tuyến 01 (đổi tên)"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var log = Assert.Single(await LogsAsync(factory));

        Assert.Equal(AuditAction.Update, log.Action);
        Assert.Equal(admin.Id, log.UserId);
        // PUT không đệm response — id lấy thẳng từ đường dẫn.
        Assert.Equal($"Routes:{route.Id}", log.Target);
    }

    [Fact]
    public async Task Xoa_mem_tuyen_ghi_Delete_voi_Target_Routes_kem_id()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsAdminAsync(factory);
        var route = await SeedRouteAsync(factory, "01");

        var response = await client.DeleteAsync($"{RoutesUrl}/{route.Id}");

        // Xoá mềm: 200 kèm tuyến đã chuyển sang Inactive, không phải 204.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var log = Assert.Single(await LogsAsync(factory));

        Assert.Equal(AuditAction.Delete, log.Action);
        Assert.Equal($"Routes:{route.Id}", log.Target);
    }

    [Fact]
    public async Task Tao_tram_ghi_Create_voi_Target_Stops_kem_id()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsAdminAsync(factory);

        var response = await client.PostAsJsonAsync(StopsUrl, StopBody("Trạm Cầu Giấy"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var createdId = (await BodyAsync(response)).GetProperty("id").GetGuid();

        var log = Assert.Single(await LogsAsync(factory));

        Assert.Equal(AuditAction.Create, log.Action);
        Assert.Equal($"Stops:{createdId}", log.Target);
    }

    [Fact]
    public async Task Xoa_tram_tra_204_van_duoc_ghi_nhat_ky()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsAdminAsync(factory);
        var stop = await SeedStopAsync(factory, "Trạm Cầu Giấy");

        var response = await client.DeleteAsync($"{StopsUrl}/{stop.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // 204 là 2xx nên middleware vẫn ghi. Đây là ca dễ mất nhất khi ai đó "tối ưu" điều kiện
        // ghi thành `StatusCode == 200`: xoá trạm là thao tác phá huỷ, mất dấu vết là mất nặng nhất.
        var log = Assert.Single(await LogsAsync(factory));

        Assert.Equal(AuditAction.Delete, log.Action);
        Assert.Equal($"Stops:{stop.Id}", log.Target);
    }

    [Fact]
    public async Task Tao_gia_ve_duoi_tuyen_ghi_Target_Fares_chu_khong_phai_Routes()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsAdminAsync(factory);
        var route = await SeedRouteAsync(factory, "01");

        var response = await client.PostAsJsonAsync(
            FaresUrl(route.Id), new { passengerType = "Standard", price = 7000m });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var fareId = (await BodyAsync(response)).GetProperty("id").GetGuid();

        var log = Assert.Single(await LogsAsync(factory));

        Assert.Equal(AuditAction.Create, log.Action);
        // Đây là cái bẫy mà ResolveTableName sinh ra để chặn: đường dẫn có HAI tham số
        // ({routeId} của tuyến và {id} của dòng giá). Lấy đoạn đầu tiên sẽ ra "Routes" — sai bảng,
        // và kiểm toán viên tra "Routes:<id giá vé>" sẽ không bao giờ thấy bản ghi nào.
        Assert.Equal($"Fares:{fareId}", log.Target);
        Assert.DoesNotContain(route.Id.ToString(), log.Target);
    }

    [Fact]
    public async Task Sua_gia_ve_duoi_tuyen_ghi_Target_Fares_kem_id_tren_duong_dan()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsAdminAsync(factory);
        var route = await SeedRouteAsync(factory, "01");
        var fare = await SeedFareAsync(factory, route.Id, PassengerType.Standard, 7000m);

        var response = await client.PutAsJsonAsync(
            $"{FaresUrl(route.Id)}/{fare.Id}", new { price = 8000m });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var log = Assert.Single(await LogsAsync(factory));

        Assert.Equal(AuditAction.Update, log.Action);
        // PUT không đệm body nên id phải lấy từ RouteValues["id"] — chính là id DÒNG GIÁ,
        // không phải {routeId}. Lấy nhầm routeId thì tra ra một tuyến, không ra dòng giá đã sửa.
        Assert.Equal($"Fares:{fare.Id}", log.Target);
    }

    [Fact]
    public async Task Khoa_tai_khoan_ghi_Update_voi_Target_Users()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsAdminAsync(factory);
        var target = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, phoneNumber: "0911111111");

        var response = await client.PatchAsJsonAsync(
            $"{UsersUrl}/{target.Id}/status", new { isActive = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var log = Assert.Single(await LogsAsync(factory));

        // PATCH gộp vào Update (quy ước A3): khoá tài khoản và sửa hồ sơ cùng nhóm "sửa".
        Assert.Equal(AuditAction.Update, log.Action);
        Assert.Equal($"Users:{target.Id}", log.Target);
    }

    [Fact]
    public async Task Gan_vai_tro_ghi_Update_voi_Target_Users()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsAdminAsync(factory);
        var target = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, phoneNumber: "0911111111");

        var response = await client.PutAsJsonAsync(
            $"{UsersUrl}/{target.Id}/roles", new { roleCodes = new[] { RoleCodes.Driver } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var log = Assert.Single(await LogsAsync(factory));

        Assert.Equal(AuditAction.Update, log.Action);
        Assert.Equal($"Users:{target.Id}", log.Target);
    }

    [Fact]
    public async Task Tao_tai_khoan_qua_man_hinh_quan_tri_van_duoc_ghi()
    {
        using var factory = new TestAppFactory();
        var (client, admin) = await SignInAsAdminAsync(factory);

        var response = await client.PostAsJsonAsync(UsersUrl, new
        {
            fullName = "Tài Xế Mới",
            phoneNumber = "0912345679",
            password = MatKhau,
            roleCode = RoleCodes.Driver,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var createdId = (await BodyAsync(response)).GetProperty("id").GetGuid();

        var log = Assert.Single(await LogsAsync(factory));

        Assert.Equal(AuditAction.Create, log.Action);
        Assert.Equal(admin.Id, log.UserId);
        Assert.Equal($"Users:{createdId}", log.Target);
    }

    [Fact]
    public async Task Thao_tac_that_bai_khong_ghi_nhat_ky()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsAdminAsync(factory);
        await SeedRouteAsync(factory, "01");

        // Trùng mã tuyến: 400 kèm errors.code.
        var trungMa = await client.PostAsJsonAsync(RoutesUrl, RouteBody("01"));
        // Thiếu tên trạm — model binding chặn ở tầng validation, controller không chạy.
        var thieuTen = await client.PostAsJsonAsync(StopsUrl, new { address = "Địa chỉ", latitude = 21.0, longitude = 105.8 });
        // Toạ độ ngoài khoảng hợp lệ.
        var saiToaDo = await client.PostAsJsonAsync(StopsUrl, StopBody("Trạm Sai Toạ Độ", latitude: 200));

        Assert.Equal(HttpStatusCode.BadRequest, trungMa.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, thieuTen.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, saiToaDo.StatusCode);

        // Dữ liệu chưa hề đổi thì nhật ký ghi vào là nhật ký sai sự thật — kiểm toán viên sẽ
        // tưởng có người đã sửa bảng giá lúc 3 giờ sáng, trong khi thực tế request bị từ chối.
        Assert.Empty(await LogsAsync(factory));
    }

    [Fact]
    public async Task Doc_du_lieu_khong_ghi_nhat_ky()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsAdminAsync(factory);
        var route = await SeedRouteAsync(factory, "01");
        var stop = await SeedStopAsync(factory, "Trạm Cầu Giấy");
        await SeedFareAsync(factory, route.Id, PassengerType.Standard, 7000m);

        var responses = new[]
        {
            await client.GetAsync(RoutesUrl),
            await client.GetAsync($"{RoutesUrl}/{route.Id}"),
            await client.GetAsync(StopsUrl),
            await client.GetAsync($"{StopsUrl}/{stop.Id}"),
            await client.GetAsync(FaresUrl(route.Id)),
            await client.GetAsync(UsersUrl),
            await client.GetAsync(AuditLogsUrl),
        };

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));

        // Đọc không làm dữ liệu đổi. Ghi nhật ký cho GET thì bảng AuditLogs phình theo lưu lượng
        // đọc và chôn mất các thao tác thật — bảng chỉ ghi thêm nên không dọn được.
        Assert.Empty(await LogsAsync(factory));
    }

    // =======================================================================================
    // C. Vòng tròn ghi → truy vấn: thao tác thật rồi ĐỌC LẠI bằng API truy vấn
    // =======================================================================================

    [Fact]
    public async Task Thao_tac_vua_lam_tra_ve_duoc_ngay_bang_API_truy_van()
    {
        using var factory = new TestAppFactory();
        var (client, admin) = await SignInAsAdminAsync(factory);

        var createdRouteId = (await BodyAsync(await client.PostAsJsonAsync(RoutesUrl, RouteBody("01"))))
            .GetProperty("id")
            .GetGuid();

        var body = await BodyAsync(await client.GetAsync(
            $"{AuditLogsUrl}?action=Create&userId={admin.Id}"));

        var item = Assert.Single(ItemsOf(body));

        Assert.Equal("Create", item.GetProperty("action").GetString());
        Assert.Equal($"Routes:{createdRouteId}", item.GetProperty("target").GetString());
        Assert.Equal(admin.Id, item.GetProperty("userId").GetGuid());

        // Hai nửa phải khớp nhau: bản ghi do middleware ghi phải tra ra được thông tin người
        // thao tác qua phép JOIN của tầng đọc. Mất tên/SĐT ở đây thì màn hình nhật ký chỉ hiện
        // một chuỗi GUID vô nghĩa với kiểm toán viên.
        Assert.Equal(admin.FullName, item.GetProperty("userFullName").GetString());
        Assert.Equal(admin.PhoneNumber, item.GetProperty("userPhoneNumber").GetString());

        // Và chính lời gọi truy vấn vừa rồi cũng không được sinh thêm bản ghi nào.
        Assert.Single(await LogsAsync(factory));
    }

    [Fact]
    public async Task Loc_theo_nguoi_thao_tac_tach_duoc_nhat_ky_cua_hai_quan_ly()
    {
        using var factory = new TestAppFactory();
        var (clientA, managerA) = await SignInAsManagerAsync(factory, "0911111111");
        var (clientB, managerB) = await SignInAsManagerAsync(factory, "0922222222");

        // Đọc nhật ký phải bằng tài khoản Admin: /api/audit-logs gác bằng AdminOnly, quản lý gọi
        // vào nhận 403. Token phát thẳng nên lần đăng nhập này không sinh bản ghi nào.
        var (adminClient, _) = await SignInAsAdminAsync(factory);

        var routeA = (await BodyAsync(await clientA.PostAsJsonAsync(RoutesUrl, RouteBody("01"))))
            .GetProperty("id").GetGuid();
        var routeB = (await BodyAsync(await clientB.PostAsJsonAsync(RoutesUrl, RouteBody("02"))))
            .GetProperty("id").GetGuid();

        var cuaA = await BodyAsync(await adminClient.GetAsync($"{AuditLogsUrl}?userId={managerA.Id}"));
        var cuaB = await BodyAsync(await adminClient.GetAsync($"{AuditLogsUrl}?userId={managerB.Id}"));

        Assert.Equal([$"Routes:{routeA}"], TargetsOf(cuaA));
        Assert.Equal([$"Routes:{routeB}"], TargetsOf(cuaB));

        // Không lọc thì thấy cả hai — bộ lọc phải THU HẸP kết quả, không phải che mất bản ghi.
        var tatCa = await BodyAsync(await adminClient.GetAsync(AuditLogsUrl));
        Assert.Equal(2, TotalOf(tatCa));

        // Người thao tác trong bản ghi phải là quản lý đã gọi API, không phải ai khác.
        // So bằng SĐT chứ không bằng họ tên: hai quản lý ở đây cố ý cùng tên, nên họ tên không
        // phân biệt được ai với ai — đúng cảnh hai người trùng tên thật ngoài đời.
        Assert.Equal(managerA.PhoneNumber, ItemsOf(cuaA)[0].GetProperty("userPhoneNumber").GetString());
        Assert.Equal(managerB.PhoneNumber, ItemsOf(cuaB)[0].GetProperty("userPhoneNumber").GetString());
    }

    [Fact]
    public async Task Nhat_ky_dang_nhap_tra_ve_duoc_qua_API_truy_van()
    {
        using var factory = new TestAppFactory();
        var admin = await SeedUserAsync(
            factory, RoleIds.Admin, RoleCodes.Admin, fullName: "Quản Trị Viên", phoneNumber: SoDienThoai);

        // Đăng nhập thật để lấy token, đồng thời sinh bản ghi Login.
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(factory, SoDienThoai, MatKhau)).StatusCode);

        var client = ClientWith(factory, factory.CreateTokenFor(admin));

        var body = await BodyAsync(await client.GetAsync($"{AuditLogsUrl}?action=Login"));

        var item = Assert.Single(ItemsOf(body));

        Assert.Equal("Login", item.GetProperty("action").GetString());
        Assert.Equal(admin.Id, item.GetProperty("userId").GetGuid());
        // Đăng nhập là hành động duy nhất KHÔNG đi qua middleware. Nếu tầng đọc chỉ hiểu bản ghi
        // do middleware sinh (Action suy từ verb, Target dạng "Bảng:Id") thì nó sẽ bỏ rơi hoặc
        // làm hỏng nhóm bản ghi này — Target NULL và Action không thuộc Create/Update/Delete.
        Assert.Equal(JsonValueKind.Null, item.GetProperty("target").ValueKind);
    }

    [Fact]
    public async Task Nhat_ky_dang_nhap_that_bai_voi_sdt_la_tra_ve_UserId_null()
    {
        using var factory = new TestAppFactory();
        var admin = await SeedUserAsync(
            factory, RoleIds.Admin, RoleCodes.Admin, phoneNumber: SoDienThoai);
        var client = ClientWith(factory, factory.CreateTokenFor(admin));

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await LoginAsync(factory, "0987654321", MatKhau)).StatusCode);

        var body = await BodyAsync(await client.GetAsync($"{AuditLogsUrl}?action=LoginFailed"));

        var item = Assert.Single(ItemsOf(body));

        // Đây là giá trị của cả task B28: response trả về giống hệt một lần gõ sai mật khẩu,
        // nhưng nhật ký nói rõ "có kẻ đang dò số điện thoại" — và tra ra được cả tên/SĐT NULL
        // nghĩa là tài khoản đó không tồn tại, chứ không phải lỗi hiển thị.
        Assert.Equal(AuditAction.LoginFailed.ToString(), item.GetProperty("action").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("userId").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("userFullName").ValueKind);
    }

    // =======================================================================================
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // =======================================================================================

    private static string FaresUrl(Guid routeId) => $"{RoutesUrl}/{routeId}/fares";

    private static object RouteBody(string code, string? ten = null, decimal distanceKm = 12.5m)
        => new
        {
            code,
            name = ten ?? $"Tuyến {code}",
            origin = "Bến Thành",
            destination = "Chợ Lớn",
            distanceKm,
        };

    private static object StopBody(string name, double latitude = 21.0307)
        => new { name, address = $"Địa chỉ {name}", latitude, longitude = 105.8034 };

    /// <summary>
    /// Đọc nhật ký thẳng từ CSDL, sắp xếp theo CreatedAt rồi tới Id.
    /// Sắp xếp tiếp theo Id là bắt buộc: nhiều bản ghi sinh trong cùng một mili giây nên
    /// CreatedAt bằng nhau, chỉ OrderBy(CreatedAt) là thứ tự không xác định giữa các lần chạy.
    /// </summary>
    private static async Task<List<AuditLog>> LogsAsync(TestAppFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.AuditLogs
            .AsNoTracking()
            .OrderBy(log => log.CreatedAt)
            .ThenBy(log => log.Id)
            .ToListAsync();
    }

    /// <summary>Đăng nhập qua API thật — đây là đường DUY NHẤT sinh bản ghi Login/LoginFailed.</summary>
    private static async Task<HttpResponseMessage> LoginAsync(
        TestAppFactory factory,
        string phoneNumber,
        string password)
        => await factory.CreateClient().PostAsJsonAsync(LoginUrl, new { phoneNumber, password });

    private static async Task<string> MessageOfAsync(HttpResponseMessage response)
        => (await BodyAsync(response)).GetProperty("message").GetString() ?? string.Empty;

    /// <summary>Đăng nhập rồi trả về refresh token — dùng cho các ca đăng xuất / làm mới token.</summary>
    private static async Task<string> RefreshTokenAfterLoginAsync(TestAppFactory factory)
    {
        var response = await LoginAsync(factory, SoDienThoai, MatKhau);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await BodyAsync(response)).GetProperty("refreshToken").GetString()!;
    }

    private static HttpClient ClientWith(TestAppFactory factory, string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    private static async Task<(HttpClient Client, UserEntity User)> SignInAsAdminAsync(TestAppFactory factory)
        => await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin, "Quản Trị Viên");

    private static async Task<(HttpClient Client, UserEntity User)> SignInAsManagerAsync(
        TestAppFactory factory,
        string phoneNumber)
        => await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý", phoneNumber);

    private static async Task<(HttpClient Client, UserEntity User)> SignInAsync(
        TestAppFactory factory,
        Guid roleId,
        string roleCode,
        string fullName,
        string? phoneNumber = null)
    {
        await EnsureAllRolesAsync(factory);
        var user = await SeedUserAsync(factory, roleId, roleCode, fullName, phoneNumber);

        // Token phát bằng chính ITokenService của app nhưng KHÔNG đi qua /api/auth/login —
        // nhờ vậy các ca ở mục B và C có nền nhật ký sạch, không lẫn bản ghi Login.
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
        string fullName = "Người Dùng Test",
        string? phoneNumber = null,
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
            PhoneNumber = phoneNumber ?? "09" + Random.Shared.Next(10_000_000, 99_999_999),
            FullName = fullName,
            PasswordHash = PasswordService.HashPassword(MatKhau),
            IsActive = isActive,
            RoleId = roleId,
            // Vai trò chính phải luôn có mặt ở bảng nối — đúng bất biến mà AuthService giữ.
            UserRoles = [new UserRole { RoleId = roleId }],
        };

        await factory.SeedAsync(db => db.Users.Add(user));

        return user;
    }

    private static async Task<RouteEntity> SeedRouteAsync(TestAppFactory factory, string code)
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

    private static async Task<Stop> SeedStopAsync(TestAppFactory factory, string name)
    {
        var stop = new Stop
        {
            Id = Guid.NewGuid(),
            Name = name,
            Address = $"Địa chỉ {name}",
            Latitude = 21.0307,
            Longitude = 105.8034,
        };

        await factory.SeedAsync(db => db.Stops.Add(stop));

        return stop;
    }

    private static async Task<Fare> SeedFareAsync(
        TestAppFactory factory,
        Guid routeId,
        PassengerType passengerType,
        decimal price)
    {
        // Chỉ gán khoá ngoại, KHÔNG gán navigation: Route được seed ở scope khác, gán navigation
        // vào đây sẽ khiến EF tưởng nó là bản ghi mới và chèn trùng khoá chính.
        var fare = new Fare
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            PassengerType = passengerType,
            Price = price,
        };

        await factory.SeedAsync(db => db.Fares.Add(fare));

        return fare;
    }

    private static JsonElement[] ItemsOf(JsonElement body)
        => body.GetProperty("items").EnumerateArray().ToArray();

    private static string[] TargetsOf(JsonElement body)
        => ItemsOf(body).Select(item => item.GetProperty("target").GetString() ?? string.Empty).ToArray();

    private static int TotalOf(JsonElement body) => body.GetProperty("total").GetInt32();

    /// <summary>
    /// Đọc thân response thành JSON. Đọc được ĐÚNG MỘT LẦN cho mỗi response — stream của
    /// <c>HttpContent</c> không tua lại được.
    /// </summary>
    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();
}
