using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Exports;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test phần thu hồi / dọn dẹp và phần chụp tham số của <see cref="IExportJobService"/> — task
/// *"Xử lý xuất file lớn bất đồng bộ + thông báo khi xong"* (Sprint 5 dòng 64 — Vàng Thị Dăm).
///
/// Ba việc bảo trì là chỗ dễ làm hỏng dữ liệu nhất của thiết kế job:
/// 1. Thu hồi chỉ chạm job <c>Processing</c> đã quá hạn và KHÔNG tăng <c>AttemptCount</c> — tăng ở
///    đây là đếm hai lần cho một lần chạy hỏng, làm job chết sớm hơn 3 lần cho phép.
/// 2. Dọn dẹp chỉ xoá job quá <c>ExpiresAt</c>; job đang chờ (chưa có hạn) phải sống.
/// 3. Ảnh chụp tham số chỉ giữ trường thuộc loại báo cáo — trường thừa của form dùng chung không
///    được lọt vào file/CSDL.
/// </summary>
public class ExportJobServiceTests
{
    [Fact]
    public async Task Thu_hoi_chi_cham_job_processing_qua_han_va_khong_tang_so_lan_thu()
    {
        using var factory = new TestAppFactory();
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var stale = Job(userId, ExportJobStatus.Processing, startedAt: now.AddMinutes(-11));
        stale.AttemptCount = 2;

        var fresh = Job(userId, ExportJobStatus.Processing, startedAt: now.AddMinutes(-1));
        fresh.AttemptCount = 1;

        var pending = Job(userId, ExportJobStatus.Pending);
        pending.AttemptCount = 1;

        var succeeded = Job(userId, ExportJobStatus.Succeeded);
        succeeded.StartedAt = now.AddHours(-2);
        succeeded.CompletedAt = now.AddHours(-2);
        succeeded.ExpiresAt = now.AddHours(22);

        await factory.SeedAsync(db => db.ExportJobs.AddRange(stale, fresh, pending, succeeded));

        var affected = await RecoverAsync(factory, now, TimeSpan.FromMinutes(10));

        Assert.Equal(1, affected);

        var savedStale = await ReloadAsync(factory, stale.Id);
        Assert.Equal(ExportJobStatus.Pending, savedStale.Status);
        Assert.Null(savedStale.StartedAt);
        Assert.Equal(2, savedStale.AttemptCount);
        Assert.NotNull(savedStale.UpdatedAt);

        var savedFresh = await ReloadAsync(factory, fresh.Id);
        Assert.Equal(ExportJobStatus.Processing, savedFresh.Status);
        Assert.NotNull(savedFresh.StartedAt);

        Assert.Equal(
            ExportJobStatus.Succeeded,
            (await ReloadAsync(factory, succeeded.Id)).Status);
    }

    [Fact]
    public async Task Thu_hoi_khong_cham_job_processing_thieu_started_at()
    {
        using var factory = new TestAppFactory();
        var now = DateTime.UtcNow;

        var noStartedAt = Job(Guid.NewGuid(), ExportJobStatus.Processing);
        await factory.SeedAsync(db => db.ExportJobs.Add(noStartedAt));

        Assert.Equal(0, await RecoverAsync(factory, now, TimeSpan.FromMinutes(10)));
        Assert.Equal(
            ExportJobStatus.Processing,
            (await ReloadAsync(factory, noStartedAt.Id)).Status);
    }

    [Fact]
    public async Task Don_dep_chi_xoa_job_qua_han()
    {
        // CleanupExpiredAsync dùng ExecuteDeleteAsync — sinh câu DELETE ở CSDL để không nạp cột
        // Content (có thể vài chục MB) vào bộ nhớ. InMemory của TestAppFactory không hỗ trợ lệnh
        // này, nên ca này chạy trên SQLite in-memory để kiểm đúng câu xoá mà PostgreSQL sẽ chạy.
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        // ExportJobs.UserId là FK Restrict sang Users — SQLite cưỡng chế FK thật (InMemory thì
        // không), nên phải có tài khoản trước khi gieo job. Vai trò có sẵn từ HasData của model,
        // không thêm lại (Roles.Code là unique).
        var roleId = await db.Roles
            .Where(r => r.Code == RoleCodes.Manager)
            .Select(r => r.Id)
            .SingleAsync();

        var owner = new User
        {
            Id = userId,
            PhoneNumber = "0912345678",
            FullName = "Người Đặt Job",
            PasswordHash = "hash-khong-dung-toi-trong-test",
            IsActive = true,
            RoleId = roleId,
        };

        db.Users.Add(owner);

        var expiredSucceeded = Job(userId, ExportJobStatus.Succeeded);
        expiredSucceeded.ExpiresAt = now.AddMinutes(-1);

        var expiredFailed = Job(userId, ExportJobStatus.Failed);
        expiredFailed.ExpiresAt = now.AddHours(-25);

        var stillAlive = Job(userId, ExportJobStatus.Succeeded);
        stillAlive.ExpiresAt = now.AddHours(1);

        var pending = Job(userId, ExportJobStatus.Pending);

        db.ExportJobs.AddRange(expiredSucceeded, expiredFailed, stillAlive, pending);
        await db.SaveChangesAsync();

        var service = new ExportJobService(db);
        var deleted = await service.CleanupExpiredAsync(now, CancellationToken.None);

        Assert.Equal(2, deleted);

        var remaining = await db.ExportJobs.AsNoTracking().Select(j => j.Id).ToListAsync();

        Assert.Equal(2, remaining.Count);
        Assert.Contains(stillAlive.Id, remaining);
        Assert.Contains(pending.Id, remaining);
    }

