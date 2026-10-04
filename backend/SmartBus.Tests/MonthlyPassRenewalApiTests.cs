using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;
using RouteEntity = SmartBus.Api.Entities.Route;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test tích hợp cho API gia hạn vé tháng — <c>POST /api/monthly-passes/{id}/renew</c>
/// (task story 16 *"Test API đăng ký / gia hạn / hết hạn vé tháng (xUnit)"* — Giàng A Vàng).
///
/// Dùng lại <see cref="TestAppFactory"/> của JwtAuthTests: chạy trên app thật, mỗi test một CSDL
/// InMemory riêng.
///
/// <b>Phạm vi — task ghi ba vế, chỉ hai vế có endpoint để gọi:</b>
///   • <b>Gia hạn</b> — <c>POST /monthly-passes/{id}/renew</c> (Phùng Duy Hoàng): CÓ, và tới trước
///     bộ test này <b>chưa có test nào</b> chạm tới service (chỉ <c>AuditLogCoverageTests</c> gọi
///     endpoint để kiểm nhật ký). Đây là phần chính của file.
///   • <b>Hết hạn</b> — không có API: đó là <c>BackgroundService</c> của Nguyễn Duy Kiên, ruột là
///     <see cref="IMonthlyPassExpiryService"/> (đã có <c>MonthlyPassExpiryServiceTests</c> phủ riêng
///     phần ruột). Phần kiểm được ở tầng API là <b>ranh giới</b> giữa job và nghiệp vụ vé tháng —
///     mục D dưới đây.
///   • <b>Đăng ký</b> — <c>POST /monthly-passes</c> <b>CHƯA LÀM</b> (Trần Trung Hiếu). Không có
///     endpoint thì không có gì để gọi; xem báo cáo kèm theo, không phải thiếu sót của file này.
///
/// <b>Ba luật nền mà bộ test này khoá lại</b> (đọc kèm <see cref="MonthlyPassStatus"/>):
///   • Hiệu lực thật suy từ cặp <c>ValidFrom</c>/<c>ValidTo</c>, KHÔNG đọc cột <c>Status</c> — cột đó
///     do job quét lật nên luôn trễ. Vì vậy mục A có đủ cặp ca lệch: "hết hạn theo ngày nhưng
///     Status còn Active" và "Status Expired nhưng còn hạn".
///   • Gia hạn ghi thêm MỘT DÒNG MỚI, dòng cũ không bị sửa một trường nào.
///   • Chồng lấn so theo KHOẢNG <c>[ValidFrom, ValidTo)</c>, không so Status — vé nối đuôi
///     (mốc này <c>ValidTo</c> = mốc kia <c>ValidFrom</c>) không tính là chồng.
/// </summary>
public class MonthlyPassRenewalApiTests
{
    /// <summary>Mốc "bây giờ" dùng chung cho mọi phép cộng trừ ngày — test chạy trong vài giây.</summary>
    private static readonly DateTime BayGio = DateTime.UtcNow;

    /// <summary>Khuôn mã vé tháng: MP-{mã tuyến}-{6 ký tự A–Z/0–9}.</summary>
    private static readonly Regex KhuonMaVe = new(@"^MP-01-[A-Z0-9]{6}$", RegexOptions.Compiled);

    // =======================================================================================
    // A. Phân quyền và danh tính
    // =======================================================================================

    [Fact]
    public async Task Khong_gui_token_thi_tra_401()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsync(RenewUrl(Guid.NewGuid()), null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Vé của người khác phải trả CÙNG MỘT CÂU với vé không tồn tại — nếu khác câu thì người gọi dò
    /// được vé của người khác có tồn tại hay không. Điều kiện <c>UserId</c> nằm ngay trong truy vấn
    /// của service nên hai ca rơi vào cùng nhánh null.
    /// </summary>
    [Fact]
    public async Task Ve_cua_nguoi_khac_tra_404_cung_cau_voi_ve_khong_ton_tai()
    {
        using var factory = new TestAppFactory();
        await EnsureAllRolesAsync(factory);

        var chuVe = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "0911111111");
        var nguoiLa = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "0922222222");

        var route = await SeedRouteAsync(factory);
        var passType = await SeedPassTypeAsync(factory, "OneMonth", 1, 200_000m);
        var veCuaNguoiKhac = await SeedMonthlyPassAsync(
            factory, chuVe.Id, route.Id, passType.Id, validFrom: BayGio.AddDays(-5), validTo: BayGio.AddDays(25));

        var client = ClientWith(factory, factory.CreateTokenFor(nguoiLa));

        var cuaNguoiKhac = await client.PostAsync(RenewUrl(veCuaNguoiKhac.Id), null);
        var khongTonTai = await client.PostAsync(RenewUrl(Guid.NewGuid()), null);

        Assert.Equal(HttpStatusCode.NotFound, cuaNguoiKhac.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, khongTonTai.StatusCode);

        var cauCuaNguoiKhac = await MessageAsync(cuaNguoiKhac);
        var cauKhongTonTai = await MessageAsync(khongTonTai);

