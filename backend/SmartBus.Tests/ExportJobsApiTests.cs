using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SmartBus.Api.Entities;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test tích hợp cho ba endpoint xuất báo cáo bất đồng bộ — task *"Xử lý xuất file lớn bất đồng bộ
/// + thông báo khi xong"* (Sprint 5 dòng 64 — Vàng Thị Dăm). Hợp đồng đầy đủ ở mục
/// "Xuất file bất đồng bộ — /exports" của docs/api-contract.md.
///
/// Những chỗ dễ hiểu sai được ghim ở đây:
/// 1. **202 chứ không phải 201**, kèm <c>Location</c> tới <c>GET /api/exports/{id}</c>.
/// 2. Trường không thuộc loại báo cáo đã chọn bị BỎ QUA, không trả 400.
/// 3. <c>audit-logs</c> là ranh giới Admin: Manager nhận 403 ngay ở bước đặt job.
/// 4. Job của người khác và job hết hạn đều 404 — không tiết lộ job nào có thật.
/// 5. Tải file khi job chưa xong (hoặc đã thất bại) là 409, không phải 404.
///
/// Job nền trong host test là bản noop (xem ThayJobNenBangBanNoop), nên trạng thái Pending đứng
/// yên; các ca cần trạng thái cuối seed thẳng vào <c>ExportJobs</c>.
/// </summary>
public class ExportJobsApiTests
{
    private const string ExportsUrl = "/api/exports";

    private const string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    // -------------------------------------------------------------- Quyền và xác thực

