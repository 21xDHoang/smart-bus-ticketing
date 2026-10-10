using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Data;

/// <summary>
/// Phần DbContext của nhóm nghiệp vụ Thanh toán (US 6 — Sprint 3): bảng <c>Payments</c> (A9 #17).
/// Vàng Thị Dăm phụ trách nhóm bảng này (dòng 27); file dựng theo uỷ quyền 10/10/2026 cho task
/// dòng 33 *"Xử lý idempotency: chống trừ tiền 2 lần khi callback trùng"* — Phùng Duy Hoàng.
/// </summary>
public partial class AppDbContext
{
    public DbSet<Payment> Payments => Set<Payment>();

    partial void ConfigurePayment(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Payment>(e =>
        {
            // Khoá chống trùng của cả luồng (A9 + hợp đồng mục "Thanh toán"): một PaymentCode chỉ có
            // một dòng — callback trùng tìm lại đúng dòng đó để bỏ qua, không tạo dòng mới.
            e.Property(p => p.PaymentCode).HasMaxLength(64).IsRequired();
            e.HasIndex(p => p.PaymentCode).IsUnique();

            e.Property(p => p.MethodCode).HasMaxLength(20).IsRequired();

            // numeric(12,2) — cùng đơn vị và cùng độ rộng với Fares.Price.
            e.Property(p => p.Amount).HasPrecision(12, 2);

            e.Property(p => p.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

            e.Property(p => p.GatewayTransactionId).HasMaxLength(50);
            e.Property(p => p.Message).HasMaxLength(500);

            // Lưu vết ghế đã chọn, ngăn bằng ';' — cùng lối SeatLayouts.VipSeatPositions.
            e.Property(p => p.SeatNumbers).HasMaxLength(500).IsRequired().HasDefaultValue(string.Empty);

            // Đường quét của job đối soát (mỗi phút): Status = 'Pending' và CreatedAt cũ hơn 5 phút.
            e.HasIndex(p => new { p.Status, p.CreatedAt });

            // A6 chốt index này cho Payments — đường tra ngược "giao dịch này đã sinh vé nào".
            e.HasIndex(p => p.TicketId);

            e.HasOne(p => p.User).WithMany()
                .HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Restrict);

            e.HasOne(p => p.Trip).WithMany()
                .HasForeignKey(p => p.TripId).OnDelete(DeleteBehavior.Restrict);

            // Nối FK sang vé — đúng ghi chú để lại lúc dựng bảng ("nối khi bảng vé có"), nay bảng
            // Tickets đã migrate. Restrict tường minh (A5): mặc định của EF là Cascade, và xoá một vé
            // mà kéo theo dòng tiền đã thu là mất dữ liệu doanh thu — đúng lỗi tốn kém nhất mà A5
            // được viết ra để chặn. Cột nullable nên quan hệ là tuỳ chọn: giao dịch Pending chưa có vé.
            e.HasOne(p => p.Ticket).WithMany()
                .HasForeignKey(p => p.TicketId).OnDelete(DeleteBehavior.Restrict);

            // Khoá phiên bản dòng cho nhánh đua "callback trùng": hai luồng cùng lật MỘT dòng
            // (IPN gửi lại, hoặc callback + job đối soát chạy trùng) — EF thêm "WHERE xmin = ..."
            // vào câu UPDATE, luồng thua nhận DbUpdateConcurrencyException và được service coi là
            // lượt trùng (bỏ qua, vẫn trả 204). xmin là cột hệ thống của PostgreSQL — KHÔNG sinh
            // cột mới trong migration; chốt với Dăm lúc dựng bảng (cùng lối đã bàn ở a374401).
            e.Property<uint>("xmin").HasColumnName("xmin").IsRowVersion();
        });
    }
}
