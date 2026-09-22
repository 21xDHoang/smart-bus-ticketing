using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Data;

/// <summary>
/// Phần DbContext của bảng nối Users ↔ Roles và seed 4 vai trò mặc định.
/// Vàng Thị Dăm sửa file này.
/// </summary>
public partial class AppDbContext
{
    public DbSet<UserRole> UserRoles => Set<UserRole>();

    partial void ConfigureUserRoles(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserRole>(e =>
        {
            // Khoá chính tổ hợp: một tài khoản chỉ giữ một vai trò tối đa một lần.
            e.HasKey(ur => new { ur.UserId, ur.RoleId });

            e.HasOne(ur => ur.User)
             .WithMany(u => u.UserRoles)
             .HasForeignKey(ur => ur.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(ur => ur.Role)
             .WithMany(r => r.UserRoles)
             .HasForeignKey(ur => ur.RoleId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        SeedRoles(modelBuilder);
    }

    /// <summary>
    /// Seed 4 vai trò mặc định kèm migration: chạy `dotnet ef database update`
    /// là CSDL đã có sẵn 4 vai trò, không cần script tay.
    /// </summary>
    private static void SeedRoles(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Role>().HasData(
            new Role { Id = RoleIds.Admin, Code = "Admin", Name = "Admin" },
            new Role { Id = RoleIds.Manager, Code = "Manager", Name = "Quản lý" },
            new Role { Id = RoleIds.Driver, Code = "Driver", Name = "Tài xế" },
            new Role { Id = RoleIds.Passenger, Code = "Passenger", Name = "Hành khách" });
    }
}
