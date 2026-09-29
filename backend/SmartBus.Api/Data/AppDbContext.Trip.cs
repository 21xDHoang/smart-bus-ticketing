using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Data;

/// <summary>
/// Phần DbContext của nhóm nghiệp vụ Vận hành chuyến xe (Buses, Seats, Trips).
/// Vàng Thị Dăm sửa file này — chủ CSDL của nhóm.
///
/// Story 13 "Lập lịch trình" phân rã task migrate là "Schedules, Trips, TripStops", nhưng
/// quy ước A8.3 chốt KHÔNG tách bảng Schedule (chỉ dùng Trips + API sinh chuyến hàng loạt)
/// và A9 không có bảng TripStops (thứ tự trạm lấy từ RouteStops). Vì vậy ở đây chỉ có Trips.
/// Buses + Seats được gộp vào cùng migration để Trips.BusId có khoá ngoại thật — A9 cũng xếp
/// Buses (10) và Seats (11) trước Trips (12).
/// </summary>
public partial class AppDbContext
{
    public DbSet<Bus> Buses => Set<Bus>();
    public DbSet<Seat> Seats => Set<Seat>();
    public DbSet<Trip> Trips => Set<Trip>();

    partial void ConfigureTrip(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Bus>(e =>
        {
            e.Property(b => b.LicensePlate).HasMaxLength(20).IsRequired();
            e.Property(b => b.BusType).HasMaxLength(50).IsRequired();

            // Trạng thái lưu dạng chuỗi, không phải số nguyên (A3).
            e.Property(b => b.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

            // Biển số là khoá nghiệp vụ — hai xe trùng biển số là dữ liệu sai.
            // A6 không liệt kê Buses, nhưng đây đúng cùng loại với Routes.Code (đã đặt unique
            // ở AppDbContext.Route.cs): mã định danh ngoài đời, không phải cột kỹ thuật.
            e.HasIndex(b => b.LicensePlate).IsUnique();
        });

        modelBuilder.Entity<Seat>(e =>
        {
            e.Property(s => s.SeatNumber).HasMaxLength(10).IsRequired();

            // Một xe không có hai ghế cùng số (A6).
            e.HasIndex(s => new { s.BusId, s.SeatNumber }).IsUnique();

            // Ghế thuộc về xe — xoá xe thì dàn ghế đi theo (A5).
            e.HasOne(s => s.Bus)
             .WithMany(b => b.Seats)
             .HasForeignKey(s => s.BusId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Trip>(e =>
        {
            e.Property(t => t.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

            // Tìm chuyến theo tuyến + giờ — chỉ mục A6 cho US 1 và US 13.
            // RouteId đứng đầu chỉ mục nên EF không sinh thêm chỉ mục riêng cho khoá ngoại này.
            e.HasIndex(t => new { t.RouteId, t.DepartureTime });

            // Tuyến bị xoá không được kéo theo chuyến (A5). Mặc định của EF là Cascade —
            // phải ghi tường minh, nếu không xoá một tuyến là mất sạch lịch sử chuyến.
            e.HasOne(t => t.Route)
             .WithMany(r => r.Trips)
             .HasForeignKey(t => t.RouteId)
             .OnDelete(DeleteBehavior.Restrict);

            // Cùng lý do: xe bị xoá không được kéo theo chuyến đã chạy (A5).
            e.HasOne(t => t.Bus)
             .WithMany(b => b.Trips)
             .HasForeignKey(t => t.BusId)
             .OnDelete(DeleteBehavior.Restrict);

            // CurrentStopId là con trỏ tới vị trí hiện tại (A8.6). Để Restrict theo mặc định
            // của A5: xoá một trạm đang là vị trí hiện tại của chuyến phải bị chặn, không được
            // để con trỏ trỏ vào trạm không còn tồn tại.
            e.HasOne(t => t.CurrentStop)
             .WithMany()
             .HasForeignKey(t => t.CurrentStopId)
             .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
