using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Data;

/// <summary>
/// Phần DbContext của nhóm nghiệp vụ Sơ đồ ghế &amp; Giữ chỗ (US 2 + US 3 — Sprint 3):
/// <c>SeatLayouts</c>, <c>Seats</c>, <c>SeatHolds</c>. Vàng Thị Dăm sửa file này — chủ CSDL của nhóm.
///
/// Cấu hình <c>Seat</c> được CHUYỂN từ AppDbContext.Trip.cs về đây: từ Sprint 3, "ghế" là một
/// nghiệp vụ riêng (sơ đồ, vị trí, giữ chỗ) chứ không còn là phần phụ của chuyến xe. Việc chuyển
/// này KHÔNG đổi mô hình EF — file partial nào cấu hình cũng như nhau, nên migration Sprint 3 chỉ
/// chứa thay đổi thật (2 bảng mới + 4 cột mới trên Seats).
/// </summary>
public partial class AppDbContext
{
    public DbSet<SeatLayout> SeatLayouts => Set<SeatLayout>();
    public DbSet<Seat> Seats => Set<Seat>();
    public DbSet<SeatHold> SeatHolds => Set<SeatHold>();

    partial void ConfigureSeat(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SeatLayout>(e =>
        {
            e.Property(l => l.BusType).HasMaxLength(50).IsRequired();

            // Mỗi loại xe đúng MỘT sơ đồ — khoá nghiệp vụ, cùng lối Buses.LicensePlate và
            // Routes.Code đã đặt unique ở các file cấu hình khác.
            e.HasIndex(l => l.BusType).IsUnique();
        });

        modelBuilder.Entity<Seat>(e =>
        {
            e.Property(s => s.SeatNumber).HasMaxLength(10).IsRequired();

            // Loại ghế lưu dạng chuỗi (A3): "Vip" đọc là hiểu, còn 1 thì phải tra bảng mã.
            // Default ở tầng CSDL phải là giá trị enum HỢP LỆ — để mặc định thì EF sinh DEFAULT ''
            // và dòng nào lỡ dính default đó sẽ làm EF ném lỗi ngay khi đọc.
            e.Property(s => s.SeatType)
             .HasConversion<string>()
             .HasMaxLength(20)
             .IsRequired()
             .HasDefaultValue(SeatType.Standard);

            // Ghế mặc định ở tầng 1: 0 là giá trị vô nghĩa với một sơ đồ xe.
            e.Property(s => s.Floor).HasDefaultValue(1);

            // Một xe không có hai ghế cùng số (A6).
            e.HasIndex(s => new { s.BusId, s.SeatNumber }).IsUnique();

            // Ghế thuộc về xe — xoá xe thì dàn ghế đi theo (A5).
            e.HasOne(s => s.Bus)
             .WithMany(b => b.Seats)
             .HasForeignKey(s => s.BusId)
             .OnDelete(DeleteBehavior.Cascade);

            // Sơ đồ bị xoá KHÔNG được kéo theo ghế đã sinh (A5 — FK nghiệp vụ để Restrict tường
            // minh): ghế đã in trên vé mà biến mất là mất dữ liệu bán vé, không phải lỗi hiển thị.
            e.HasOne(s => s.SeatLayout)
             .WithMany(l => l.Seats)
             .HasForeignKey(s => s.SeatLayoutId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SeatHold>(e =>
        {
            e.Property(h => h.SessionCode).HasMaxLength(64).IsRequired();
            e.Property(h => h.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

            // 🔴 Chống hai khách giữ cùng một ghế — bản partial unique index mà A6 đã chốt cho
            // Tickets, áp đúng tinh thần đó cho SeatHolds: chỉ dòng ĐANG GIỮ mới chặn ghế; hold đã
            // hết hạn / đã nhả / đã thành vé thì không giữ ghế nữa nên không được chặn.
            // EF Core không sinh được partial index bằng attribute, phải viết ở đây.
            e.HasIndex(h => new { h.TripId, h.SeatId })
             .IsUnique()
             .HasFilter("\"Status\" = 'Holding'");

            // Job quét hold hết hạn (BackgroundService — task "Migrate bảng SeatHoldLogs + job quét
            // hold hết hạn") quét đúng cột này: Holding và ExpiresAt < now.
            e.HasIndex(h => h.ExpiresAt);

            // API tra trạng thái giữ chỗ / gia hạn / đếm ngược đều hỏi theo mã phiên.
            // Không unique: một phiên giữ nhiều ghế.
            e.HasIndex(h => h.SessionCode);

            // Task "Ghi log và cảnh báo khi một tài khoản giữ chỗ quá nhiều lần": đếm số lượt giữ
            // của một tài khoản trong một khoảng thời gian. UserId đứng đầu chỉ mục nên EF không
            // sinh thêm chỉ mục riêng cho khoá ngoại này.
            e.HasIndex(h => new { h.UserId, h.CreatedAt });

            // FK nghiệp vụ → Restrict tường minh (A5): xoá chuyến, xoá ghế hay xoá tài khoản đều
            // KHÔNG được kéo theo lượt giữ chỗ.
            e.HasOne(h => h.Trip)
             .WithMany()
             .HasForeignKey(h => h.TripId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(h => h.Seat)
             .WithMany()
             .HasForeignKey(h => h.SeatId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(h => h.User)
             .WithMany()
             .HasForeignKey(h => h.UserId)
             .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
