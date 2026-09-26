using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test tích hợp cho API xuất nhật ký kiểm toán ra Excel —
/// <c>GET /api/audit-logs/export</c> (task story 23 — Phùng Duy Hoàng).
///
/// Dùng lại <see cref="TestAppFactory"/> của JwtAuthTests: chạy trên app thật, mỗi test một CSDL
/// InMemory riêng.
///
/// ⚠️ Đây là file test ĐẦU TIÊN của dự án gieo thẳng entity <see cref="AuditLog"/> bằng
/// <c>SeedAsync</c> — <c>AuditLogTests</c> chỉ đọc những dòng mà middleware tự ghi. Ở đây phải
/// gieo thẳng vì thứ cần kiểm chứng là DỮ LIỆU ĐỌC RA (bộ lọc, thứ tự, nội dung file), không
/// phải đường ghi.
///
/// ⚠️ Provider InMemory KHÔNG dựng unique index, khoá ngoại Restrict, HasPrecision hay HasData,
/// nên các khẳng định dưới đây chỉ nói về byte, header và nội dung file — đúng phạm vi.
/// </summary>
public class AuditLogExportTests
{
    private const string ExportUrl = "/api/audit-logs/export";

    private const string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Hàng tiêu đề cột — hàng 1–2 là khối tự mô tả, hàng 3 để trống.</summary>
    private const int HeaderRow = 4;

    private static readonly string[] ExpectedHeaders =
    [
        "Thời gian (UTC)",
        "Người thao tác",
        "Số điện thoại",
        "Hành động",
        "Đối tượng",
        "Địa chỉ IP",
        "Mã người dùng",
    ];

    /// <summary>4 byte đầu của mọi file .zip — và .xlsx là một file zip.</summary>
    private static readonly byte[] ZipMagic = [0x50, 0x4B, 0x03, 0x04];

    // ---------------------------------------------------------------------------------------
    // Phân quyền
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Admin_tai_duoc_file_xlsx()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        var response = await client.GetAsync(ExportUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(XlsxContentType, response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();

        // Kiểm tra "có trả file thật không" bằng magic bytes; phần "nội dung có đúng không" do
        // các ca mở workbook bằng ClosedXML bên dưới lo.
        Assert.True(bytes.Length > ZipMagic.Length, "File trả về rỗng hoặc quá ngắn.");
        Assert.Equal(ZipMagic, bytes[..ZipMagic.Length]);
    }

    [Theory]
    [InlineData(RoleCodes.Manager)]
    [InlineData(RoleCodes.Driver)]
    [InlineData(RoleCodes.Passenger)]
    public async Task Khong_phai_Admin_thi_tra_403(string roleCode)
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIdsFor(roleCode), roleCode);

        var response = await client.GetAsync(ExportUrl);

        // Đã đăng nhập nhưng thiếu quyền là 403, không phải 401 — frontend phân biệt hai ca này
        // để biết khi nào chuyển về trang đăng nhập, khi nào hiện "không có quyền".
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("không có quyền", MessageOf(await BodyAsync(response)));

        // Nhật ký là dữ liệu nhạy cảm: người thiếu quyền không được nhận một byte nào của file.
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Khong_gui_token_thi_tra_401()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync(ExportUrl);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------------
    // Bộ lọc thời gian
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Bo_trong_from_va_to_thi_lay_30_ngay_gan_nhat()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        await SeedLogAsync(factory, DateTime.UtcNow.AddDays(-1), target: "Routes:moi");
        await SeedLogAsync(factory, DateTime.UtcNow.AddDays(-45), target: "Routes:cu");

        using var workbook = await WorkbookAsync(await client.GetAsync(ExportUrl));
        var targets = TargetColumn(workbook);