    // -------------------------------------------------------------- Ảnh chụp tham số

    [Fact]
    public async Task Doanh_thu_chi_luu_granularity_ngay_va_tuyen()
    {
        using var factory = new TestAppFactory();
        var userId = Guid.NewGuid();

        var response = await EnqueueAsync(factory, userId, new CreateExportJobRequest
        {
            ReportType = "revenue",
            Format = "pdf",
            Granularity = "week",
            From = new DateOnly(2026, 10, 1),
            To = new DateOnly(2026, 10, 14),
            TimeSlot = "morning",
            UserId = Guid.NewGuid(),
            Action = "Update",
        });

        var job = await ReloadAsync(factory, response.Id);

        Assert.Equal(ExportReportType.Revenue, job.ReportType);
        Assert.Equal("pdf", job.Format);

        using var parameters = JsonDocument.Parse(job.ParametersJson);
        var root = parameters.RootElement;

        Assert.Equal("week", root.GetProperty("granularity").GetString());
        Assert.Equal("2026-10-01", root.GetProperty("from").GetString());
        Assert.Equal("2026-10-14", root.GetProperty("to").GetString());
        Assert.False(root.TryGetProperty("timeSlot", out _));
        Assert.False(root.TryGetProperty("auditUserId", out _));
        Assert.False(root.TryGetProperty("auditAction", out _));
    }

    [Fact]
    public async Task Lap_day_chi_luu_khung_gio_ngay_va_tuyen()
    {
        using var factory = new TestAppFactory();
        var userId = Guid.NewGuid();

        var response = await EnqueueAsync(factory, userId, new CreateExportJobRequest
        {
            ReportType = "fill-rate",
            Granularity = "month",
            TimeSlot = "night",
            From = new DateOnly(2026, 10, 1),
            To = new DateOnly(2026, 10, 14),
        });

        var job = await ReloadAsync(factory, response.Id);

        using var parameters = JsonDocument.Parse(job.ParametersJson);
        var root = parameters.RootElement;

        Assert.Equal("night", root.GetProperty("timeSlot").GetString());
        Assert.False(root.TryGetProperty("granularity", out _));
    }

    [Fact]
    public async Task Nhat_ky_chi_luu_nguoi_thao_tac_va_hanh_dong()
    {
        using var factory = new TestAppFactory();
        var userId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        var response = await EnqueueAsync(factory, userId, new CreateExportJobRequest
        {
            ReportType = "audit-logs",
            From = new DateOnly(2026, 10, 1),
            To = new DateOnly(2026, 10, 14),
            UserId = actorId,
            Action = "LoginFailed",
            Granularity = "day",
            TimeSlot = "early",
        });

        var job = await ReloadAsync(factory, response.Id);

        using var parameters = JsonDocument.Parse(job.ParametersJson);
        var root = parameters.RootElement;

        Assert.Equal(actorId, root.GetProperty("auditUserId").GetGuid());
        Assert.Equal("LoginFailed", root.GetProperty("auditAction").GetString());
        Assert.False(root.TryGetProperty("granularity", out _));
        Assert.False(root.TryGetProperty("timeSlot", out _));
    }

    // =======================================================================================
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // =======================================================================================

    private static async Task<int> RecoverAsync(
        TestAppFactory factory,
        DateTime utcNow,
        TimeSpan staleAfter)
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IExportJobService>();

        return await service.RecoverStaleAsync(utcNow, staleAfter, CancellationToken.None);
    }

    private static async Task<ExportJobResponse> EnqueueAsync(
        TestAppFactory factory,
        Guid userId,
        CreateExportJobRequest request)
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IExportJobService>();

        var result = await service.EnqueueAsync(
            request,
            userId,
            "Người Đặt Job",
            CancellationToken.None);

        Assert.True(result.Success, result.Error);

        return result.Data!;
    }

    private static async Task<ExportJob> ReloadAsync(TestAppFactory factory, Guid jobId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.ExportJobs.AsNoTracking().SingleAsync(j => j.Id == jobId);
    }

    private static ExportJob Job(
        Guid userId,
        ExportJobStatus status,
        DateTime? startedAt = null) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        RequestedByName = "Người Đặt Job",
        ReportType = ExportReportType.Revenue,
        Format = "xlsx",
        Status = status,
        ParametersJson = "{}",
        CreatedAt = DateTime.UtcNow.AddHours(-1),
        StartedAt = startedAt,
    };
}
