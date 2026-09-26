using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test tích hợp cho API truy vấn danh sách nhật ký kiểm toán —
/// <c>GET /api/audit-logs</c> (task story 23 — Nguyễn Duy Kiên).
///
/// Dùng lại <see cref="TestAppFactory"/> của JwtAuthTests: chạy trên app thật (routing, model
/// binding, [Authorize], filter), mỗi test một CSDL InMemory riêng. Gieo thẳng entity
/// <see cref="AuditLog"/> để kiểm soát được CreatedAt / Action / UserId — cùng lối
/// <c>AuditLogExportTests</c>, vì thứ cần kiểm chứng ở đây là DỮ LIỆU ĐỌC RA chứ không phải đường ghi.
///
/// ⚠️ Provider InMemory KHÔNG dựng unique index, khoá ngoại Restrict, HasPrecision hay HasData.
/// Hệ quả được dùng CÓ CHỦ Ý ở ca "người thao tác mồ côi": gieo được bản ghi trỏ tới một User
/// không tồn tại, đúng cảnh dữ liệu cũ còn lại sau khi tài khoản bị xoá cứng.
/// </summary>
public class AuditLogQueryTests
{
    private const string ListUrl = "/api/audit-logs";

    private const string ExportUrl = "/api/audit-logs/export";

    private const string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    // ---------------------------------------------------------------------------------------
    // Phân quyền
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Admin_truy_van_duoc_danh_sach()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        await SeedLogAsync(factory, DateTime.UtcNow.AddHours(-1), target: "Routes:abc");

        var response = await client.GetAsync(ListUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var body = await BodyAsync(response);
        Assert.Equal(["Routes:abc"], TargetsOf(body));
    }