        Assert.Contains("Routes:moi", targets);
        Assert.DoesNotContain("Routes:cu", targets);
    }

    [Fact]
    public async Task Khoang_ngay_tinh_tron_ca_hai_dau_mut()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        var to = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3);
        var from = to.AddDays(-2);

        // Đúng mốc 00:00:00 của ngày đầu và đúng giây cuối cùng của ngày cuối.
        await SeedLogAsync(factory, At(from, 0, 0, 0), target: "Routes:dau-ky");
        await SeedLogAsync(factory, At(to, 23, 59, 59), target: "Routes:cuoi-ky");

        // Ngay sau đó một giây — ngoài khoảng, phải bị loại.
        await SeedLogAsync(factory, At(to.AddDays(1), 0, 0, 0), target: "Routes:ngoai-ky");

        using var workbook = await WorkbookAsync(
            await client.GetAsync($"{ExportUrl}?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}"));

        var targets = TargetColumn(workbook);

        // Nếu cận trên bị hiểu là 00:00 của ngày `to` thì "cuoi-ky" mất; nếu cận dưới bị hiểu là
        // 00:00 của ngày hôm sau thì "dau-ky" mất. Cả hai đều là lỗi im lặng với người kiểm toán.
        Assert.Equal(["Routes:cuoi-ky", "Routes:dau-ky"], targets);
    }

    [Fact]
    public async Task To_som_hon_from_thi_tra_400()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        var response = await client.GetAsync($"{ExportUrl}?from=2026-09-20&to=2026-09-10");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var json = await BodyAsync(response);
        Assert.Contains("Ngày kết thúc", MessageOf(json));
        Assert.True(json.GetProperty("errors").TryGetProperty("to", out _));
    }

    [Fact]
    public async Task UserId_sai_dinh_dang_thi_tra_400()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        // Khác lối "giá trị lạ trả rỗng" của `action`: userId là ĐỊNH DANH, gõ sai là request
        // hỏng chứ không phải "bộ lọc không khớp gì".
        var response = await client.GetAsync($"{ExportUrl}?userId=khong-phai-guid");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    // ---------------------------------------------------------------------------------------
    // Bộ lọc theo người và theo hành động
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Loc_theo_nguoi_thao_tac()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        var first = await SeedUserAsync(factory, RoleIds.Admin, RoleCodes.Admin);
        var second = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager);

        await SeedLogAsync(factory, DateTime.UtcNow.AddHours(-3), userId: first.Id, target: "Routes:cua-nguoi-1");
        await SeedLogAsync(factory, DateTime.UtcNow.AddHours(-2), userId: second.Id, target: "Routes:cua-nguoi-2");

        using var workbook = await WorkbookAsync(await client.GetAsync($"{ExportUrl}?userId={first.Id}"));

        Assert.Equal(["Routes:cua-nguoi-1"], TargetColumn(workbook));
    }

    [Fact]
    public async Task Loc_theo_hanh_dong()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        await SeedLogAsync(factory, DateTime.UtcNow.AddHours(-3), AuditAction.Create, target: "Routes:tao-moi");
        await SeedLogAsync(factory, DateTime.UtcNow.AddHours(-2), AuditAction.Delete, target: "Routes:da-xoa");

        using var workbook = await WorkbookAsync(await client.GetAsync($"{ExportUrl}?action=Delete"));

        Assert.Equal(["Routes:da-xoa"], TargetColumn(workbook));
    }

    [Fact]
    public async Task Hanh_dong_go_sai_tra_file_rong_chu_khong_400()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        await SeedLogAsync(factory, DateTime.UtcNow.AddHours(-1));

        var response = await client.GetAsync($"{ExportUrl}?action=KhongCoHanhDongNay");

        // `action` là MÃ, mà một mã lạ có thể hợp lệ trong tương lai — cùng lối `status` của
        // GET /routes. Khác hẳn userId ở trên.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var workbook = await WorkbookAsync(response);
        Assert.Empty(TargetColumn(workbook));
    }

    [Fact]
    public async Task Khong_co_ban_ghi_nao_thi_tra_file_chi_co_tieu_de_chu_khong_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        var response = await client.GetAsync(ExportUrl);

        // "Tuần đó không ai xoá gì" là một CÂU TRẢ LỜI, không phải một lỗi. Trả 404 thì trình
        // duyệt tải về một file JSON lỗi đội tên .xlsx và người kiểm toán mở ra thấy rác.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(XlsxContentType, response.Content.Headers.ContentType?.MediaType);

        using var workbook = await WorkbookAsync(response);
        Assert.Equal(HeaderRow, workbook.Worksheet(1).LastRowUsed()!.RowNumber());
    }

    // ---------------------------------------------------------------------------------------
    // Trần dòng
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Vuot_tran_thi_tra_400_chu_khong_tra_file()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        var start = DateTime.UtcNow.AddHours(-1);
        var logs = Enumerable.Range(0, AuditLogExportService.MaxRows + 1)
            .Select(i => new AuditLog
            {
                CreatedAt = start.AddTicks(i),
                Action = AuditAction.Create,
                Target = $"Routes:{i}",
            })
            .ToList();

        await factory.SeedAsync(db => db.AuditLogs.AddRange(logs));

        var response = await client.GetAsync(ExportUrl);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Điểm mấu chốt: KHÔNG được trả về một file .xlsx cụt trông như thành công. Content-Type
        // phải là JSON, và thân bài phải theo đúng envelope { message, errors } của dự án.
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var json = await BodyAsync(response);
        Assert.Contains($"vượt trần {AuditLogExportService.MaxRows}", MessageOf(json));

        var errors = json.GetProperty("errors");

        // Gắn lỗi vào cả hai ô ngày vì cả hai đều phải đổi mới hẹp lại được.
        Assert.True(errors.TryGetProperty("from", out _));
        Assert.True(errors.TryGetProperty("to", out _));
    }

    [Fact]
    public async Task Dung_bang_tran_thi_van_xuat_duoc()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        var start = DateTime.UtcNow.AddHours(-1);
        var logs = Enumerable.Range(0, AuditLogExportService.MaxRows)
            .Select(i => new AuditLog
            {
                CreatedAt = start.AddTicks(i),
                Action = AuditAction.Create,
                Target = $"Routes:{i}",
            })
            .ToList();

        await factory.SeedAsync(db => db.AuditLogs.AddRange(logs));

        var response = await client.GetAsync(ExportUrl);

        // Biên trên phải bao gồm chứ không loại trừ — lệch một dòng ở đây là lỗi khó thấy.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(XlsxContentType, response.Content.Headers.ContentType?.MediaType);

        using var workbook = await WorkbookAsync(response);
        Assert.Equal(HeaderRow + AuditLogExportService.MaxRows, workbook.Worksheet(1).LastRowUsed()!.RowNumber());
    }

    // ---------------------------------------------------------------------------------------
    // Nội dung file
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Noi_dung_file_dung_cot_thu_tu_va_kieu_du_lieu()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        var actor = await SeedUserAsync(factory, RoleIds.Admin, RoleCodes.Admin, fullName: "Trần Văn Kiểm");
        var createdAt = DateTime.UtcNow.AddHours(-2);

        await factory.SeedAsync(db => db.AuditLogs.Add(new AuditLog
        {
            CreatedAt = createdAt,
            Action = AuditAction.Update,
            UserId = actor.Id,
            Target = "Routes:3f2a1b0c-0000-0000-0000-000000000000",
            IpAddress = "203.0.113.5",
        }));

        using var workbook = await WorkbookAsync(await client.GetAsync(ExportUrl));
        var sheet = workbook.Worksheet(1);

        var headers = Enumerable.Range(1, ExpectedHeaders.Length)
            .Select(column => sheet.Cell(HeaderRow, column).GetString())
            .ToArray();
        Assert.Equal(ExpectedHeaders, headers);

        // Ô thời gian phải là DateTime THẬT, không phải chuỗi — gán chuỗi thì người kiểm toán
        // mất khả năng sắp xếp và lọc theo thời gian, tức mất nửa giá trị của bản xuất.
        var timeCell = sheet.Cell(HeaderRow + 1, 1);
        Assert.Equal(XLDataType.DateTime, timeCell.DataType);
        Assert.Equal(createdAt, timeCell.GetDateTime(), TimeSpan.FromSeconds(1));

        Assert.Equal("Trần Văn Kiểm", sheet.Cell(HeaderRow + 1, 2).GetString());
        Assert.Equal(actor.PhoneNumber, sheet.Cell(HeaderRow + 1, 3).GetString());
        Assert.Equal("Update", sheet.Cell(HeaderRow + 1, 4).GetString());
        Assert.Equal("Routes:3f2a1b0c-0000-0000-0000-000000000000", sheet.Cell(HeaderRow + 1, 5).GetString());
        Assert.Equal("203.0.113.5", sheet.Cell(HeaderRow + 1, 6).GetString());
        Assert.Equal(actor.Id.ToString(), sheet.Cell(HeaderRow + 1, 7).GetString());
    }

    [Fact]
    public async Task Dong_khong_xac_dinh_duoc_nguoi_thao_tac_van_xuat_duoc()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        // Đăng nhập thất bại với SĐT không tồn tại sinh ra đúng dòng như thế này: UserId NULL.
        // Mất IP hay mất tên không phải lý do để mất cả bản ghi nhật ký.
        await SeedLogAsync(
            factory,
            DateTime.UtcNow.AddHours(-1),
            AuditAction.LoginFailed,
            userId: null,
            target: null,
            ipAddress: null);

        using var workbook = await WorkbookAsync(await client.GetAsync(ExportUrl));
        var sheet = workbook.Worksheet(1);

        Assert.Equal("(không xác định)", sheet.Cell(HeaderRow + 1, 2).GetString());
        Assert.Equal("LoginFailed", sheet.Cell(HeaderRow + 1, 4).GetString());
        Assert.Equal(string.Empty, sheet.Cell(HeaderRow + 1, 5).GetString());
        Assert.Equal(string.Empty, sheet.Cell(HeaderRow + 1, 7).GetString());
    }

    [Fact]
    public async Task Ban_ghi_moi_nhat_nam_tren_cung()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        await SeedLogAsync(factory, DateTime.UtcNow.AddHours(-5), target: "Routes:cu-nhat");
        await SeedLogAsync(factory, DateTime.UtcNow.AddHours(-1), target: "Routes:moi-nhat");

        using var workbook = await WorkbookAsync(await client.GetAsync(ExportUrl));

        Assert.Equal(["Routes:moi-nhat", "Routes:cu-nhat"], TargetColumn(workbook));
    }

    [Fact]
    public async Task Xuat_nhat_ky_khong_sinh_them_ban_ghi_nhat_ky()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);

        await SeedLogAsync(factory, DateTime.UtcNow.AddHours(-1));
        var before = await CountLogsAsync(factory);

        var response = await client.GetAsync(ExportUrl);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // AuditLogMiddleware chỉ ghi POST/PUT/PATCH/DELETE nên GET này không để lại vết — nghĩa
        // là chính thao tác xuất không được ghi nhận. Khối tự mô tả ở hàng 2 của file là chỗ duy
        // nhất nói ai đã xuất lúc nào, và ca test này giữ cho hành vi đó không đổi ngoài ý muốn.
        Assert.Equal(before, await CountLogsAsync(factory));
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

    private static async Task SeedLogAsync(
        TestAppFactory factory,
        DateTime createdAtUtc,
        AuditAction action = AuditAction.Create,
        Guid? userId = null,
        string? target = "Routes:3f2a1b0c-0000-0000-0000-000000000000",
        string? ipAddress = "203.0.113.5")
    {
        await factory.SeedAsync(db => db.AuditLogs.Add(new AuditLog
        {
            CreatedAt = createdAtUtc,
            Action = action,
            UserId = userId,
            Target = target,
            IpAddress = ipAddress,
        }));
    }

    private static async Task<int> CountLogsAsync(TestAppFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.AuditLogs.CountAsync();
    }

    private static async Task<XLWorkbook> WorkbookAsync(HttpResponseMessage response)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync();

        return new XLWorkbook(new MemoryStream(bytes));
    }

    /// <summary>Cột "Đối tượng" của các dòng dữ liệu, theo đúng thứ tự trong file.</summary>
    private static string[] TargetColumn(XLWorkbook workbook)
    {
        var sheet = workbook.Worksheet(1);
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? HeaderRow;

        return Enumerable
            .Range(HeaderRow + 1, Math.Max(0, lastRow - HeaderRow))
            .Select(row => sheet.Cell(row, 5).GetString())
            .Where(value => value.Length > 0)
            .ToArray();
    }

    /// <summary>
    /// Đọc thân response lỗi thành JSON. Đọc được ĐÚNG MỘT LẦN cho mỗi response — stream của
    /// <c>HttpContent</c> không tua lại được, nên gọi thêm lần nữa sẽ ném
    /// <c>ObjectDisposedException: Cannot access a closed Stream</c>. Muốn hai khẳng định trên
    /// cùng một thân bài thì lấy <see cref="JsonElement"/> ra rồi khẳng định trên nó.
    /// </summary>
    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string MessageOf(JsonElement body)
        => body.GetProperty("message").GetString() ?? string.Empty;
}
