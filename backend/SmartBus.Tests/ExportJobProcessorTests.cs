using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;
using RouteEntity = SmartBus.Api.Entities.Route;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test ruột của job nền xuất báo cáo — task *"Xử lý xuất file lớn bất đồng bộ + thông báo khi xong"*
/// (Sprint 5 dòng 64 — Vàng Thị Dăm).
///
/// Gọi thẳng <see cref="IExportJobProcessor.ProcessNextAsync"/> trong một scope của host test thay vì
/// chờ <c>ExportJobBackgroundService</c> (host test dùng bản noop): nhờ vậy test kiểm đúng phần nghiệp
/// vụ — nhận job, dựng file, chốt trạng thái, ghi thông báo — mà không phụ thuộc nhịp 2 giây.
///
/// Năm điều kiện được ghim:
/// 1. Job xong: file nằm trong cột <c>Content</c>, trạng thái <c>Succeeded</c>, hạn 24 giờ.
/// 2. Thông báo <c>ExportCompleted</c> ghi đúng chủ job, đúng <c>RelatedEntityId</c>.
/// 3. Job hỏng vì dữ liệu cấu hình (định dạng lạ) chốt <c>Failed</c> + <c>ExportFailed</c>.
/// 4. Job đã thử đủ 3 lần không bị nhận lần nữa — chốt <c>Failed</c> ngay.
/// 5. Thông báo chỉ ghi MỘT lần dù job bị đưa lại <c>Pending</c> và chốt kết quả lần hai.
/// </summary>
public class ExportJobProcessorTests
{
    private const string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    // -------------------------------------------------------------- Báo cáo lấp đầy

    [Fact]
    public async Task Job_lap_day_xong_luu_file_va_bao_cho_chu_job()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var route = await SeedRouteAsync(factory, "19");
        var bus = await SeedBusAsync(factory, 40, "51B-123.45");
        await SeedTripAsync(factory, route.Id, bus.Id, GioVietNam(14, 8, 0));

        var job = NewJob(
            manager.Id,
            ExportReportType.FillRate,
            """{"from":"2026-10-14","to":"2026-10-14"}""");
        await factory.SeedAsync(db => db.ExportJobs.Add(job));

        Assert.True(await ProcessAsync(factory));

        var saved = await ReloadAsync(factory, job.Id);

        Assert.Equal(ExportJobStatus.Succeeded, saved.Status);
        Assert.NotNull(saved.Content);
        Assert.NotEmpty(saved.Content!);
        Assert.Equal(XlsxContentType, saved.ContentType);
        Assert.Equal(saved.Content!.LongLength, saved.FileSizeBytes);
        Assert.Equal(1, saved.RowCount);
        Assert.Equal(1, saved.AttemptCount);
        Assert.NotNull(saved.StartedAt);
        Assert.NotNull(saved.CompletedAt);
        Assert.Equal(saved.CompletedAt!.Value.AddHours(24), saved.ExpiresAt);
        Assert.Null(saved.ErrorMessage);
        Assert.Matches(@"^bao-cao-ty-le-lap-day_\d{8}-\d{6}Z\.xlsx$", saved.FileName);

        // Ghim magic ZIP — file xlsx thật, không phải chuỗi giả.
        Assert.Equal(
            new byte[] { 0x50, 0x4B, 0x03, 0x04 },
            saved.Content.Take(4).ToArray());

        var notifications = await NotificationsAsync(factory, manager.Id, job.Id);
        var notification = Assert.Single(notifications);

