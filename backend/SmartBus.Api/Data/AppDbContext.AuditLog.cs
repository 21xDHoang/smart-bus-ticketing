using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Data;

/// <summary>
/// Phần DbContext của nhóm Nhật ký hoạt động (AuditLogs).
/// Vàng Thị Dăm sửa file này.
/// </summary>
public partial class AppDbContext
{
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    partial void ConfigureAuditLog(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditLog>(e =>
        {
            // Loại hành động lưu dạng chuỗi, không phải số nguyên (A3).
            e.Property(a => a.Action).HasConversion<string>().HasMaxLength(20).IsRequired();

            // "<Tên bảng>:<Id>" — 200 ký tự thừa sức chứa tên bảng dài nhất
            // (DiscountRequests, MonthlyPasses) ghép một Guid.
            e.Property(a => a.Target).HasMaxLength(200);

            // 45 ký tự là độ dài tối đa của một địa chỉ IPv6 ở dạng đầy đủ.
            e.Property(a => a.IpAddress).HasMaxLength(45);

            // Chỉ mục theo quy ước A6: màn hình nhật ký lọc theo người dùng rồi sắp xếp
            // giảm dần theo thời gian, nên UserId đứng trước CreatedAt.
            e.HasIndex(a => new { a.UserId, a.CreatedAt });

            // Nhưng API truy vấn nhật ký còn lọc theo khoảng thời gian mà không kèm người dùng
            // (màn hình mặc định là "mọi người, mới nhất trước"). Chỉ mục (UserId, CreatedAt)
            // không dùng được cho truy vấn đó vì UserId là cột đầu, nên cần thêm chỉ mục riêng
            // cho CreatedAt. Bảng này chỉ ghi thêm nên chỉ mục sẽ không bị phân mảnh vì sửa.
            e.HasIndex(a => a.CreatedAt);

            // Nhật ký phải giữ được dấu vết người thực hiện: xoá một tài khoản còn nhật ký
            // là bị chặn, không được âm thầm xoá log hoặc để log mồ côi (A5).
            // Tài khoản trong hệ thống vốn không xoá cứng — khoá bằng Users.IsActive (A4).
            e.HasOne(a => a.User)
             .WithMany()
             .HasForeignKey(a => a.UserId)
             .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
