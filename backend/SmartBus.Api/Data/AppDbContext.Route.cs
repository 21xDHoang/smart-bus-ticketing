using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Entities;

// Web SDK tự thêm implicit using Microsoft.AspNetCore.Routing, trong đó có lớp Route —
// trùng tên với entity Route của mình. Alias để mọi chỗ trong file này trỏ về entity.
// Ai viết Controller cho tuyến đường cũng sẽ gặp lại chuyện này.
using Route = SmartBus.Api.Entities.Route;

namespace SmartBus.Api.Data;

/// <summary>
/// Phần DbContext của nhóm nghiệp vụ Tuyến đường (Routes, Stops, RouteStops, Fares).
/// Vàng Thị Dăm sửa file này — chủ CSDL của nhóm.
/// </summary>
public partial class AppDbContext
{
    public DbSet<Route> Routes => Set<Route>();
    public DbSet<Stop> Stops => Set<Stop>();
    public DbSet<RouteStop> RouteStops => Set<RouteStop>();
    public DbSet<Fare> Fares => Set<Fare>();

    partial void ConfigureRoute(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Route>(e =>
        {
            e.Property(r => r.Code).HasMaxLength(20).IsRequired();
            e.Property(r => r.Name).HasMaxLength(200).IsRequired();
            e.Property(r => r.Origin).HasMaxLength(200).IsRequired();
            e.Property(r => r.Destination).HasMaxLength(200).IsRequired();

            // Quãng đường numeric(6,2) — tối đa 9999.99 km. Không dùng float/double (A3).
            e.Property(r => r.DistanceKm).HasPrecision(6, 2);

            // Trạng thái lưu dạng chuỗi, không phải số nguyên (A3).
            e.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

            // Mã tuyến là khoá nghiệp vụ: hai tuyến trùng mã là dữ liệu sai, chặn ở tầng CSDL.
            e.HasIndex(r => r.Code).IsUnique();
        });

        modelBuilder.Entity<Stop>(e =>
        {
            e.Property(s => s.Name).HasMaxLength(200).IsRequired();
            e.Property(s => s.Address).HasMaxLength(300).IsRequired();
        });

        modelBuilder.Entity<RouteStop>(e =>
        {
            // Khai báo tường minh, nếu không EF sinh ra numeric trần (không giới hạn
            // precision/scale) — cùng đơn vị km với Route.DistanceKm.
            e.Property(rs => rs.DistanceKm).HasPrecision(6, 2);

            // Một trạm không xuất hiện hai lần trên cùng một tuyến (A6).
            // Chỉ mục này cũng phục vụ luôn truy vấn "các trạm của tuyến" theo RouteId —
            // RouteId là cột đầu của chỉ mục nên không cần thêm chỉ mục riêng cho nó.
            e.HasIndex(rs => new { rs.RouteId, rs.StopId }).IsUnique();

            // Bảng nối thật sự thuộc cha — xoá tuyến thì các dòng RouteStops đi theo (A5).
            e.HasOne(rs => rs.Route)
             .WithMany(r => r.RouteStops)
             .HasForeignKey(rs => rs.RouteId)
             .OnDelete(DeleteBehavior.Cascade);

            // Chiều còn lại là FK nghiệp vụ: xoá một trạm đang nằm trên tuyến phải bị chặn,
            // không được âm thầm rút trạm khỏi mọi tuyến (A5).
            e.HasOne(rs => rs.Stop)
             .WithMany(s => s.RouteStops)
             .HasForeignKey(rs => rs.StopId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Fare>(e =>
        {
            e.Property(f => f.PassengerType).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(f => f.Price).HasPrecision(12, 2);

            // Mỗi tuyến + đối tượng chỉ có một giá (A6).
            e.HasIndex(f => new { f.RouteId, f.PassengerType }).IsUnique();

            // Không nằm trong danh sách Cascade của A5 nên theo mặc định: Restrict.
            e.HasOne(f => f.Route)
             .WithMany(r => r.Fares)
             .HasForeignKey(f => f.RouteId)
             .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