        Assert.Equal(NotificationType.ExportCompleted, notification.Type);
        Assert.Equal(NotificationTarget.ExportJob, notification.RelatedEntityType!.Value);
        Assert.Equal("Báo cáo đã sẵn sàng", notification.Title);
        Assert.Contains("1 dòng", notification.Body);
        Assert.Contains("hết hạn", notification.Body);
    }

    [Fact]
    public async Task Job_doanh_thu_xong_nhung_row_count_de_trong()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");

        var job = NewJob(
            manager.Id,
            ExportReportType.Revenue,
            """{"granularity":"day","from":"2026-10-14","to":"2026-10-14"}""");
        await factory.SeedAsync(db => db.ExportJobs.Add(job));

        Assert.True(await ProcessAsync(factory));

        var saved = await ReloadAsync(factory, job.Id);

        Assert.Equal(ExportJobStatus.Succeeded, saved.Status);
        Assert.NotNull(saved.Content);
        Assert.Null(saved.RowCount);
        Assert.Matches(@"^bao-cao-doanh-thu_\d{8}-\d{6}Z\.xlsx$", saved.FileName);

        var notification = Assert.Single(await NotificationsAsync(factory, manager.Id, job.Id));
        Assert.Equal(NotificationType.ExportCompleted, notification.Type);
        Assert.DoesNotContain(" dòng", notification.Body);
    }

    [Fact]
    public async Task Job_nhat_ky_kiem_toan_xong_voi_dung_so_dong()
    {
        using var factory = new TestAppFactory();
        var admin = await SeedUserAsync(factory, RoleIds.Admin, RoleCodes.Admin, "Quản Trị Viên");

        await factory.SeedAsync(db =>
        {
            db.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                UserId = admin.Id,
                Action = AuditAction.Update,
                Target = $"Routes:{Guid.NewGuid()}",
                IpAddress = "203.0.113.5",
                CreatedAt = new DateTime(2026, 10, 14, 3, 0, 0, DateTimeKind.Utc),
            });

            db.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                UserId = null,
                Action = AuditAction.LoginFailed,
                Target = null,
                IpAddress = null,
                CreatedAt = new DateTime(2026, 10, 15, 3, 0, 0, DateTimeKind.Utc),
            });
        });

        var job = NewJob(
            admin.Id,
            ExportReportType.AuditLogs,
            """{"from":"2026-10-14","to":"2026-10-14"}""");
        await factory.SeedAsync(db => db.ExportJobs.Add(job));

        Assert.True(await ProcessAsync(factory));

        var saved = await ReloadAsync(factory, job.Id);

        Assert.Equal(ExportJobStatus.Succeeded, saved.Status);
        Assert.Equal(1, saved.RowCount);
        Assert.Matches(@"^nhat-ky-kiem-toan_\d{8}-\d{6}Z\.xlsx$", saved.FileName);

        var notification = Assert.Single(await NotificationsAsync(factory, admin.Id, job.Id));
        Assert.Contains("1 dòng", notification.Body);
    }

    // -------------------------------------------------------------- Nhánh thất bại

    [Fact]
    public async Task Dinh_dang_la_chot_failed_va_bao_loi()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");

        var job = NewJob(manager.Id, ExportReportType.Revenue, "{}");
        job.Format = "docx";
        await factory.SeedAsync(db => db.ExportJobs.Add(job));

        Assert.True(await ProcessAsync(factory));

        var saved = await ReloadAsync(factory, job.Id);

        Assert.Equal(ExportJobStatus.Failed, saved.Status);
        Assert.Equal("Định dạng file của yêu cầu không hợp lệ.", saved.ErrorMessage);
        Assert.Null(saved.Content);
        Assert.Null(saved.FileName);
        Assert.Equal(1, saved.AttemptCount);
        Assert.NotNull(saved.ExpiresAt);

        var notification = Assert.Single(await NotificationsAsync(factory, manager.Id, job.Id));
        Assert.Equal(NotificationType.ExportFailed, notification.Type);
        Assert.Equal("Xuất báo cáo thất bại", notification.Title);
        Assert.Contains("Định dạng file của yêu cầu không hợp lệ.", notification.Body);
    }

    [Fact]
    public async Task Job_da_thu_du_ba_lan_chot_failed_ma_khong_dung_file()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");

        var job = NewJob(manager.Id, ExportReportType.Revenue, "{}");
        job.AttemptCount = ExportJobProcessor.MaxAttempts;
        await factory.SeedAsync(db => db.ExportJobs.Add(job));

        Assert.True(await ProcessAsync(factory));

        var saved = await ReloadAsync(factory, job.Id);

        Assert.Equal(ExportJobStatus.Failed, saved.Status);
        Assert.Equal(ExportJobProcessor.MaxAttempts, saved.AttemptCount);
        Assert.Contains("3 lần", saved.ErrorMessage);

        var notification = Assert.Single(await NotificationsAsync(factory, manager.Id, job.Id));
        Assert.Equal(NotificationType.ExportFailed, notification.Type);
    }

    [Fact]
    public async Task Job_chot_lai_lan_hai_khong_ghi_trung_thong_bao()
    {
        using var factory = new TestAppFactory();
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");

        var job = NewJob(manager.Id, ExportReportType.Revenue, "{}");
        job.Format = "docx";
        await factory.SeedAsync(db => db.ExportJobs.Add(job));

        Assert.True(await ProcessAsync(factory));

        var saved = await ReloadAsync(factory, job.Id);
        Assert.Equal(ExportJobStatus.Failed, saved.Status);

        // Lượt hai không nhận job đã Failed, nên để mô phỏng "app tắt rồi thu hồi" ta đưa job về
        // Pending đúng như RecoverStaleAsync làm, rồi chốt kết quả lần nữa.
        await factory.SeedAsync(db =>
        {
            var tracked = db.ExportJobs.Single(j => j.Id == job.Id);
            tracked.Status = ExportJobStatus.Pending;
            tracked.StartedAt = null;
        });

        Assert.True(await ProcessAsync(factory));

        var notifications = await NotificationsAsync(factory, manager.Id, job.Id);
        Assert.Single(notifications);
    }

    [Fact]
    public async Task Hang_doi_rong_thi_tra_false()
    {
        using var factory = new TestAppFactory();

        Assert.False(await ProcessAsync(factory));
    }

    // =======================================================================================
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // =======================================================================================

    private static async Task<bool> ProcessAsync(TestAppFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<IExportJobProcessor>();

        return await processor.ProcessNextAsync(CancellationToken.None);
    }

    private static async Task<ExportJob> ReloadAsync(TestAppFactory factory, Guid jobId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.ExportJobs.AsNoTracking().SingleAsync(j => j.Id == jobId);
    }

    private static async Task<List<Notification>> NotificationsAsync(
        TestAppFactory factory,
        Guid userId,
        Guid jobId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId && n.RelatedEntityId == jobId)
            .ToListAsync();
    }

    private static ExportJob NewJob(
        Guid userId,
        ExportReportType reportType,
        string parametersJson) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        RequestedByName = "Người Đặt Job",
        ReportType = reportType,
        Format = "xlsx",
        Status = ExportJobStatus.Pending,
        ParametersJson = parametersJson,
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

    private static async Task<Bus> SeedBusAsync(TestAppFactory factory, int capacity, string licensePlate)
    {
        var bus = new Bus
        {
            Id = Guid.NewGuid(),
            LicensePlate = licensePlate,
            BusType = "Xe buýt test",
            Capacity = capacity,
        };

        await factory.SeedAsync(db => db.Buses.Add(bus));

        return bus;
    }

    private static async Task SeedTripAsync(
        TestAppFactory factory,
        Guid routeId,
        Guid busId,
        DateTime departureUtc)
    {
        await factory.SeedAsync(db => db.Trips.Add(new Trip
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            BusId = busId,
            DepartureTime = departureUtc,
            Status = TripStatus.Scheduled,
        }));
    }

    /// <summary>Mốc giờ Việt Nam (UTC+7) trên ngày 2026-10-<paramref name="day"/> → mốc UTC tương ứng.</summary>
    private static DateTime GioVietNam(int day, int hour, int minute)
        => new DateTime(2026, 10, day, hour, minute, 0, DateTimeKind.Utc).AddHours(-7);
}
