using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Data;

/// <summary>
/// Phần DbContext của nhóm nghiệp vụ Sơ đồ ghế &amp; Giữ chỗ (US 2 + US 3 — Sprint 3):
/// <c>SeatLayouts</c>, <c>Seats</c>, <c>SeatHolds</c>, <c>SeatHoldLogs</c>.
/// Vàng Thị Dăm sửa file này — chủ CSDL của nhóm.
///
/// Cấu hình <c>Seat</c> được CHUYỂN từ AppDbContext.Trip.cs về đây: từ Sprint 3, "ghế" là một
/// nghiệp vụ riêng (sơ đồ, vị trí, giữ chỗ) chứ không còn là phần phụ của chuyến xe. Việc chuyển
/// này KHÔNG đổi mô hình EF — file partial nào cấu hình cũng như nhau, nên migration Sprint 3 chỉ
/// chứa thay đổi thật (2 bảng mới + 4 cột mới trên Seats).
///
/// Migration thứ hai của Sprint 3 — <c>Sprint3_SeatLayouts_CauHinhSoDoGhe</c> — thêm 3 cột cấu hình
/// lưới ghế cho <c>SeatLayouts</c> (số hàng/tầng, số cột/hàng, danh sách vị trí ghế VIP), tức dòng 9
/// của bảng phân công: "Cấu hình sơ đồ ghế theo loại xe (số tầng, số ghế, ghế VIP)".
///
/// Migration thứ ba — <c>Sprint3_SeatHoldLogs</c> — dựng bảng nhật ký <c>SeatHoldLogs</c> cho task
/// "Migrate bảng SeatHoldLogs + job quét hold hết hạn" (dòng 14), đi kèm job nền
/// <c>SeatHoldExpiryBackgroundService</c>.
/// </summary>
public partial class AppDbContext
{
    public DbSet<SeatLayout> SeatLayouts => Set<SeatLayout>();
    public DbSet<Seat> Seats => Set<Seat>();
    public DbSet<SeatHold> SeatHolds => Set<SeatHold>();
    public DbSet<SeatHoldLog> SeatHoldLogs => Set<SeatHoldLog>();

    partial void ConfigureSeat(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SeatLayout>(e =>
        {
            e.Property(l => l.BusType).HasMaxLength(50).IsRequired();

            // Vị trí ghế VIP là chuỗi ngắn có giới hạn (A3): mỗi vị trí "tầng-hàng-cột" ngăn bằng ';'.
            // 2000 ký tự là mức chừa rộng rãi cho trần của màn cấu hình (2 tầng × 13 hàng × 6 cột =
            // 156 ghế, ~1.100 ký tự nếu VIP hết) — đủ để không bao giờ chạm trần và Postgres cắt cụt
            // âm thầm. Rỗng là giá trị hợp lệ (sơ đồ không có ghế VIP), nên default là chuỗi rỗng chứ
            // không phải NULL: cột NOT NULL thì tầng service không phải phân biệt hai nghĩa "không có".
            e.Property(l => l.VipSeatPositions)
             .HasMaxLength(2000)
             .IsRequired()
             .HasDefaultValue(string.Empty);

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

        modelBuilder.Entity<SeatHoldLog>(e =>
        {
            e.Property(l => l.SessionCode).HasMaxLength(64).IsRequired();
            e.Property(l => l.Action).HasConversion<string>().HasMaxLength(20).IsRequired();

            // 🔴 Mỗi hành động của một lượt giữ xảy ra ĐÚNG MỘT LẦN, nên (SeatHoldId, Action) là
            // unique. Ràng buộc này không phải để cho đẹp: nó là thứ giữ cho nhật ký không đếm hai
            // lần khi CÓ HAI bản app cùng chạy job quét — cả hai cùng đọc một lượt giữ quá hạn rồi
            // cùng ghi log. Bên SeatHolds chuyện đó vô hại (hai bản ghi cùng một giá trị Status),
            // nhưng ở bảng chỉ-ghi-thêm thì nó thành hai dòng cho một sự kiện, và câu hỏi "tài khoản
            // này để hết hạn bao nhiêu lần" trả lời sai gấp đôi. Bản app thứ hai đâm vào unique là
            // SaveChanges ném DbUpdateException, cả lượt quét của nó rollback — lượt sau không còn
            // gì để làm vì bản thứ nhất đã lật xong.
            //
            // Nó cũng chính là chốt CSDL cho luật "gia hạn tối đa 1 lần" của US 3 (API gia hạn là
            // task của Trần Trung Hiếu): Extended thứ hai không lọt được xuống bảng.
            e.HasIndex(l => new { l.SeatHoldId, l.Action }).IsUnique();

            // Đếm số lượt giữ của một tài khoản trong một khoảng thời gian — task "Ghi log và cảnh
            // báo khi một tài khoản giữ chỗ quá nhiều lần". Cùng lối chỉ mục (UserId, CreatedAt)
            // của AuditLogs.
            e.HasIndex(l => new { l.UserId, l.CreatedAt });

            // FK nghiệp vụ → Restrict tường minh (A5): nhật ký phải giữ được dấu vết, xoá một lượt
            // giữ hay một tài khoản còn nhật ký là bị chặn chứ không được âm thầm xoá log.
            e.HasOne(l => l.SeatHold)
             .WithMany()
             .HasForeignKey(l => l.SeatHoldId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(l => l.User)
             .WithMany()
             .HasForeignKey(l => l.UserId)
             .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
