using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Data;

/// <summary>
/// Phần DbContext của nhóm nghiệp vụ Huỷ vé (US 5 — "Hủy &amp; Đổi vé", Sprint 4): bảng
/// <c>CancellationRequests</c> (A9 KHÔNG có bảng này — xem ghi chú "ngoài A9" ở
/// <c>docs/32-csdl-huy-doi-ve.md</c> §1).
/// Vàng Thị Dăm sửa file này — chủ CSDL của nhóm.
///
/// Dựng theo task Sprint 4 *"Migrate bảng TicketChanges, CancellationRequests"* (Vàng Thị Dăm).
/// Bảng anh em <c>TicketChanges</c> nằm ở <c>AppDbContext.TicketChange.cs</c> — cố ý tách hai file
/// theo đúng lối "một file một nghiệp vụ" của repo (AppDbContext.Ticket.cs, AppDbContext.Payment.cs,
/// AppDbContext.DiscountRequest.cs…), dù hai bảng cùng một user story.
/// </summary>
public partial class AppDbContext
{
    public DbSet<CancellationRequest> CancellationRequests => Set<CancellationRequest>();

    partial void ConfigureCancellationRequest(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CancellationRequest>(e =>
        {
            // Trạng thái lưu chuỗi (A3). KHÔNG đặt HasDefaultValue — theo đúng khuôn Ticket.Status và
            // SeatHold.Status: service gửi yêu cầu luôn ghi 'Pending' tường minh, nên một DEFAULT ở
            // tầng CSDL chỉ tạo thêm một đường ghi ra dòng mới mà không ai chủ ý.
            e.Property(c => c.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

            // Tiền: numeric(12,2) — cùng đơn vị và cùng độ rộng với Tickets.Price, Fares.Price,
            // Payments.Amount, MonthlyPass.Price. Không dùng float/double cho tiền (A3). Không có cột
            // số tiền hoàn: nó là Ticket.Price - CancellationFee, xem doc-comment CancellationFee.
            e.Property(c => c.CancellationFee).HasPrecision(12, 2);

            // Reason và RejectReason cố ý KHÔNG đặt HasMaxLength: để Npgsql map thành text. A3 xếp
            // "mô tả dài" vào text, và lý do huỷ / lý do từ chối không có độ dài trần hợp lý — cùng
            // lối đã dùng cho DiscountRequest.EvidenceUrl và RejectReason.

            // 🔴 Một vé chỉ được có MỘT yêu cầu huỷ ĐANG CHỜ — chốt ở tầng CSDL, cùng tinh thần chỉ
            // mục điều kiện chống bán trùng ghế của A6 (và bản SeatHolds đang mô phỏng).
            //
            // Vì sao phải chặn ở đây chứ không giao hết cho service: hai tab cùng bấm "Gửi yêu cầu
            // huỷ" là chuyện xảy ra được — cả hai đọc "vé chưa có yêu cầu nào", cả hai ghi. Hai dòng
            // Pending cho một vé nghĩa là hai lượt duyệt, tức hai khoản hoàn tiền cho một chiếc vé.
            // Đây là lỗi tiền, đúng loại mà A5/A6 sinh ra để chặn ở tầng CSDL.
            //
            // Vì sao là chỉ mục ĐIỀU KIỆN chứ không phải unique thường trên TicketId: một vé hợp lệ
            // khi có nhiều yêu cầu theo thời gian — bị từ chối rồi xin lại. Unique thường sẽ chặn
            // vĩnh viễn lần xin thứ hai, còn điều kiện dưới đây chỉ chặn khi yêu cầu ĐANG CHỜ.
            //
            // ⚠️ Ràng buộc này CHỈ áp trong bảng này. Luật "một vé không thể vừa có yêu cầu huỷ vừa
            // có yêu cầu đổi đang chờ" (hai bảng khác nhau) không diễn đạt được bằng chỉ mục — việc
            // của service, xem docs/29 §5.
            e.HasIndex(c => c.TicketId, "IX_CancellationRequests_TicketId_Pending")
             .IsUnique()
             .HasFilter("\"Status\" = 'Pending'");

            // 🔴 Chỉ mục này KHÔNG thừa dù chỉ mục điều kiện ở trên cũng mở đầu bằng TicketId —
            // đúng bài học đã ghi ở docs/28 §4 cho IX_Tickets_TripId. PostgreSQL chỉ dùng được một
            // partial index khi truy vấn CHỨNG MINH được điều kiện của nó; câu hỏi "vé này từng bị
            // xin huỷ chưa" không nói gì về Status nên không thoả, và bộ lập kế hoạch sẽ bỏ qua chỉ
            // mục kia.
            //
            // Vì sao phải gọi HasIndex kèm TÊN: EF nhận diện chỉ mục theo danh sách cột, nên lần gọi
            // thứ hai cho cùng một cột sẽ trả về đúng cái builder của lần gọi đầu (đã unique + có
            // điều kiện) chứ không tạo chỉ mục thứ hai — im lặng, không báo lỗi. Có tên riêng thì EF
            // coi là hai chỉ mục khác nhau và sinh đủ cả hai. Tên đặt tay vì tên theo quy ước
            // (IX_CancellationRequests_TicketId) đã bị chỉ mục điều kiện ở trên chiếm.
            //
            // Chỉ mục này cũng phục vụ luôn khoá ngoại TicketId — EF thấy cột đã có chỉ mục nên
            // không sinh thêm chỉ mục FK trùng.
            e.HasIndex(c => c.TicketId, "IX_CancellationRequests_TicketId");

            // "Yêu cầu huỷ - đổi của tôi" của màn hành khách, và cũng là chỉ mục cho khoá ngoại UserId.
            e.HasIndex(c => c.UserId);

            // Hàng đợi duyệt của Admin: "các yêu cầu đang chờ, cũ nhất trước". Status đứng đầu vì
            // Pending là số ít so với Approved/Rejected, CreatedAt đứng sau để sắp được luôn. Cùng
            // khuôn với AppDbContext.DiscountRequest.cs nên màn duyệt gộp đọc được cả hai bảng.
            e.HasIndex(c => new { c.Status, c.CreatedAt });

            // FK nghiệp vụ → Restrict tường minh (A5). Mặc định của EF là Cascade: xoá một vé mà kéo
            // theo yêu cầu huỷ là mất dấu vết kiểm toán của một khoản hoàn tiền.
            e.HasOne(c => c.Ticket)
             .WithMany()
             .HasForeignKey(c => c.TicketId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(c => c.User)
             .WithMany()
             .HasForeignKey(c => c.UserId)
             .OnDelete(DeleteBehavior.Restrict);

            // Người duyệt cũng là một hàng Users, nên đây là khoá ngoại thứ hai cùng trỏ Users —
            // Postgres và EF đều xử lý bình thường vì hai navigation có tên khác nhau (User vs
            // ReviewedBy) và đều khai báo tường minh. Cùng lối DiscountRequest.
            //
            // WithMany() rỗng cho CẢ BA khoá ngoại: không thêm collection nghịch vào User.cs /
            // Ticket.cs — hai file mà người khác đang đọc, và AppDbContext.Ticket.cs đã đi trước
            // đúng lối này.
            e.HasOne(c => c.ReviewedBy)
             .WithMany()
             .HasForeignKey(c => c.ReviewedByUserId)
             .OnDelete(DeleteBehavior.Restrict);

            // Khoá phiên bản dòng cho nhánh đua "hai admin cùng duyệt một yêu cầu" — cùng lối đã
            // dùng ở Payments (callback trùng) và Vouchers (hai lượt áp cùng lúc).
            //
            // Vì sao bảng này cần mà Tickets thì cố ý không có: việc DUYỆT chính là nhánh đua. Hai
            // quản lý cùng bấm Duyệt trên một yêu cầu Pending, cả hai đều đọc thấy Pending rồi cùng
            // ghi Approved — chỉ mục 'Pending' ở trên không cứu được vì lúc ghi cả hai đều đã rời
            // khỏi tập Pending. Với xmin, EF thêm "WHERE xmin = ..." vào câu UPDATE, lượt thua nhận
            // DbUpdateConcurrencyException và service coi là "đã có người xử lý" — chặn đúng khoản
            // hoàn tiền thứ hai. xmin là cột hệ thống của PostgreSQL — KHÔNG sinh cột mới trong
            // migration.
            e.Property<uint>("xmin").HasColumnName("xmin").IsRowVersion();
        });
    }
}