    [Theory]
    [InlineData(RoleCodes.Manager)]
    [InlineData(RoleCodes.Driver)]
    [InlineData(RoleCodes.Passenger)]
    public async Task Khong_phai_Admin_thi_tra_403(string roleCode)
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIdsFor(roleCode), roleCode);

        var response = await client.GetAsync(ListUrl);

        // Đã đăng nhập nhưng thiếu quyền là 403, không phải 401 — frontend phân biệt hai ca này
        // để biết khi nào chuyển về trang đăng nhập, khi nào hiện "không có quyền".
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("không có quyền", MessageOf(await BodyAsync(response)));
    }

    [Fact]
    public async Task Khong_gui_token_thi_tra_401()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync(ListUrl);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------------
    // Envelope và phân trang
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Tra_ve_du_bon_truong_envelope()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        var body = await BodyAsync(await client.GetAsync(ListUrl));

        // Cùng khuôn { items, total, page, pageSize } của GET /routes và GET /admin/users —
        // frontend dùng chung một kiểu kết quả phân trang.
        Assert.Equal(JsonValueKind.Array, body.GetProperty("items").ValueKind);
        Assert.Equal(0, TotalOf(body));
        Assert.Equal(1, body.GetProperty("page").GetInt32());
        Assert.Equal(10, body.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task Phan_trang_tra_dung_so_dong_va_tong()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        for (var i = 0; i < 3; i++)
        {
            await SeedLogAsync(factory, DateTime.UtcNow.AddHours(-i - 1), target: $"Routes:{i}");
        }

        var body = await BodyAsync(await client.GetAsync($"{ListUrl}?pageSize=2"));

        Assert.Equal(2, ItemsOf(body).Length);

        // total là TỔNG số dòng khớp bộ lọc, không phải số dòng của trang — AntD Table dùng nó
        // để vẽ thanh phân trang.
        Assert.Equal(3, TotalOf(body));
        Assert.Equal(2, body.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task Phan_trang_trang_thu_hai_tra_phan_con_lai()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        for (var i = 0; i < 3; i++)
        {
            await SeedLogAsync(factory, DateTime.UtcNow.AddHours(-i - 1), target: $"Routes:{i}");
        }

        var body = await BodyAsync(await client.GetAsync($"{ListUrl}?page=2&pageSize=2"));

        Assert.Single(ItemsOf(body));
        Assert.Equal(3, TotalOf(body));
        Assert.Equal(2, body.GetProperty("page").GetInt32());
    }

    [Fact]
    public async Task Nhieu_ban_ghi_cung_thoi_diem_thi_khong_trung_khong_thieu()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        // Ba bản ghi CÙNG CreatedAt — đúng cảnh middleware ghi liên tiếp trong một giây. Đây là
        // ca chứng minh hai trang KHÔNG trùng và KHÔNG thiếu bản ghi nào.
        //
        // ⚠️ Biết trước giới hạn: ca này KHÔNG bắt được việc xoá ThenBy(Id) — đã thử bằng mutation
        // test và nó vẫn xanh. Lý do: OrderByDescending của LINQ-to-Objects là sắp xếp ỔN ĐỊNH nên
        // trên provider InMemory thứ tự các bản ghi cùng mốc vẫn cố định (theo thứ tự gieo), và
        // hai trang vẫn chia đều tập dữ liệu. Trên PostgreSQL thì thứ tự đó KHÔNG được bảo đảm,
        // nên ThenBy vẫn phải giữ. Khẳng định thứ tự cụ thể (ví dụ "hai id nhỏ nhất đứng trước")
        // sẽ bắt được, nhưng nó mã hoá quy tắc so sánh GUID của .NET — khác quy tắc byte-wise của
        // kiểu uuid trong PostgreSQL — tức đổi một điểm mù lấy một khẳng định sai trên CSDL thật.
        var instant = DateTime.UtcNow.AddHours(-1);
        var expected = new List<Guid>();

        for (var i = 0; i < 3; i++)
        {
            expected.Add(await SeedLogAsync(factory, instant, target: $"Routes:{i}"));
        }

        var page1 = ItemsOf(await BodyAsync(await client.GetAsync($"{ListUrl}?page=1&pageSize=2")));
        var page2 = ItemsOf(await BodyAsync(await client.GetAsync($"{ListUrl}?page=2&pageSize=2")));

        var seen = page1.Concat(page2).Select(item => item.GetProperty("id").GetGuid()).ToList();

        Assert.Equal(3, seen.Count);
        Assert.Equal(expected.Order(), seen.Order());
    }

    [Fact]
    public async Task Sap_xep_moi_nhat_truoc()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        await SeedLogAsync(factory, DateTime.UtcNow.AddHours(-3), target: "Routes:cu-nhat");
        await SeedLogAsync(factory, DateTime.UtcNow.AddHours(-2), target: "Routes:giua");
        await SeedLogAsync(factory, DateTime.UtcNow.AddHours(-1), target: "Routes:moi-nhat");

        var body = await BodyAsync(await client.GetAsync(ListUrl));

        Assert.Equal(["Routes:moi-nhat", "Routes:giua", "Routes:cu-nhat"], TargetsOf(body));
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    public async Task Page_hoac_pageSize_ngoai_khoang_thi_tra_400(string query)
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        var response = await client.GetAsync($"{ListUrl}?{query}");

        // Trần 100 nằm ở [Range] trên DTO chứ không phải một phép cắt im lặng trong service —
        // vượt trần là request hỏng, giống GET /routes và GET /admin/users.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Page_qua_lon_tra_trang_rong_chu_khong_quay_ve_trang_dau()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        for (var i = 0; i < 3; i++)
        {
            await SeedLogAsync(factory, DateTime.UtcNow.AddHours(-i - 1), target: $"Routes:{i}");
        }

        // Page nhận tới int.MaxValue, nên (page - 1) * pageSize tính bằng int sẽ TRÀN thành số âm;
        // Skip với số âm bị coi như 0 và thế là envelope nói "page": 2000000000 trong khi thực chất
        // trả về trang 1 kèm đủ dữ liệu — nói dối về vị trí đang xem.
        var response = await client.GetAsync($"{ListUrl}?page=2000000000&pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await BodyAsync(response);
        Assert.Empty(ItemsOf(body));
        Assert.Equal(3, TotalOf(body));
        Assert.Equal(2000000000, body.GetProperty("page").GetInt32());
    }

    [Fact]
    public async Task Bo_loc_khong_khop_ban_ghi_nao_tra_danh_sach_rong_chu_khong_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        await SeedLogAsync(factory, DateTime.UtcNow.AddHours(-1));

        var response = await client.GetAsync($"{ListUrl}?action=KhongCoHanhDongNay");

        // "Không có bản ghi nào" là một CÂU TRẢ LỜI, không phải một lỗi — cùng lối file Excel
        // rỗng của GET /audit-logs/export.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await BodyAsync(response);
        Assert.Empty(ItemsOf(body));
        Assert.Equal(0, TotalOf(body));
    }

    [Fact]
    public async Task Action_tra_ve_la_chuoi_chu_khong_phai_so()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        await SeedLogAsync(factory, DateTime.UtcNow.AddHours(-1), AuditAction.Delete);

        var item = ItemsOf(await BodyAsync(await client.GetAsync(ListUrl)))[0];

        // Program.cs không đăng ký JsonStringEnumConverter, nên nếu DTO để kiểu enum thì trường
        // này sẽ là 5 — frontend rơi vào nhánh dự phòng của getAuditActionMeta() và hiện số thay
        // vì nhãn tiếng Việt. Ca này khoá hành vi đó lại.
        Assert.Equal(JsonValueKind.String, item.GetProperty("action").ValueKind);
        Assert.Equal("Delete", item.GetProperty("action").GetString());
    }

    // ---------------------------------------------------------------------------------------
    // Bộ lọc thời gian
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Bo_trong_from_va_to_thi_lay_30_ngay_gan_nhat()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        await SeedLogAsync(factory, DateTime.UtcNow.AddDays(-1), target: "Routes:hom-qua");
        await SeedLogAsync(factory, DateTime.UtcNow.AddDays(-45), target: "Routes:qua-cu");

        var body = await BodyAsync(await client.GetAsync(ListUrl));

        // Mặc định 30 ngày gần nhất tính cả hôm nay, y hệt GET /audit-logs/export.
        Assert.Equal(["Routes:hom-qua"], TargetsOf(body));
    }

    [Fact]
    public async Task Khoang_ngay_tinh_tron_ca_hai_dau_mut()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        var day = new DateOnly(2026, 9, 20);

        await SeedLogAsync(factory, At(day.AddDays(-1), 23, 59, 59), target: "Routes:truoc-ngay");
        await SeedLogAsync(factory, At(day, 0, 0, 0), target: "Routes:dau-ngay");
        await SeedLogAsync(factory, At(day, 23, 59, 59), target: "Routes:cuoi-ngay");
        await SeedLogAsync(factory, At(day.AddDays(1), 0, 0, 0), target: "Routes:sau-ngay");

        var body = await BodyAsync(
            await client.GetAsync($"{ListUrl}?from=2026-09-20&to=2026-09-20"));

        // Cả hai đầu mút tính TRỌN ngày: 00:00:00 và 23:59:59 của ngày `to` đều phải có mặt,
        // còn 00:00:00 của ngày kế tiếp phải nằm ngoài.
        Assert.Equal(["Routes:cuoi-ngay", "Routes:dau-ngay"], TargetsOf(body));
        Assert.Equal(2, TotalOf(body));
    }

    [Fact]
    public async Task To_som_hon_from_thi_tra_400()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        var response = await client.GetAsync($"{ListUrl}?from=2026-09-20&to=2026-09-10");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await BodyAsync(response);
        Assert.Contains("Ngày kết thúc", MessageOf(body));
        Assert.True(body.GetProperty("errors").TryGetProperty("to", out _));
    }

    [Fact]
    public async Task From_sai_dinh_dang_thi_tra_400()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        var response = await client.GetAsync($"{ListUrl}?from=hom-qua");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await BodyAsync(response)).GetProperty("errors").TryGetProperty("from", out _));
    }

    // ---------------------------------------------------------------------------------------
    // Bộ lọc theo người
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Loc_theo_nguoi_dung_tra_dung_ban_ghi()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        var first = await SeedUserAsync(factory, RoleIds.Admin, RoleCodes.Admin);
        var second = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager);

        await SeedLogAsync(factory, DateTime.UtcNow.AddHours(-3), userId: first.Id, target: "Routes:cua-nguoi-1");
        await SeedLogAsync(factory, DateTime.UtcNow.AddHours(-2), userId: second.Id, target: "Routes:cua-nguoi-2");

        var body = await BodyAsync(await client.GetAsync($"{ListUrl}?userId={first.Id}"));

        Assert.Equal(["Routes:cua-nguoi-1"], TargetsOf(body));
    }

    [Fact]
    public async Task UserId_sai_dinh_dang_thi_tra_400()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        // Khác lối "giá trị lạ trả rỗng" của `action`: userId là ĐỊNH DANH, gõ sai là request
        // hỏng chứ không phải "bộ lọc không khớp gì".
        var response = await client.GetAsync($"{ListUrl}?userId=khong-phai-guid");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True((await BodyAsync(response)).GetProperty("errors").TryGetProperty("userId", out _));
    }

    [Fact]
    public async Task UserId_dung_nhung_khong_co_ban_ghi_tra_danh_sach_rong()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        await SeedLogAsync(factory, DateTime.UtcNow.AddHours(-1), target: "Routes:cua-nguoi-khac");

        var response = await client.GetAsync($"{ListUrl}?userId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await BodyAsync(response);
        Assert.Empty(ItemsOf(body));
        Assert.Equal(0, TotalOf(body));
    }

    // ---------------------------------------------------------------------------------------
    // Bộ lọc theo hành động
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Loc_theo_hanh_dong_tra_dung_ban_ghi()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        await SeedLogAsync(factory, DateTime.UtcNow.AddHours(-3), AuditAction.Create, target: "Routes:tao-moi");
        await SeedLogAsync(factory, DateTime.UtcNow.AddHours(-2), AuditAction.Delete, target: "Routes:da-xoa");

        var body = await BodyAsync(await client.GetAsync($"{ListUrl}?action=Delete"));

        Assert.Equal(["Routes:da-xoa"], TargetsOf(body));
    }

    [Fact]
    public async Task Hanh_dong_khac_hoa_thuong_van_loc_duoc()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        await SeedLogAsync(factory, DateTime.UtcNow.AddHours(-3), AuditAction.Create, target: "Routes:tao-moi");
        await SeedLogAsync(factory, DateTime.UtcNow.AddHours(-2), AuditAction.Delete, target: "Routes:da-xoa");

        var body = await BodyAsync(await client.GetAsync($"{ListUrl}?action=delete"));

        Assert.Equal(["Routes:da-xoa"], TargetsOf(body));
    }

    [Fact]
    public async Task Hanh_dong_la_chuoi_so_tra_danh_sach_rong()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        await SeedLogAsync(factory, DateTime.UtcNow.AddHours(-3), AuditAction.Create);
        await SeedLogAsync(factory, DateTime.UtcNow.AddHours(-2), AuditAction.Delete);

        var body = await BodyAsync(await client.GetAsync($"{ListUrl}?action=3"));

        // "3" KHÔNG được hiểu thành Delete. Đây là ca khoá quyết định không dùng Enum.TryParse
        // (nó chấp nhận chuỗi số) mà so với Enum.GetNames — quy ước A3: trạng thái lưu dạng chuỗi
        // đọc được, nên một chuỗi số không phải là mã hợp lệ.
        Assert.Empty(ItemsOf(body));
    }

    [Fact]
    public async Task Hanh_dong_dai_qua_20_ky_tu_thi_tra_400()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        var response = await client.GetAsync($"{ListUrl}?action={new string('a', 21)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await BodyAsync(response)).GetProperty("errors").TryGetProperty("action", out _));
    }

    // ---------------------------------------------------------------------------------------
    // Người thao tác ghép kèm
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Kem_ho_ten_va_sdt_nguoi_thao_tac()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        var actor = await SeedUserAsync(factory, RoleIds.Admin, RoleCodes.Admin, "Trần Thị B");
        await SeedLogAsync(factory, DateTime.UtcNow.AddHours(-1), userId: actor.Id);

        var item = ItemsOf(await BodyAsync(await client.GetAsync(ListUrl)))[0];

        Assert.Equal(actor.Id, item.GetProperty("userId").GetGuid());
        Assert.Equal("Trần Thị B", item.GetProperty("userFullName").GetString());
        Assert.Equal(actor.PhoneNumber, item.GetProperty("userPhoneNumber").GetString());
    }

    [Fact]
    public async Task Ban_ghi_khong_xac_dinh_duoc_nguoi_thao_tac_tra_null()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        // Đăng nhập thất bại với SĐT không tồn tại — không có ai để ghép tên.
        await SeedLogAsync(factory, DateTime.UtcNow.AddHours(-1), AuditAction.LoginFailed, userId: null);

        var item = ItemsOf(await BodyAsync(await client.GetAsync(ListUrl)))[0];

        Assert.Equal(JsonValueKind.Null, item.GetProperty("userId").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("userFullName").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("userPhoneNumber").ValueKind);
    }

    [Fact]
    public async Task Nguoi_thao_tac_mo_coi_thi_tra_null_chu_khong_loi()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        // Bản ghi trỏ tới một User không tồn tại. InMemory không dựng khoá ngoại nên gieo được —
        // đúng cảnh dữ liệu cũ còn lại sau khi tài khoản bị xoá cứng. Phải trả null, không 500.
        await SeedLogAsync(factory, DateTime.UtcNow.AddHours(-1), userId: Guid.NewGuid());

        var response = await client.GetAsync(ListUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var item = ItemsOf(await BodyAsync(response))[0];
        Assert.Equal(JsonValueKind.Null, item.GetProperty("userFullName").ValueKind);
    }

    [Fact]
    public async Task Khong_lo_hash_mat_khau_trong_ket_qua()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        var actor = await SeedUserAsync(factory, RoleIds.Admin, RoleCodes.Admin);
        await SeedLogAsync(factory, DateTime.UtcNow.AddHours(-1), userId: actor.Id);

        var raw = await (await client.GetAsync(ListUrl)).Content.ReadAsStringAsync();

        // Chốt chặn cho quyết định KHÔNG dùng Include(a => a.User): đường đó kéo cả cột
        // PasswordHash (hash BCrypt) về chỉ để lấy họ tên. Bản ghi nhật ký là dữ liệu Admin xem,
        // nhưng hash mật khẩu thì không có lý do gì để rời khỏi CSDL.
        Assert.DoesNotContain("passwordHash", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("matkhau123", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("$2", raw, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------
    // Kết hợp, không tác dụng phụ, và va chạm đường dẫn
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Ket_hop_ca_ba_bo_loc_tra_giao_cua_cac_dieu_kien()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        var actor = await SeedUserAsync(factory, RoleIds.Admin, RoleCodes.Admin);
        var other = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager);

        var day = new DateOnly(2026, 9, 20);

        await SeedLogAsync(factory, At(day, 10, 0, 0), AuditAction.Delete, actor.Id, "Routes:dung-het");
        await SeedLogAsync(factory, At(day, 11, 0, 0), AuditAction.Create, actor.Id, "Routes:sai-hanh-dong");
        await SeedLogAsync(factory, At(day, 12, 0, 0), AuditAction.Delete, other.Id, "Routes:sai-nguoi");
        await SeedLogAsync(factory, At(day.AddDays(-1), 13, 0, 0), AuditAction.Delete, actor.Id, "Routes:sai-ngay");

        var body = await BodyAsync(await client.GetAsync(
            $"{ListUrl}?from=2026-09-20&to=2026-09-20&userId={actor.Id}&action=Delete"));

        Assert.Equal(["Routes:dung-het"], TargetsOf(body));
        Assert.Equal(1, TotalOf(body));
    }

    [Fact]
    public async Task Truy_van_khong_sinh_them_ban_ghi_nhat_ky()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        await SeedLogAsync(factory, DateTime.UtcNow.AddHours(-1));
        var before = await CountLogsAsync(factory);

        var response = await client.GetAsync(ListUrl);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // AuditLogMiddleware chỉ ghi POST/PUT/PATCH/DELETE nên thao tác ĐỌC nhật ký không để lại
        // vết — nghĩa là câu "tuần trước ai đã xem nhật ký" không trả lời được từ chính bảng này.
        // Giới hạn đã biết của thiết kế; ca này giữ cho hành vi đó không đổi ngoài ý muốn.
        Assert.Equal(before, await CountLogsAsync(factory));
    }

    [Fact]
    public async Task Duong_dan_export_khong_bi_duong_dan_danh_sach_nuot()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        // Hai controller cùng tiền tố api/audit-logs. Đoạn literal "export" phải thắng đoạn tham
        // số, và controller danh sách không được khai [HttpGet("export")] — nếu khai, ASP.NET Core
        // ném AmbiguousMatchException. Ca này giữ cho tuyên bố đó không bị phá về sau.
        var response = await client.GetAsync(ExportUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(XlsxContentType, response.Content.Headers.ContentType?.MediaType);
    }

    // ---------------------------------------------------------------------------------------
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // ---------------------------------------------------------------------------------------

    private static DateTime At(DateOnly date, int hour, int minute, int second)
        => date.ToDateTime(new TimeOnly(hour, minute, second), DateTimeKind.Utc);

    private static async Task<HttpClient> SignInAsync(TestAppFactory factory, Guid roleId, string roleCode)
    {
        await EnsureAllRolesAsync(factory);
        var user = await SeedUserAsync(factory, roleId, roleCode);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", factory.CreateTokenFor(user));

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

    /// <summary>
    /// Gieo một bản ghi nhật ký và trả về Id của nó — cần Id để khẳng định phân trang không trùng
    /// không thiếu. <see cref="AuditLog.Id"/> đã có giá trị mặc định <c>Guid.NewGuid()</c> ngay
    /// khi dựng object nên đọc lại được trước khi lưu.
    /// </summary>
    private static async Task<Guid> SeedLogAsync(
        TestAppFactory factory,
        DateTime createdAtUtc,
        AuditAction action = AuditAction.Create,
        Guid? userId = null,
        string? target = "Routes:3f2a1b0c-0000-0000-0000-000000000000",
        string? ipAddress = "203.0.113.5")
    {
        var log = new AuditLog
        {
            CreatedAt = createdAtUtc,
            Action = action,
            UserId = userId,
            Target = target,
            IpAddress = ipAddress,
        };

        await factory.SeedAsync(db => db.AuditLogs.Add(log));

        return log.Id;
    }

    private static async Task<int> CountLogsAsync(TestAppFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.AuditLogs.CountAsync();
    }

    private static JsonElement[] ItemsOf(JsonElement body)
        => body.GetProperty("items").EnumerateArray().ToArray();

    private static string[] TargetsOf(JsonElement body)
        => ItemsOf(body).Select(item => item.GetProperty("target").GetString() ?? string.Empty).ToArray();

    private static int TotalOf(JsonElement body) => body.GetProperty("total").GetInt32();

    /// <summary>
    /// Đọc thân response thành JSON. Đọc được ĐÚNG MỘT LẦN cho mỗi response — stream của
    /// <c>HttpContent</c> không tua lại được. Muốn nhiều khẳng định trên cùng một thân bài thì
    /// lấy <see cref="JsonElement"/> ra rồi khẳng định trên nó.
    /// </summary>
    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string MessageOf(JsonElement body)
        => body.GetProperty("message").GetString() ?? string.Empty;
}
