using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Data;

/// <summary>
/// Phần DbContext của nhóm nghiệp vụ Vận hành chuyến xe (Buses, Trips).
/// Vàng Thị Dăm sửa file này — chủ CSDL của nhóm.
///
/// Story 13 "Lập lịch trình" phân rã task migrate là "Schedules, Trips, TripStops", nhưng
/// quy ước A8.3 chốt KHÔNG tách bảng Schedule (chỉ dùng Trips + API sinh chuyến hàng loạt)
/// và A9 không có bảng TripStops (thứ tự trạm lấy từ RouteStops). Vì vậy ở đây chỉ có Trips.
/// Buses được gộp vào cùng migration để Trips.BusId có khoá ngoại thật — A9 cũng xếp Buses (10)
/// trước Trips (12).
///
/// <c>Seats</c> từng nằm ở đây (Sprint 2) nhưng đã CHUYỂN sang AppDbContext.Seat.cs ở Sprint 3,
/// khi ghế có sơ đồ riêng và lượt giữ chỗ — xem file đó.
/// </summary>
public partial class AppDbContext
{
    public DbSet<Bus> Buses => Set<Bus>();
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

        // Cấu hình Seat nằm ở AppDbContext.Seat.cs (Sprint 3).

        modelBuilder.Entity<Trip>(e =>
        {
            e.Property(t => t.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

            // Tìm chuyến theo tuyến + giờ — chỉ mục A6 cho US 1 và US 13.
            // RouteId đứng đầu chỉ mục nên EF không sinh thêm chỉ mục riêng cho khoá ngoại này.
            //
            // INCLUDE (Id, BusId, Status) phục vụ báo cáo tỷ lệ lấp đầy theo chuyến (US 20 — Vàng Thị
            // Dăm): truy vấn lọc một tuyến trong kỳ rồi cần Id để trả về/đối chiếu vé, BusId để tra
            // sức chứa và Status để loại chuyến đã huỷ. Các cột đó nằm ngay trong chỉ mục nên
            // Postgres đọc được bằng index-only scan, không phải quay lại bảng Trips cho từng chuyến.
            e.HasIndex(t => new { t.RouteId, t.DepartureTime })
             .IncludeProperties(t => new { t.Id, t.BusId, t.Status });

            // Quét chuyến theo KHOẢNG THỜI GIAN, không lọc tuyến — nhánh "toàn công ty" của báo cáo
            // tỷ lệ lấp đầy (US 20). Chỉ mục (RouteId, DepartureTime) ở trên không dùng được cho
            // truy vấn này vì RouteId đứng đầu: Postgres không nhảy cóc qua cột tiền tố để lấy một
            // khoảng DepartureTime. INCLUDE đủ các cột báo cáo cần (Id để trả về/đối chiếu vé, RouteId
            // để gộp theo tuyến, BusId để tra sức chứa, Status để loại chuyến đã huỷ) — index-only
            // scan, không đọc bảng.
            e.HasIndex(t => t.DepartureTime)
             .IncludeProperties(t => new { t.Id, t.RouteId, t.BusId, t.Status });

            // Các chuyến của một tài xế trong một ngày — đúng truy vấn của service kiểm tra
            // trùng lịch tài xế (US 14): "tài xế X có chuyến nào trong ngày Y".
            // KHÔNG đặt unique: một tài xế chạy nhiều chuyến là bình thường, chỉ sai khi KHUNG
            // GIỜ CHỒNG LẤN — ràng buộc đó Postgres không diễn đạt được bằng chỉ mục, nên để
            // tầng service kiểm tra (task của Hoàng), không phải ràng buộc CSDL.
            e.HasIndex(t => new { t.DriverId, t.DepartureTime });

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

            // Tài xế bị xoá không được kéo theo chuyến đã chạy (A5). Users không nằm trong
            // danh sách ngoại lệ Cascade của A5 nên theo mặc định: Restrict. Hệ quả chấp nhận
            // được — hệ thống "xoá" tài khoản bằng User.IsActive chứ không xoá cứng (A4).
            // WithMany() rỗng vì User.cs là file của Hiếu và không cần collection nghịch —
            // cùng lối với Trip.CurrentStop ngay dưới đây.
            e.HasOne(t => t.Driver)
             .WithMany()
             .HasForeignKey(t => t.DriverId)
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
