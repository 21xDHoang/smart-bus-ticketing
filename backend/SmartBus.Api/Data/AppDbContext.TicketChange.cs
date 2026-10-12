using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Data;

/// <summary>
/// Phần DbContext của nhóm nghiệp vụ Đổi chuyến (US 5 — "Hủy &amp; Đổi vé", Sprint 4): bảng
/// <c>TicketChanges</c> (A9 KHÔNG có bảng này — xem ghi chú "ngoài A9" ở
/// <c>docs/32-csdl-huy-doi-ve.md</c> §1).
/// Vàng Thị Dăm sửa file này — chủ CSDL của nhóm.
///
/// Dựng theo task Sprint 4 *"Migrate bảng TicketChanges, CancellationRequests"* (Vàng Thị Dăm).
/// Bảng anh em <c>CancellationRequests</c> nằm ở <c>AppDbContext.CancellationRequest.cs</c>.
///
/// 🔴 Duyệt một yêu cầu đổi KHÔNG sửa vé cũ — nó huỷ vé cũ rồi phát hành vé MỚI
/// (<see cref="TicketChange.NewTicketId"/>). Chốt 12/10/2026, lý do ở <c>docs/32-csdl-huy-doi-ve.md</c> §2.
/// </summary>
public partial class AppDbContext
{
    public DbSet<TicketChange> TicketChanges => Set<TicketChange>();

    partial void ConfigureTicketChange(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TicketChange>(e =>
        {
            // Trạng thái lưu chuỗi (A3), không HasDefaultValue — cùng lý do như CancellationRequest.
            e.Property(t => t.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

            // Giá vé của chuyến MỚI: numeric(12,2) (A3). Chênh lệch giá là NewPrice - Ticket.Price,
            // cố ý không có cột riêng — xem doc-comment TicketChange.NewPrice.
            e.Property(t => t.NewPrice).HasPrecision(12, 2);

            // Reason và RejectReason để Npgsql map thành text (A3) — cùng lối bảng anh em.

            // 🔴 Một vé chỉ được có MỘT yêu cầu đổi ĐANG CHỜ. Cùng đúng lý do và cùng đúng khuôn với
            // chỉ mục điều kiện của CancellationRequests — đọc ghi chú dài ở
            // AppDbContext.CancellationRequest.cs, phần "Một vé chỉ được có MỘT yêu cầu huỷ ĐANG CHỜ".
            // Ở bảng này hậu quả nặng hơn một bậc: hai yêu cầu đổi cùng được duyệt nghĩa là hai vé mới
            // phát hành và chuyến cũ bị huỷ hai lần.
            e.HasIndex(t => t.TicketId, "IX_TicketChanges_TicketId_Pending")
             .IsUnique()
             .HasFilter("\"Status\" = 'Pending'");

            // Chỉ mục thường cho cùng cột — KHÔNG thừa, vì câu hỏi "vé này từng bị xin đổi chưa"
            // không chứng minh được điều kiện Status nên Postgres bỏ qua chỉ mục điều kiện ở trên.
            // Phải gọi kèm TÊN riêng, nếu không EF trả về đúng builder của lần gọi đầu và im lặng
            // không sinh chỉ mục thứ hai. Lý do đầy đủ: AppDbContext.CancellationRequest.cs.
            e.HasIndex(t => t.TicketId, "IX_TicketChanges_TicketId");

            // "Yêu cầu huỷ - đổi của tôi" + chỉ mục cho khoá ngoại UserId.
            e.HasIndex(t => t.UserId);

            // Hàng đợi duyệt của Admin — cùng khuôn với CancellationRequests và DiscountRequests để
            // màn duyệt gộp sắp xếp được cả ba.
            e.HasIndex(t => new { t.Status, t.CreatedAt });

            // FK nghiệp vụ → Restrict tường minh (A5). Mặc định của EF là Cascade, và ở đây hậu quả
            // đúng như cảnh báo đầu dự án: xoá một chuyến / một ghế / một vé mà kéo theo yêu cầu đổi
            // là mất dấu vết của một lượt đổi đã phát hành vé thật.
            e.HasOne(t => t.Ticket)
             .WithMany()
             .HasForeignKey(t => t.TicketId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(t => t.User)
             .WithMany()
             .HasForeignKey(t => t.UserId)
             .OnDelete(DeleteBehavior.Restrict);

            // Chuyến và ghế MUỐN ĐỔI SANG. Restrict chứ không Cascade: xoá một chuyến không được
            // âm thầm xoá yêu cầu đổi của khách — chuyến bị xoá mềm (Trip.Status = Cancelled) chứ
            // không xoá cứng, và yêu cầu còn Pending sẽ được service xử lý lại.
            e.HasOne(t => t.NewTrip)
             .WithMany()
             .HasForeignKey(t => t.NewTripId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(t => t.NewSeat)
             .WithMany()
             .HasForeignKey(t => t.NewSeatId)
             .OnDelete(DeleteBehavior.Restrict);

            // Vé MỚI phát hành khi duyệt. Cột nullable nên quan hệ là tuỳ chọn: yêu cầu Pending chưa
            // có vé mới, yêu cầu Rejected thì không bao giờ có. Restrict đúng A5 — xoá một vé không
            // được kéo theo dấu vết của lượt đổi đã sinh ra nó.
            e.HasOne(t => t.NewTicket)
             .WithMany()
             .HasForeignKey(t => t.NewTicketId)
             .OnDelete(DeleteBehavior.Restrict);

            // Người duyệt — khoá ngoại thứ hai cùng trỏ Users, cùng lối DiscountRequest.
            e.HasOne(t => t.ReviewedBy)
             .WithMany()
             .HasForeignKey(t => t.ReviewedByUserId)
             .OnDelete(DeleteBehavior.Restrict);

            // Khoá phiên bản dòng cho nhánh đua "hai admin cùng duyệt một yêu cầu" — cùng lý do đã
            // ghi đầy đủ ở AppDbContext.CancellationRequest.cs. Ở bảng này lượt thua còn phải không
            // được phát hành vé mới thứ hai.
            e.Property<uint>("xmin").HasColumnName("xmin").IsRowVersion();
        });
    }
}
