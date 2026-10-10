using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Data;

/// <summary>
/// Phần DbContext của nhóm nghiệp vụ Vé điện tử (US 4 — Sprint 3): bảng <c>Tickets</c>.
/// Vàng Thị Dăm sửa file này — chủ CSDL của nhóm.
///
/// Dựng theo task dòng "Migrate bảng Tickets, TicketQRCodes" của bảng phân công Sprint 3.
/// ⚠️ Tên task ghi hai bảng nhưng chỉ có MỘT bảng được dựng: mã QR là cột <c>Code</c> của
/// <c>Tickets</c>, không phải bảng <c>TicketQRCodes</c> riêng — lý do đầy đủ ở doc-comment của
/// <see cref="Ticket"/> và <c>docs/28-csdl-ve-dien-tu.md</c> §2.
///
/// File này được nhắc tên sẵn trong doc-comment của <c>Data/AppDbContext.cs</c> từ trước khi nó tồn
/// tại ("AppDbContext.Auth.cs, AppDbContext.Route.cs, AppDbContext.Ticket.cs, ...") — giờ nó có thật.
/// </summary>
public partial class AppDbContext
{
    public DbSet<Ticket> Tickets => Set<Ticket>();

    partial void ConfigureTicket(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Ticket>(e =>
        {
            // Mã QR in trên vé. 200 ký tự là mức chừa cho payload CÓ KÝ SỐ của task "Service sinh mã
            // QR duy nhất + ký số chống làm giả" (Nguyễn Duy Kiên) — xem doc-comment của Ticket.Code.
            e.Property(t => t.Code).HasMaxLength(200).IsRequired();

            // Vé phải tra được bằng chính mã QR cổng đọc lên — đây là đường quét nóng nhất của cả
            // tính năng, và cũng là ràng buộc A6: mã vé duy nhất toàn hệ thống, không chỉ trong một
            // chuyến. Trùng mã nghĩa là hai khách cùng cầm một mã QR — tức làm giả vé được.
            e.HasIndex(t => t.Code).IsUnique();

            // Giá tiền: numeric(12,2), cùng đơn vị và cùng độ rộng với Fares.Price, Payments.Amount
            // và MonthlyPass.Price. Không dùng float/double cho tiền (A3).
            e.Property(t => t.Price).HasPrecision(12, 2);

            // Trạng thái lưu chuỗi (A3): "Paid" đọc là hiểu, còn 2 thì phải tra bảng mã.
            // KHÔNG đặt HasDefaultValue — theo đúng khuôn SeatHold.Status: service phát hành luôn ghi
            // 'Paid' tường minh (đó là bước 2 của luồng trong api-contract), nên một DEFAULT ở tầng
            // CSDL chỉ tạo thêm một đường ghi ra vé mà không ai chủ ý.
            e.Property(t => t.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

            // 🔴 Chống bán trùng ghế ở tầng CSDL — bản partial unique index mà A6 đã chốt cho Tickets,
            // và là bản gốc mà SeatHolds đang mô phỏng (AppDbContext.Seat.cs dùng
            // WHERE "Status" = 'Holding').
            //
            // Vì sao phải là CHỈ MỤC ĐIỀU KIỆN chứ không phải unique thường trên (TripId, SeatId):
            // một ghế của một chuyến hợp lệ khi có nhiều vé theo thời gian — vé cũ đã huỷ, rồi khách
            // khác mua lại chính ghế đó. Unique thường sẽ chặn vĩnh viễn việc bán lại, còn điều kiện
            // dưới đây chỉ chặn khi vé ĐANG CÓ HIỆU LỰC, tức đúng lúc cần chặn.
            //
            // ⚠️ Nhánh 'Held' giữ nguyên theo chữ đã chốt của A6/api-contract dù enum
            // TicketStatus không có giá trị này — không dòng nào mang 'Held' nên nhánh đó là dự phòng
            // vô hại; lý do đầy đủ ở doc-comment của TicketStatus. Đừng "dọn" nó đi: đó là chữ của
            // một luật cứng, và nếu nhóm sau này cho vé sinh ngay từ lúc giữ chỗ thì chỉ mục đã sẵn.
            //
            // EF Core không sinh được partial index bằng attribute — phải viết ở đây.
            e.HasIndex(t => new { t.TripId, t.SeatId })
             .IsUnique()
             .HasFilter("\"Status\" IN ('Held', 'Paid')");

            // "Danh sách khách của chuyến" (A6): WHERE TripId = @t, KHÔNG kèm điều kiện Status.
            //
            // 🔴 Chỉ mục này KHÔNG thừa dù chỉ mục điều kiện ở trên cũng mở đầu bằng TripId. Postgres
            // chỉ dùng được một partial index khi truy vấn CHỨNG MINH được điều kiện của nó — câu hỏi
            // "vé của chuyến này" không nói gì về Status nên không thoả, và bộ lập kế hoạch sẽ bỏ qua
            // chỉ mục đó. Đây cũng là chỗ EF dễ đánh lừa: quy ước sinh chỉ mục cho khoá ngoại của EF
            // thấy TripId đã là cột tiền tố của một chỉ mục đã khai nên nó KHÔNG tự sinh thêm — khai
            // tường minh ở đây là cách duy nhất để có chỉ mục dùng được cho truy vấn này.
            e.HasIndex(t => t.TripId);

            // "Tra vé của tôi" (A6): WHERE UserId = @u — đường của GET /tickets/me.
            // Chỉ mục này phục vụ luôn khoá ngoại UserId (UserId là cột tiền tố) nên EF không sinh
            // thêm chỉ mục FK riêng cho UserId.
            e.HasIndex(t => t.UserId);

            // FK nghiệp vụ → Restrict tường minh (A5). Mặc định của EF là Cascade, và ở đây hậu quả
            // đúng như cảnh báo đầu dự án: xoá một tuyến kéo theo chuyến, rồi chuyến kéo theo vé — mất
            // dữ liệu doanh thu. Xoá chuyến / xoá ghế / xoá tài khoản / xoá trạm đều phải bị chặn.
            e.HasOne(t => t.Trip)
             .WithMany()
             .HasForeignKey(t => t.TripId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(t => t.Seat)
             .WithMany()
             .HasForeignKey(t => t.SeatId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(t => t.User)
             .WithMany()
             .HasForeignKey(t => t.UserId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(t => t.BoardingStop)
             .WithMany()
             .HasForeignKey(t => t.BoardingStopId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(t => t.AlightingStop)
             .WithMany()
             .HasForeignKey(t => t.AlightingStopId)
             .OnDelete(DeleteBehavior.Restrict);

            // ⚠️ Cố ý KHÔNG khai concurrency token xmin ở đây, dù Payments và Vouchers đều có.
            // Hai bảng kia cần vì tính năng của chúng có nhánh đua đã gặp thật: callback cổng gửi
            // lại (Payments), và hai lượt áp voucher cùng lúc (Vouchers). Bảng vé chưa có nhánh đua
            // nào thuộc phạm vi Sprint 3 — bước phát hành đã được chốt bằng idempotency phía Payments
            // cộng chỉ mục duy nhất điều kiện ở trên, còn việc soát vé (Paid → Used) là story khác.
            // Thêm sau KHÔNG tốn migration cột: xmin là cột hệ thống của PostgreSQL, EF chỉ đọc nó.
        });
    }
}