    [Fact]
    public async Task Khong_gui_token_thi_dat_job_tra_401()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            ExportsUrl,
            new { reportType = "revenue" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Khach_hang_dat_job_tra_403()
    {
        using var factory = new TestAppFactory();
        var passenger = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Hành Khách");
        var client = ClientWith(factory, factory.CreateTokenFor(passenger));

        var response = await client.PostAsJsonAsync(
            ExportsUrl,
            new { reportType = "revenue" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Manager_xuat_nhat_ky_kiem_toan_tra_403()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var response = await client.PostAsJsonAsync(
            ExportsUrl,
            new { reportType = "audit-logs" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var body = await ReadJsonAsync(response);
        Assert.Equal(
            "Chỉ quản trị viên được xuất nhật ký kiểm toán.",
            body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Admin_xuat_nhat_ky_kiem_toan_duoc_chap_nhan()
    {
        using var factory = new TestAppFactory();
        var admin = await SeedUserAsync(factory, RoleIds.Admin, RoleCodes.Admin, "Quản Trị Viên");
        var client = ClientWith(factory, factory.CreateTokenFor(admin));

        var response = await client.PostAsJsonAsync(
            ExportsUrl,
            new { reportType = "audit-logs" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var body = await ReadJsonAsync(response);
        Assert.Equal("audit-logs", body.GetProperty("reportType").GetString());
    }

    // -------------------------------------------------------------- Đặt job

    [Fact]
    public async Task Dat_job_hop_le_tra_202_kem_location_va_trang_thai_pending()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var response = await client.PostAsJsonAsync(
            ExportsUrl,
            new { reportType = "revenue", granularity = "week", format = "pdf" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var body = await ReadJsonAsync(response);
        var id = body.GetProperty("id").GetString();

        Assert.False(string.IsNullOrWhiteSpace(id));
        Assert.Equal("revenue", body.GetProperty("reportType").GetString());
        Assert.Equal("pdf", body.GetProperty("format").GetString());
        Assert.Equal("Pending", body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("downloadUrl").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("startedAt").ValueKind);

        Assert.EndsWith(
            $"/api/exports/{id}",
            response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Truong_khong_thuoc_loai_bao_cao_bi_bo_qua()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        // timeSlot là của báo cáo lấp đầy; báo cáo doanh thu phải bỏ qua nó thay vì trả 400.
        var response = await client.PostAsJsonAsync(
            ExportsUrl,
            new { reportType = "revenue", timeSlot = "khong-hop-le", userId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task Report_type_la_tra_400_o_dung_truong()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var response = await client.PostAsJsonAsync(ExportsUrl, new { reportType = "sales" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var errors = (await ReadJsonAsync(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("reportType", out _));
    }

    [Fact]
    public async Task Format_la_tra_400_o_dung_truong()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var response = await client.PostAsJsonAsync(
            ExportsUrl,
            new { reportType = "revenue", format = "csv" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var errors = (await ReadJsonAsync(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("format", out _));
    }

    [Fact]
    public async Task Granularity_la_tra_400_o_dung_truong()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var response = await client.PostAsJsonAsync(
            ExportsUrl,
            new { reportType = "revenue", granularity = "year" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var errors = (await ReadJsonAsync(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("granularity", out _));
    }

    [Fact]
    public async Task Time_slot_la_tra_400_o_dung_truong()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var response = await client.PostAsJsonAsync(
            ExportsUrl,
            new { reportType = "fill-rate", timeSlot = "noon" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var errors = (await ReadJsonAsync(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("timeSlot", out _));
    }

    [Fact]
    public async Task To_truoc_from_tra_400_gan_loi_vao_ca_hai_dau()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var response = await client.PostAsJsonAsync(
            ExportsUrl,
            new { reportType = "revenue", from = "2026-10-10", to = "2026-10-01" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var errors = (await ReadJsonAsync(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("from", out _));
        Assert.True(errors.TryGetProperty("to", out _));
    }

    [Fact]
    public async Task Doanh_thu_qua_366_ngay_tra_400()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var response = await client.PostAsJsonAsync(
            ExportsUrl,
            new { reportType = "revenue", from = "2024-01-01", to = "2026-01-01" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var errors = (await ReadJsonAsync(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("from", out _));
        Assert.True(errors.TryGetProperty("to", out _));
    }

    [Fact]
    public async Task Nhat_ky_khong_bi_gioi_han_366_ngay()
    {
        using var factory = new TestAppFactory();
        var admin = await SeedUserAsync(factory, RoleIds.Admin, RoleCodes.Admin, "Quản Trị Viên");
        var client = ClientWith(factory, factory.CreateTokenFor(admin));

        var response = await client.PostAsJsonAsync(
            ExportsUrl,
            new { reportType = "audit-logs", from = "2024-01-01", to = "2026-01-01" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task Tuyen_khong_ton_tai_tra_404_ngay_khi_dat_job()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var response = await client.PostAsJsonAsync(
            ExportsUrl,
            new { reportType = "fill-rate", routeId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Da_co_ba_job_chua_xong_thi_tra_409()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        await factory.SeedAsync(db =>
        {
            for (var i = 0; i < 3; i++)
            {
                db.ExportJobs.Add(Job(manager.Id, ExportJobStatus.Pending));
            }
        });

        var response = await client.PostAsJsonAsync(
            ExportsUrl,
            new { reportType = "revenue" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var message = (await ReadJsonAsync(response)).GetProperty("message").GetString();
        Assert.Contains("3 yêu cầu xuất file chưa xong", message);
    }

    [Fact]
    public async Task Job_da_xong_khong_tinh_vao_tran_ba_job()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        await factory.SeedAsync(db =>
        {
            db.ExportJobs.Add(Job(manager.Id, ExportJobStatus.Succeeded));
            db.ExportJobs.Add(Job(manager.Id, ExportJobStatus.Succeeded));
            db.ExportJobs.Add(Job(manager.Id, ExportJobStatus.Failed));
        });

        var response = await client.PostAsJsonAsync(
            ExportsUrl,
            new { reportType = "revenue" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    // -------------------------------------------------------------- Đọc trạng thái

    [Fact]
    public async Task Doc_trang_thai_job_cua_minh_tra_du_truong()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var client = ClientWith(factory, factory.CreateTokenFor(manager));

        var job = Job(manager.Id, ExportJobStatus.Succeeded);
        job.FileName = "bao-cao-doanh-thu_20261017-000000Z.xlsx";
        job.ContentType = XlsxContentType;
        job.FileSizeBytes = 123;
        job.CompletedAt = DateTime.UtcNow;
        job.ExpiresAt = DateTime.UtcNow.AddHours(24);

        await factory.SeedAsync(db => db.ExportJobs.Add(job));

        var response = await client.GetAsync($"{ExportsUrl}/{job.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);
        Assert.Equal(job.Id, body.GetProperty("id").GetGuid());
        Assert.Equal("Succeeded", body.GetProperty("status").GetString());
        Assert.Equal(
            $"/api/exports/{job.Id}/download",
            body.GetProperty("downloadUrl").GetString());
    }

    [Fact]
    public async Task Job_cua_nguoi_khac_tra_404()
    {
        using var factory = new TestAppFactory();
        var owner = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Chủ Job");
        var other = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Người Khác");

        var job = Job(owner.Id, ExportJobStatus.Succeeded);
        await factory.SeedAsync(db => db.ExportJobs.Add(job));

        var client = ClientWith(factory, factory.CreateTokenFor(other));
        var response = await client.GetAsync($"{ExportsUrl}/{job.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Job_het_han_tra_404_du_chua_bi_don()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");

        var job = Job(manager.Id, ExportJobStatus.Succeeded);
        job.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await factory.SeedAsync(db => db.ExportJobs.Add(job));

        var client = ClientWith(factory, factory.CreateTokenFor(manager));
        var response = await client.GetAsync($"{ExportsUrl}/{job.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // -------------------------------------------------------------- Tải file

    [Fact]
    public async Task Tai_file_khi_job_chua_xong_tra_409()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");

        var job = Job(manager.Id, ExportJobStatus.Pending);
        await factory.SeedAsync(db => db.ExportJobs.Add(job));

        var client = ClientWith(factory, factory.CreateTokenFor(manager));
        var response = await client.GetAsync($"{ExportsUrl}/{job.Id}/download");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var message = (await ReadJsonAsync(response)).GetProperty("message").GetString();
        Assert.Contains("chưa sẵn sàng", message);
    }

    [Fact]
    public async Task Tai_file_cua_job_that_bai_tra_409_voi_cau_khac()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");

        var job = Job(manager.Id, ExportJobStatus.Failed);
        await factory.SeedAsync(db => db.ExportJobs.Add(job));

        var client = ClientWith(factory, factory.CreateTokenFor(manager));
        var response = await client.GetAsync($"{ExportsUrl}/{job.Id}/download");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var message = (await ReadJsonAsync(response)).GetProperty("message").GetString();
        Assert.Contains("đã thất bại", message);
    }

    [Fact]
    public async Task Tai_file_job_thanh_cong_tra_file_va_ten_file()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");

        var content = Encoding.UTF8.GetBytes("noi-dung-file-bao-cao");
        var job = Job(manager.Id, ExportJobStatus.Succeeded);
        job.FileName = "bao-cao-doanh-thu_20261017-000000Z.xlsx";
        job.ContentType = XlsxContentType;
        job.Content = content;
        job.FileSizeBytes = content.Length;
        job.CompletedAt = DateTime.UtcNow;
        job.ExpiresAt = DateTime.UtcNow.AddHours(24);

        await factory.SeedAsync(db => db.ExportJobs.Add(job));

        var client = ClientWith(factory, factory.CreateTokenFor(manager));
        var response = await client.GetAsync($"{ExportsUrl}/{job.Id}/download");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(XlsxContentType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(content, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(
            "bao-cao-doanh-thu_20261017-000000Z.xlsx",
            response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
    }

    [Fact]
    public async Task Tai_file_job_het_han_tra_404()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");

        var job = Job(manager.Id, ExportJobStatus.Succeeded);
        job.Content = Encoding.UTF8.GetBytes("qua-cu");
        job.FileName = "cu.xlsx";
        job.ContentType = XlsxContentType;
        job.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);

        await factory.SeedAsync(db => db.ExportJobs.Add(job));

        var client = ClientWith(factory, factory.CreateTokenFor(manager));
        var response = await client.GetAsync($"{ExportsUrl}/{job.Id}/download");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // =======================================================================================
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // =======================================================================================

    private static HttpClient ClientWith(TestAppFactory factory, string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();

        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static ExportJob Job(Guid userId, ExportJobStatus status) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        RequestedByName = "Người Đặt Job",
        ReportType = ExportReportType.Revenue,
        Format = "xlsx",
        Status = status,
        ParametersJson = "{}",
        CreatedAt = DateTime.UtcNow,
    };

    private static async Task<UserEntity> SeedUserAsync(
        TestAppFactory factory,
        Guid roleId,
        string roleCode,
        string fullName)
    {
        var role = new Role { Id = roleId, Code = roleCode, Name = roleCode };

        await factory.SeedAsync(db =>
        {
            if (!db.Roles.Any(r => r.Id == role.Id || r.Code == role.Code))
            {
                db.Roles.Add(role);
            }
        });

        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "09" + Random.Shared.Next(10_000_000, 99_999_999),
            FullName = fullName,
            PasswordHash = "hash-khong-dung-toi-trong-test",
            IsActive = true,
            RoleId = role.Id,
            UserRoles = [new UserRole { RoleId = role.Id }],
        };

        await factory.SeedAsync(db => db.Users.Add(user));

        return user;
    }
}
