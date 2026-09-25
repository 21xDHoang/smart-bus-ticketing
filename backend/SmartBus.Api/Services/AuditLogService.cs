using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Ghi nhật ký hoạt động — task story 23, Vàng Thị Dăm.
/// Xem <see cref="IAuditLogService"/> để biết vì sao hàm này không bao giờ ném lỗi.
/// </summary>
public sealed class AuditLogService : IAuditLogService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(IServiceScopeFactory scopeFactory, ILogger<AuditLogService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task RecordAsync(AuditAction action, Guid? userId, string? target, string? ipAddress)
    {
        try
        {
            // Cố ý mở scope riêng thay vì dùng AppDbContext của request. SaveChanges ghi kèm MỌI
            // thay đổi đang chờ sẵn trong DbContext dùng chung, nên chỉ cần một service quên
            // SaveChanges là thao tác dở dang của nó bị commit dưới danh nghĩa ghi nhật ký —
            // và bảng nhật ký là chỗ khó lần ra nhất. Scope riêng thì việc ghi nhật ký
            // không thể chạm vào dữ liệu nghiệp vụ.
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.AuditLogs.Add(new AuditLog
            {
                Action = action,
                UserId = userId,
                Target = target,
                IpAddress = ipAddress,
            });

            await db.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            // Nuốt lỗi có chủ ý — xem ghi chú ở IAuditLogService.RecordAsync.
            // Ghi lại đủ chi tiết để còn dựng lại được bản ghi đã mất.
            _logger.LogError(
                exception,
                "Không ghi được nhật ký hoạt động: hành động {Action}, người dùng {UserId}, đối tượng {Target}.",
                action,
                userId,
                target);
        }
    }
}
