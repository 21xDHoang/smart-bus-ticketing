using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Data;

/// <summary>
/// Cấu hình giờ đến từng trạm — bảng chỉ giữ các mốc <b>ghi đè</b> của trạm trung gian.
/// Giờ dự kiến được suy ra khi đọc nên không có dòng cho mọi chuyến × mọi trạm.
/// </summary>
public partial class AppDbContext
{
    public DbSet<TripStopTimeOverride> TripStopTimeOverrides => Set<TripStopTimeOverride>();

    partial void ConfigureTripStopTime(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TripStopTimeOverride>(e =>
        {
            // Một chuyến chỉ có tối đa một mốc ghi đè cho mỗi trạm.
            e.HasIndex(o => new { o.TripId, o.StopId }).IsUnique();

            // FK StopId không nằm đầu index duy nhất nên cần index riêng cho
            // truy vấn "trạm này bị ghi đè ở chuyến nào" và cho kiểm tra FK khi xoá.
            e.HasIndex(o => o.StopId);

            e.HasOne(o => o.Trip)
             .WithMany()
             .HasForeignKey(o => o.TripId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(o => o.Stop)
             .WithMany()
             .HasForeignKey(o => o.StopId)
             .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