        Assert.Equal("Không tìm thấy vé tháng", cauCuaNguoiKhac);
        Assert.Equal(cauKhongTonTai, cauCuaNguoiKhac);
    }

    [Fact]
    public async Task Id_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsPassengerAsync(factory);

        var response = await client.PostAsync(RenewUrl(Guid.NewGuid()), null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Không tìm thấy vé tháng", await MessageAsync(response));
    }

    /// <summary>
    /// <c>{id}</c> khai <c>{id:guid}</c> nên chuỗi không phải GUID không khớp route nào → 404 của
    /// routing, không phải 400 của model binding. Khoá lại để đổi ràng buộc route là có test đỏ.
    /// </summary>
    [Fact]
    public async Task Id_sai_dinh_dang_guid_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsPassengerAsync(factory);

        var response = await client.PostAsync("/api/monthly-passes/khong-phai-guid/renew", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Không có policy Passenger trong RBAC của dự án nên endpoint dùng <c>[Authorize]</c> trần:
    /// vai trò nào cũng gia hạn được, nhưng chỉ vé của CHÍNH MÌNH.
    /// </summary>
    [Theory]
    [InlineData(RoleCodes.Admin)]
    [InlineData(RoleCodes.Manager)]
    [InlineData(RoleCodes.Driver)]
    [InlineData(RoleCodes.Passenger)]
    public async Task Vai_tro_nao_cung_gia_han_duoc_ve_cua_chinh_minh(string roleCode)
    {
        using var factory = new TestAppFactory();
        var user = await SeedUserAsync(factory, RoleIdsFor(roleCode), roleCode);

        var route = await SeedRouteAsync(factory);
        var passType = await SeedPassTypeAsync(factory, "OneMonth", 1, 200_000m);
        var ve = await SeedMonthlyPassAsync(
            factory, user.Id, route.Id, passType.Id, validFrom: BayGio.AddDays(-5), validTo: BayGio.AddDays(25));

        var client = ClientWith(factory, factory.CreateTokenFor(user));
        var response = await client.PostAsync(RenewUrl(ve.Id), null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // =======================================================================================
    // B. Tính ngày hiệu lực kế tiếp — phần "tính ngày hiệu lực kế tiếp" của task
    // =======================================================================================

    /// <summary>Vé còn hạn: kỳ mới nối đuôi kỳ cũ liền mạch, không mất ngày nào.</summary>
    [Fact]
    public async Task Ve_con_han_thi_ky_moi_noi_duoi_lien_mach()
    {
        using var factory = new TestAppFactory();
        var (client, _, veCu, _) = await DungSanAsync(factory);

        var truoc = DateTime.UtcNow;
        var body = await RenewAsync(client, veCu.Id, body: null);
        var sau = DateTime.UtcNow;

        var validFrom = body.GetProperty("validFrom").GetDateTime();
        var validTo = body.GetProperty("validTo").GetDateTime();

        // Mốc nối CHÍNH XÁC bằng ValidTo của vé cũ — không phải "xấp xỉ bây giờ".
        Assert.Equal(veCu.ValidTo, validFrom);
        Assert.Equal(validFrom.AddMonths(1), validTo);

        // Và mốc nối đó nằm ở tương lai: đây là dòng đăng ký trước cho kỳ sau.
        Assert.True(validFrom > truoc && validFrom < sau.AddDays(30));
    }

    /// <summary>
    /// Vé đã hết hạn: hiệu lực từ BÂY GIỜ, không hồi tố. Hồi tố là tặng không một khoảng thời gian
    /// đã trôi qua mà khách không dùng được gì.
    /// </summary>
    [Fact]
    public async Task Ve_da_het_han_thi_hieu_luc_tu_bay_gio_khong_hoi_to()
    {
        using var factory = new TestAppFactory();
        var (client, _, veCu, _) = await DungSanAsync(
            factory,
            validFrom: BayGio.AddDays(-60),
            validTo: BayGio.AddDays(-30),
            status: MonthlyPassStatus.Expired);

        var truoc = DateTime.UtcNow;
        var body = await RenewAsync(client, veCu.Id, body: null);
        var sau = DateTime.UtcNow;

        var validFrom = body.GetProperty("validFrom").GetDateTime();

        Assert.InRange(validFrom, truoc.AddSeconds(-2), sau.AddSeconds(2));

        // Không hồi tố: kỳ mới bắt đầu SAU mốc hết hạn của kỳ cũ, không kéo lùi về đó.
        Assert.True(validFrom > veCu.ValidTo, "Kỳ mới đã hồi tố về mốc hết hạn của vé cũ.");
    }

    /// <summary>
    /// Vé hết hạn theo NGÀY nhưng cột Status còn Active — đúng trạng thái lệch mà job quét của Kiên
    /// tạo ra trong khoảng giữa lúc ValidTo trôi qua và lúc job chạy. Service phải xử theo NGÀY.
    /// Đây là ca ăn tiền của cả file: đọc Status ở đây là hỏng.
    /// </summary>
    [Fact]
    public async Task Ve_het_han_theo_ngay_nhung_Status_con_Active_van_tinh_la_het_han()
    {
        using var factory = new TestAppFactory();
        var (client, _, veCu, _) = await DungSanAsync(
            factory,
            validFrom: BayGio.AddDays(-40),
            validTo: BayGio.AddDays(-1),
            status: MonthlyPassStatus.Active);

        var truoc = DateTime.UtcNow;
        var body = await RenewAsync(client, veCu.Id, body: null);
        var sau = DateTime.UtcNow;

        var validFrom = body.GetProperty("validFrom").GetDateTime();

        Assert.InRange(validFrom, truoc.AddSeconds(-2), sau.AddSeconds(2));
        Assert.True(validFrom > veCu.ValidTo);
    }

    /// <summary>
    /// Ca đối chứng của ca trên: Status Expired nhưng cặp mốc vẫn còn hạn → vẫn nối đuôi theo NGÀY.
    /// Service không được đọc Status theo chiều nào cả.
    /// </summary>
    [Fact]
    public async Task Ve_Status_Expired_nhung_con_han_thi_van_noi_duoi_theo_ngay()
    {
        using var factory = new TestAppFactory();
        var (client, _, veCu, _) = await DungSanAsync(
            factory,
            validFrom: BayGio.AddDays(-5),
            validTo: BayGio.AddDays(25),
            status: MonthlyPassStatus.Expired);

        var body = await RenewAsync(client, veCu.Id, body: null);

        Assert.Equal(veCu.ValidTo, body.GetProperty("validFrom").GetDateTime());
    }

    /// <summary>
    /// Cộng theo THÁNG LỊCH chứ không phải số ngày: 31/01 + 1 tháng = 28/02 (ngày cuối tháng tự kẹp).
    /// Mốc vé cũ đặt cố định trong tương lai để phép tính không phụ thuộc ngày chạy test.
    /// </summary>
    [Fact]
    public async Task ValidTo_cong_theo_thang_lich_khong_phai_so_ngay()
    {
        using var factory = new TestAppFactory();

        var mocCuoiThang1 = new DateTime(2027, 1, 28, 3, 0, 0, DateTimeKind.Utc);
        var (client, _, veCu, _) = await DungSanAsync(
            factory,
            validFrom: mocCuoiThang1.AddDays(-27),
            validTo: mocCuoiThang1);

        var body = await RenewAsync(client, veCu.Id, body: null);

        Assert.Equal(mocCuoiThang1, body.GetProperty("validFrom").GetDateTime());

        // 28/01 + 1 tháng = 28/02 (không kẹp vì 28/02 tồn tại) — ca kẹp thật nằm ở dòng dưới.
        Assert.Equal(new DateTime(2027, 2, 28, 3, 0, 0, DateTimeKind.Utc), body.GetProperty("validTo").GetDateTime());

        // Và đúng 31 ngày sau — KHÔNG phải 28 ngày như "cộng 1 tháng = 28 ngày" sẽ cho.
        Assert.Equal(31, (body.GetProperty("validTo").GetDateTime() - mocCuoiThang1).TotalDays);
    }

    /// <summary>
    /// Ca kẹp ngày cuối tháng thật: 31/01 + 1 tháng phải ra 28/02, không phải 03/03.
    /// </summary>
    [Fact]
    public async Task Cong_mot_thang_tu_ngay_31_ra_ngay_cuoi_thang_sau()
    {
        using var factory = new TestAppFactory();

        var ngay31 = new DateTime(2027, 1, 31, 0, 0, 0, DateTimeKind.Utc);
        var (client, _, veCu, _) = await DungSanAsync(
            factory,
            validFrom: ngay31.AddDays(-30),
            validTo: ngay31);

        var body = await RenewAsync(client, veCu.Id, body: null);

        Assert.Equal(ngay31, body.GetProperty("validFrom").GetDateTime());
        Assert.Equal(new DateTime(2027, 2, 28, 0, 0, 0, DateTimeKind.Utc), body.GetProperty("validTo").GetDateTime());
    }

    // =======================================================================================
    // C. Loại vé của kỳ mới
    // =======================================================================================

    /// <summary>Đổi loại vé khi gia hạn: thời hạn theo loại MỚI, giá theo giá MỚI.</summary>
    [Fact]
    public async Task Doi_loai_ve_thi_dung_thoi_han_va_gia_cua_loai_moi()
    {
        using var factory = new TestAppFactory();
        var (client, _, veCu, _) = await DungSanAsync(factory);
        await SeedPassTypeAsync(factory, "TwelveMonths", durationMonths: 12, price: 1_800_000m);

        var body = await RenewAsync(client, veCu.Id, new { passTypeCode = "TwelveMonths" });

        var validFrom = body.GetProperty("validFrom").GetDateTime();

        Assert.Equal("TwelveMonths", body.GetProperty("passTypeCode").GetString());
        Assert.Equal(1_800_000m, body.GetProperty("price").GetDecimal());
        Assert.Equal(validFrom.AddMonths(12), body.GetProperty("validTo").GetDateTime());
    }

    /// <summary>Bỏ trống <c>passTypeCode</c> = giữ nguyên loại vé của vé đang gia hạn.</summary>
    [Theory]
    [InlineData(null)]          // gửi {} — trường vắng mặt
    [InlineData("")]            // gửi chuỗi rỗng
    [InlineData("   ")]         // gửi toàn khoảng trắng
    public async Task Bo_trong_passTypeCode_thi_giu_nguyen_loai_ve(string? passTypeCode)
    {
        using var factory = new TestAppFactory();
        var (client, _, veCu, _) = await DungSanAsync(factory, maLoaiVe: "ThreeMonths", thoiHanThang: 3);

        var body = await RenewAsync(client, veCu.Id, new { passTypeCode });

        Assert.Equal("ThreeMonths", body.GetProperty("passTypeCode").GetString());
        Assert.Equal(
            body.GetProperty("validFrom").GetDateTime().AddMonths(3),
            body.GetProperty("validTo").GetDateTime());
    }

    /// <summary>
    /// Không gửi body vẫn phải chạy — controller khai <c>EmptyBodyBehavior.Allow</c> nên body null
    /// không bị model binding từ chối thành 400.
    /// </summary>
    [Fact]
    public async Task Khong_gui_body_thi_giu_nguyen_loai_ve()
    {
        using var factory = new TestAppFactory();
        var (client, _, veCu, _) = await DungSanAsync(factory, maLoaiVe: "ThreeMonths", thoiHanThang: 3);

        var response = await client.PostAsync(RenewUrl(veCu.Id), null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);
        Assert.Equal("ThreeMonths", body.GetProperty("passTypeCode").GetString());
    }

    /// <summary>Mã loại vé được cắt khoảng trắng thừa trước khi tra (service gọi <c>Trim()</c>).</summary>
    [Fact]
    public async Task PassTypeCode_co_khoang_trang_thua_van_khop()
    {
        using var factory = new TestAppFactory();
        var (client, _, veCu, _) = await DungSanAsync(factory);
        await SeedPassTypeAsync(factory, "ThreeMonths", durationMonths: 3, price: 600_000m);

        var body = await RenewAsync(client, veCu.Id, new { passTypeCode = "  ThreeMonths  " });

        // Cắt khoảng trắng rồi mới tra, nên vẫn khớp mã loại vé "ThreeMonths" (không phải OneMonth
        // mà vé cũ đang dùng — nếu tra hụt thì đã ra 404 chứ không phải 200).
        Assert.Equal("ThreeMonths", body.GetProperty("passTypeCode").GetString());
    }

    [Fact]
    public async Task PassTypeCode_khong_khop_loai_ve_nao_tra_404()
    {
        using var factory = new TestAppFactory();
        var (client, _, veCu, _) = await DungSanAsync(factory);

        var response = await client.PostAsJsonAsync(RenewUrl(veCu.Id), new { passTypeCode = "KhongCoLoaiNay" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Không tìm thấy loại vé", await MessageAsync(response));
    }

    /// <summary>Ràng buộc <c>[StringLength(20)]</c> trên DTO → 400 theo khuôn D3 <c>{message, errors}</c>.</summary>
    [Fact]
    public async Task PassTypeCode_qua_20_ky_tu_tra_400()
    {
        using var factory = new TestAppFactory();
        var (client, _, veCu, _) = await DungSanAsync(factory);

        var response = await client.PostAsJsonAsync(RenewUrl(veCu.Id), new { passTypeCode = new string('A', 21) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await ReadJsonAsync(response);
        Assert.Equal("Dữ liệu đầu vào không hợp lệ", body.GetProperty("message").GetString());
        AssertCoLoiTheoTruong(body, "passTypeCode");
    }

    /// <summary>Mã loại vé đúng 20 ký tự vẫn hợp lệ — biên trên là 20, không phải 19.</summary>
    [Fact]
    public async Task PassTypeCode_dung_20_ky_tu_van_qua_duoc_kiem_tra_dinh_dang()
    {
        using var factory = new TestAppFactory();
        var (client, _, veCu, _) = await DungSanAsync(factory);
        await SeedPassTypeAsync(factory, new string('B', 20), durationMonths: 1, price: 100_000m);

        var response = await client.PostAsJsonAsync(RenewUrl(veCu.Id), new { passTypeCode = new string('B', 20) });

        // Qua được model binding; không khớp loại vé nào trong bảng là ca khác — ở đây loại vé CÓ,
        // nên kỳ vọng là 200.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // =======================================================================================
    // D. Dòng cũ không bị sửa + hình dạng dòng mới
    // =======================================================================================

    /// <summary>
    /// Quy tắc chốt trong entity <see cref="MonthlyPass"/>: gia hạn ghi thêm MỘT DÒNG MỚI, dòng cũ
    /// giữ nguyên — nó ở lại làm lịch sử VÀ làm mốc tính kỳ kế tiếp. Sửa tại chỗ thì mất cả hai.
    /// </summary>
    [Fact]
    public async Task Dong_cu_khong_bi_sua_mot_truong_nao()
    {
        using var factory = new TestAppFactory();
        var (client, _, veCu, _) = await DungSanAsync(
            factory,
            validFrom: BayGio.AddDays(-10),
            validTo: BayGio.AddDays(20));

        // Đổi hẳn sang loại vé khác khi gia hạn, để phép so dòng cũ chạy trên ca "khác nhiều nhất
        // có thể": nếu service lỡ ghi đè dòng cũ thì PassTypeId sẽ lộ ra ngay.
        await SeedPassTypeAsync(factory, "ThreeMonths", durationMonths: 3, price: 600_000m);

        var truocKhiGoi = await DocVeAsync(factory, veCu.Id);

        await RenewAsync(client, veCu.Id, new { passTypeCode = "ThreeMonths" });

        var sauKhiGoi = await DocVeAsync(factory, veCu.Id);

        Assert.NotNull(truocKhiGoi);
        Assert.NotNull(sauKhiGoi);

        Assert.Equal(truocKhiGoi!.Id, sauKhiGoi!.Id);
        Assert.Equal(truocKhiGoi.Code, sauKhiGoi.Code);
        Assert.Equal(truocKhiGoi.Price, sauKhiGoi.Price);
        Assert.Equal(truocKhiGoi.ValidFrom, sauKhiGoi.ValidFrom);
        Assert.Equal(truocKhiGoi.ValidTo, sauKhiGoi.ValidTo);
        Assert.Equal(truocKhiGoi.Status, sauKhiGoi.Status);
        Assert.Equal(truocKhiGoi.PassTypeId, sauKhiGoi.PassTypeId);
        Assert.Equal(truocKhiGoi.CreatedAt, sauKhiGoi.CreatedAt);

        // Job quét của Kiên là thứ DUY NHẤT được ghi UpdatedAt — gia hạn không được chạm vào.
        Assert.Null(sauKhiGoi.UpdatedAt);
    }

    [Fact]
    public async Task Tra_ve_dong_moi_voi_id_moi_va_code_moi_dung_khuon()
    {
        using var factory = new TestAppFactory();
        var (client, _, veCu, _) = await DungSanAsync(factory);

        var body = await RenewAsync(client, veCu.Id, body: null);

        var idMoi = body.GetProperty("id").GetGuid();
        var codeMoi = body.GetProperty("code").GetString();

        Assert.NotEqual(veCu.Id, idMoi);
        Assert.NotNull(codeMoi);
        Assert.NotEqual(veCu.Code, codeMoi);
        Assert.Matches(KhuonMaVe, codeMoi);

        // Dòng mới thật sự nằm trong CSDL, không chỉ trong body trả về.
        var trongCsdl = await DocVeAsync(factory, idMoi);
        Assert.NotNull(trongCsdl);
        Assert.Equal(codeMoi, trongCsdl!.Code);
        Assert.Equal(veCu.RouteId, trongCsdl.RouteId);
        Assert.Equal(veCu.UserId, trongCsdl.UserId);
    }

    /// <summary>
    /// <c>price</c> là ẢNH CHỤP giá hiện hành của loại vé, không tra lại giá của vé cũ. Vé cũ mua
    /// lúc giá còn 100 000, gói nay 250 000 → kỳ mới phải chụp 250 000.
    /// </summary>
    [Fact]
    public async Task Price_la_anh_chup_gia_hien_hanh_khong_phai_gia_ve_cu()
    {
        using var factory = new TestAppFactory();

        var route = await SeedRouteAsync(factory);
        // Loại vé đã bị đổi giá SAU khi vé cũ được bán.
        var passType = await SeedPassTypeAsync(factory, "OneMonth", durationMonths: 1, price: 250_000m);
        var chuVe = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);

        // Vé cũ giữ giá đã chụp lúc mua: 100 000.
        var veCu = await SeedMonthlyPassAsync(
            factory, chuVe.Id, route.Id, passType.Id,
            price: 100_000m, validFrom: BayGio.AddDays(-10), validTo: BayGio.AddDays(20));

        var client = ClientWith(factory, factory.CreateTokenFor(chuVe));
        var body = await RenewAsync(client, veCu.Id, body: null);

        Assert.Equal(250_000m, body.GetProperty("price").GetDecimal());

        // Và vé cũ vẫn nguyên giá cũ — ảnh chụp không bị ghi đè ngược.
        Assert.Equal(100_000m, (await DocVeAsync(factory, veCu.Id))!.Price);
    }

    /// <summary>Vé mới luôn <c>Active</c>, kể cả khi vé cũ đang <c>Expired</c>.</summary>
    [Theory]
    [InlineData(MonthlyPassStatus.Active)]
    [InlineData(MonthlyPassStatus.Expired)]
    public async Task Ve_moi_luon_Active(MonthlyPassStatus trangThaiVeCu)
    {
        using var factory = new TestAppFactory();
        var (client, _, veCu, _) = await DungSanAsync(factory, status: trangThaiVeCu);

        var body = await RenewAsync(client, veCu.Id, body: null);

        Assert.Equal("Active", body.GetProperty("status").GetString());
        Assert.Equal("Active", (await DocVeAsync(factory, body.GetProperty("id").GetGuid()))!.Status.ToString());
    }

    // =======================================================================================
    // E. Chồng lấn — validate "trùng vé tháng đang hoạt động trên cùng tuyến"
    // =======================================================================================

    [Fact]
    public async Task Chong_lan_voi_ve_khac_cung_tuyen_tra_409()
    {
        using var factory = new TestAppFactory();

        var route = await SeedRouteAsync(factory);
        var passType = await SeedPassTypeAsync(factory, "OneMonth", 1, 200_000m);
        var chuVe = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);

        // Vé đang gia hạn: còn hạn tới +20 ngày → kỳ mới sẽ là [+20d, +20d+1m].
        var veDangGiaHan = await SeedMonthlyPassAsync(
            factory, chuVe.Id, route.Id, passType.Id, code: "MP-01-AAAAA",
            validFrom: BayGio.AddDays(-10), validTo: BayGio.AddDays(20));

        // Vé khác cùng người cùng tuyến chồng hẳn vào kỳ mới: [+15d, +50d].
        await SeedMonthlyPassAsync(
            factory, chuVe.Id, route.Id, passType.Id, code: "MP-01-BBBBB",
            validFrom: BayGio.AddDays(15), validTo: BayGio.AddDays(50));

        var client = ClientWith(factory, factory.CreateTokenFor(chuVe));
        var response = await client.PostAsync(RenewUrl(veDangGiaHan.Id), null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Đã có vé tháng khác trên cùng tuyến trong khoảng thời gian này", await MessageAsync(response));

        // Và KHÔNG có dòng mới nào được ghi.
        Assert.Equal(2, await DemVeAsync(factory, chuVe.Id));
    }

    /// <summary>
    /// Vé nối đuôi — mốc này <c>ValidTo</c> đúng bằng mốc kia <c>ValidFrom</c> — KHÔNG tính là chồng.
    /// Đây chính là ca gia hạn bình thường, nên nếu so sai thành chồng thì tính năng chính của
    /// story 16 hỏng hoàn toàn.
    /// </summary>
    [Fact]
    public async Task Ve_noi_duoi_khong_tinh_la_chong_lan()
    {
        using var factory = new TestAppFactory();

        var route = await SeedRouteAsync(factory);
        var passType = await SeedPassTypeAsync(factory, "OneMonth", 1, 200_000m);
        var chuVe = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);

        var veDangGiaHan = await SeedMonthlyPassAsync(
            factory, chuVe.Id, route.Id, passType.Id, code: "MP-01-AAAAA",
            validFrom: BayGio.AddDays(-10), validTo: BayGio.AddDays(20));

        // Kỳ mới sẽ là [+20 ngày, +20 ngày + 1 tháng]. Vé kỳ SAU nữa đã đăng ký trước, bắt đầu
        // ĐÚNG mốc kết thúc của kỳ mới — chạm nhau chứ không chồng.
        await SeedMonthlyPassAsync(
            factory, chuVe.Id, route.Id, passType.Id, code: "MP-01-BBBBB",
            validFrom: BayGio.AddDays(20).AddMonths(1), validTo: BayGio.AddDays(20).AddMonths(2));

        var client = ClientWith(factory, factory.CreateTokenFor(chuVe));
        var response = await client.PostAsync(RenewUrl(veDangGiaHan.Id), null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Chồng lấn chỉ tính trên CÙNG TUYẾN — vé tuyến khác chồng khoảng vẫn hợp lệ.</summary>
    [Fact]
    public async Task Ve_chong_lan_o_tuyen_khac_thi_van_200()
    {
        using var factory = new TestAppFactory();

        var route01 = await SeedRouteAsync(factory, "01");
        var route02 = await SeedRouteAsync(factory, "02");
        var passType = await SeedPassTypeAsync(factory, "OneMonth", 1, 200_000m);
        var chuVe = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);

        var veDangGiaHan = await SeedMonthlyPassAsync(
            factory, chuVe.Id, route01.Id, passType.Id, code: "MP-01-AAAAA",
            validFrom: BayGio.AddDays(-10), validTo: BayGio.AddDays(20));

        await SeedMonthlyPassAsync(
            factory, chuVe.Id, route02.Id, passType.Id, code: "MP-02-BBBBB",
            validFrom: BayGio.AddDays(15), validTo: BayGio.AddDays(50));

        var client = ClientWith(factory, factory.CreateTokenFor(chuVe));
        var response = await client.PostAsync(RenewUrl(veDangGiaHan.Id), null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Chồng lấn chỉ tính trên vé của CÙNG NGƯỜI — vé người khác không chặn được ai.</summary>
    [Fact]
    public async Task Ve_chong_lan_cua_nguoi_khac_thi_van_200()
    {
        using var factory = new TestAppFactory();
        await EnsureAllRolesAsync(factory);

        var route = await SeedRouteAsync(factory);
        var passType = await SeedPassTypeAsync(factory, "OneMonth", 1, 200_000m);
        var chuVe = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "0911111111");
        var nguoiKhac = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "0922222222");

        var veDangGiaHan = await SeedMonthlyPassAsync(
            factory, chuVe.Id, route.Id, passType.Id, code: "MP-01-AAAAA",
            validFrom: BayGio.AddDays(-10), validTo: BayGio.AddDays(20));

        await SeedMonthlyPassAsync(
            factory, nguoiKhac.Id, route.Id, passType.Id, code: "MP-01-CCCCC",
            validFrom: BayGio.AddDays(15), validTo: BayGio.AddDays(50));

        var client = ClientWith(factory, factory.CreateTokenFor(chuVe));
        var response = await client.PostAsync(RenewUrl(veDangGiaHan.Id), null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Gia hạn vé cũ hai lần: lần thứ hai nhắm đúng kỳ mà lần thứ nhất vừa ghi, nên phải 409 —
    /// chặn mua trùng kỳ. Đồng thời chứng minh chính vé đang gia hạn không tự chặn chính nó.
    /// </summary>
    [Fact]
    public async Task Gia_han_lan_hai_cung_mot_ve_tra_409()
    {
        using var factory = new TestAppFactory();
        var (client, _, veCu, _) = await DungSanAsync(factory);

        var lanMot = await client.PostAsync(RenewUrl(veCu.Id), null);
        Assert.Equal(HttpStatusCode.OK, lanMot.StatusCode);

        var lanHai = await client.PostAsync(RenewUrl(veCu.Id), null);

        Assert.Equal(HttpStatusCode.Conflict, lanHai.StatusCode);
    }

    /// <summary>
    /// Nhưng gia hạn tiếp chính DÒNG MỚI vừa sinh thì hợp lệ — nó nối đuôi dòng đó, không chồng.
    /// Cặp với ca trên để chứng minh 409 đến từ chồng lấn thật, không phải từ việc gọi hai lần.
    /// </summary>
    [Fact]
    public async Task Gia_han_tiep_dong_vua_sinh_thi_van_200()
    {
        using var factory = new TestAppFactory();
        var (client, _, veCu, _) = await DungSanAsync(factory);

        var lanMot = await RenewAsync(client, veCu.Id, body: null);
        var idDongMoi = lanMot.GetProperty("id").GetGuid();

        var response = await client.PostAsync(RenewUrl(idDongMoi), null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, await DemVeAsync(factory, veCu.UserId));
    }

    // =======================================================================================
    // F. Dữ liệu mồ côi — nhánh phòng vệ của service
    // =======================================================================================

    /// <summary>Vé trỏ tới tuyến không còn tồn tại → 404, không phải NullReferenceException.</summary>
    [Fact]
    public async Task Tuyen_cua_ve_khong_con_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();

        var passType = await SeedPassTypeAsync(factory, "OneMonth", 1, 200_000m);
        var chuVe = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);

        var veMoCoi = await SeedMonthlyPassAsync(
            factory, chuVe.Id, Guid.NewGuid(), passType.Id,
            validFrom: BayGio.AddDays(-5), validTo: BayGio.AddDays(25));

        var client = ClientWith(factory, factory.CreateTokenFor(chuVe));
        var response = await client.PostAsync(RenewUrl(veMoCoi.Id), null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Không tìm thấy tuyến đường", await MessageAsync(response));
    }

    /// <summary>
    /// Vé trỏ tới loại vé không còn tồn tại, và người gọi KHÔNG gửi <c>passTypeCode</c> → tra theo
    /// <c>PassTypeId</c> của vé cũ ra null → 404, không nổ.
    /// </summary>
    [Fact]
    public async Task Loai_ve_cua_ve_khong_con_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();

        var route = await SeedRouteAsync(factory);
        var chuVe = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);

        var veMoCoi = await SeedMonthlyPassAsync(
            factory, chuVe.Id, route.Id, Guid.NewGuid(),
            validFrom: BayGio.AddDays(-5), validTo: BayGio.AddDays(25));

        var client = ClientWith(factory, factory.CreateTokenFor(chuVe));
        var response = await client.PostAsync(RenewUrl(veMoCoi.Id), null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Không tìm thấy loại vé", await MessageAsync(response));
    }

    // =======================================================================================
    // G. Ranh giới "hết hạn" — job quét của Kiên gặp nghiệp vụ gia hạn và tra cứu
    // =======================================================================================

    /// <summary>
    /// Gia hạn vé CÒN HẠN tạo một dòng TƯƠNG LAI (<c>ValidFrom</c> &gt; bây giờ). Dòng đó hợp lệ
    /// trong CSDL nhưng KHÔNG phải vé "đang hoạt động" — <c>GET /me</c> vẫn chỉ trả vé cũ.
    /// </summary>
    [Fact]
    public async Task Sau_khi_gia_han_ky_sau_thi_GET_me_chi_tra_ve_dang_hieu_luc()
    {
        using var factory = new TestAppFactory();
        var (client, _, veCu, _) = await DungSanAsync(factory);

        await RenewAsync(client, veCu.Id, body: null);

        var body = await ReadJsonAsync(await client.GetAsync(MeUrl));

        Assert.Equal([veCu.Code], CodesOf(body));
    }

    /// <summary>
    /// Ngược lại: gia hạn vé ĐÃ HẾT HẠN thì dòng mới có hiệu lực ngay, nên <c>GET /me</c> trả nó
    /// và không trả dòng cũ. Đây là cặp ca cho thấy gia hạn đổi đúng cái mà khách quan tâm.
    /// </summary>
    [Fact]
    public async Task Gia_han_ve_da_het_han_thi_GET_me_tra_ngay_dong_moi()
    {
        using var factory = new TestAppFactory();
        var (client, _, veCu, _) = await DungSanAsync(
            factory,
            validFrom: BayGio.AddDays(-60),
            validTo: BayGio.AddDays(-30),
            status: MonthlyPassStatus.Expired);

        var dongMoi = await RenewAsync(client, veCu.Id, body: null);
        var maDongMoi = dongMoi.GetProperty("code").GetString() ?? string.Empty;

        var body = await ReadJsonAsync(await client.GetAsync(MeUrl));

        Assert.Equal([maDongMoi], CodesOf(body));
    }

    /// <summary>
    /// Job quét hết hạn chỉ được chạm DÒNG CŨ. Dòng mới vừa gia hạn có <c>ValidTo</c> ở tương lai
    /// nên phải đứng yên — nếu job lật nhầm nó thì khách vừa trả tiền đã mất vé.
    /// </summary>
    [Fact]
    public async Task Job_quet_khong_lat_dong_moi_vua_gia_han()
    {
        using var factory = new TestAppFactory();

        // Vé cũ hết hạn theo NGÀY nhưng Status còn Active — đúng ca mà job phải lật.
        var (client, _, veCu, _) = await DungSanAsync(
            factory,
            validFrom: BayGio.AddDays(-40),
            validTo: BayGio.AddDays(-1),
            status: MonthlyPassStatus.Active);

        var dongMoi = await RenewAsync(client, veCu.Id, body: null);
        var idDongMoi = dongMoi.GetProperty("id").GetGuid();

        var soDongLat = await ChayJobQuetAsync(factory, DateTime.UtcNow);

        Assert.Equal(1, soDongLat);

        // Dòng cũ: bị lật, và UpdatedAt được ghi đúng mốc đã dùng để xét hạn.
        var cuSauQuet = await DocVeAsync(factory, veCu.Id);
        Assert.Equal(MonthlyPassStatus.Expired, cuSauQuet!.Status);
        Assert.NotNull(cuSauQuet.UpdatedAt);

        // Dòng mới: đứng yên, và UpdatedAt vẫn null (chưa từng bị sửa).
        var moiSauQuet = await DocVeAsync(factory, idDongMoi);
        Assert.Equal(MonthlyPassStatus.Active, moiSauQuet!.Status);
        Assert.Null(moiSauQuet.UpdatedAt);

        // Và vé mới vẫn là vé đang hoạt động duy nhất.
        var maDongMoi = dongMoi.GetProperty("code").GetString() ?? string.Empty;
        var body = await ReadJsonAsync(await client.GetAsync(MeUrl));
        Assert.Equal([maDongMoi], CodesOf(body));
    }

    // =======================================================================================
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // =======================================================================================

    private const string MeUrl = "/api/monthly-passes/me";

    private static string RenewUrl(Guid id) => $"/api/monthly-passes/{id}/renew";

    /// <summary>
    /// Dựng sàn tối thiểu cho một ca gia hạn: một hành khách, một tuyến "01", loại vé
    /// <paramref name="maLoaiVe"/> đang bán, và một vé cũ thuộc về hành khách đó.
    ///
    /// Cố ý KHÔNG seed loại vé "ThreeMonths" ở đây — ca nào cần thì tự seed, để phép đếm loại vé
    /// trong bảng không phụ thuộc vào helper dùng chung.
    /// </summary>
    private static async Task<(HttpClient Client, UserEntity ChuVe, MonthlyPass VeCu, Guid RouteId)>
        DungSanAsync(
            TestAppFactory factory,
            string maLoaiVe = "OneMonth",
            int thoiHanThang = 1,
            DateTime? validFrom = null,
            DateTime? validTo = null,
            MonthlyPassStatus status = MonthlyPassStatus.Active)
    {
        await EnsureAllRolesAsync(factory);

        var route = await SeedRouteAsync(factory, "01");
        var passType = await SeedPassTypeAsync(factory, maLoaiVe, thoiHanThang, 200_000m);
        var chuVe = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);

        var veCu = await SeedMonthlyPassAsync(
            factory,
            chuVe.Id,
            route.Id,
            passType.Id,
            code: "MP-01-OLD001",
            validFrom: validFrom ?? BayGio.AddDays(-10),
            validTo: validTo ?? BayGio.AddDays(20),
            status: status);

        return (ClientWith(factory, factory.CreateTokenFor(chuVe)), chuVe, veCu, route.Id);
    }

    private static async Task<JsonElement> RenewAsync(HttpClient client, Guid id, object? body)
    {
        var response = body is null
            ? await client.PostAsync(RenewUrl(id), null)
            : await client.PostAsJsonAsync(RenewUrl(id), body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await ReadJsonAsync(response);
    }

    private static async Task<int> ChayJobQuetAsync(TestAppFactory factory, DateTime now)
    {
        using var scope = factory.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<IMonthlyPassExpiryService>();

        return await job.ExpireDuePassesAsync(now);
    }

    private static async Task<MonthlyPass?> DocVeAsync(TestAppFactory factory, Guid id)
    {
        MonthlyPass? timThay = null;

        await factory.SeedAsync(db => timThay = db.MonthlyPasses.AsNoTracking().FirstOrDefault(p => p.Id == id));

        return timThay;
    }

    private static async Task<int> DemVeAsync(TestAppFactory factory, Guid userId)
    {
        var dem = 0;

        await factory.SeedAsync(db => dem = db.MonthlyPasses.Count(p => p.UserId == userId));

        return dem;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        var body = await ReadJsonAsync(response);

        return body.GetProperty("message").GetString() ?? string.Empty;
    }

    private static string[] CodesOf(JsonElement body)
        => body.EnumerateArray()
            .Select(item => item.GetProperty("code").GetString() ?? string.Empty)
            .ToArray();

    private static void AssertCoLoiTheoTruong(JsonElement body, string truong)
    {
        Assert.True(
            body.GetProperty("errors").TryGetProperty(truong, out var loi),
            $"Thiếu errors.{truong} trong body lỗi.");

        Assert.NotEmpty(loi.EnumerateArray());
    }

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
            Name = $"Tuyến {code}",
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
    /// Vé tháng với đủ cặp mốc để cài đúng ca kiểm: mốc ngày, giá và Status cài ĐỘC LẬP nhau —
    /// hợp đồng nói hiệu lực suy từ ngày, nên test phải dựng được cả hai thế lệch.
    /// </summary>
    private static async Task<MonthlyPass> SeedMonthlyPassAsync(
        TestAppFactory factory,
        Guid userId,
        Guid routeId,
        Guid passTypeId,
        string code = "MP-01-ABC123",
        decimal price = 200_000m,
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
            Price = price,
            Code = code,
            ValidFrom = validFrom ?? BayGio.AddDays(-10),
            ValidTo = validTo ?? BayGio.AddDays(20),
            Status = status,
        };

        await factory.SeedAsync(db => db.MonthlyPasses.Add(pass));

        return pass;
    }
}
