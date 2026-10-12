using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Data;

/// <summary>
/// Phần DbContext của nhóm nghiệp vụ Thông báo trong ứng dụng: bảng <c>Notifications</c> — A9 #15.
/// Vàng Thị Dăm sửa file này — chủ CSDL của nhóm.
///
/// Dựng theo task Sprint 4 *"Ghi log và thông báo kết quả xử lý cho hành khách"* (Vàng Thị Dăm,
/// dòng 7): duyệt/từ chối yêu cầu huỷ hoặc đổi vé (US 5) thì khách phải nhận được kết quả, mà A8.5
/// chốt thông báo đi **in-app qua SignalR + lưu bảng `Notifications`**. Không có bảng thì nửa sau
/// của A8.5 (*"lưu vào bảng để khách xem lại thông báo khi offline"*) không có chỗ nào để ghi.
///
/// ⚠️ Bảng này là việc của Sprint 5 theo bảng phân công (*"Migrate bảng Notifications,
/// NotificationTemplates, Subscriptions"*, Vàng Thị Dăm, dòng 14) — nhánh này **kéo trước đúng một
/// bảng** vì task dòng 7 không chạy được nếu thiếu nó. Hai bảng còn lại (<c>NotificationTemplates</c>,
/// <c>Subscriptions</c>) KHÔNG được kéo theo: chưa task Sprint 4 nào cần tới, xem
/// <c>docs/33-thong-bao-ket-qua-huy-doi-ve.md</c> §1.
/// </summary>
public partial class AppDbContext
{
    public DbSet<Notification> Notifications => Set<Notification>();

    partial void ConfigureNotification(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Notification>(e =>
        {
            // Trạng thái/loại lưu chuỗi (A3). KHÔNG đặt HasDefaultValue — cùng lối Ticket.Status và
            // CancellationRequest.Status: service luôn ghi giá trị tường minh, nên một DEFAULT ở tầng
            // CSDL chỉ tạo thêm một đường ghi ra dòng mới mà không ai chủ ý.
            //
            // varchar(30): giá trị dài nhất hiện tại là "TicketChangeRejected" (20 ký tự). Cố ý KHÔNG
            // dùng varchar(20) như các cột Status khác — 20 là đúng bằng độ dài dài nhất, nên giá trị
            // hợp lệ kế tiếp ("TicketChangeRequestWithdrawn"…) sẽ vỡ ở tầng CSDL thay vì ở tầng code.
            // Rộng hơn giá trị dài nhất một khoảng an toàn là chủ ý; cột varchar không tốn thêm gì.
            e.Property(n => n.Type).HasConversion<string>().HasMaxLength(30).IsRequired();

            // Nullable: thông báo hệ thống (Sprint 5) không trỏ vào bản ghi nào. Bộ chuyển đổi chuỗi
            // của EF chỉ áp cho giá trị khác null, nên NULL vẫn là NULL dưới CSDL.
            e.Property(n => n.RelatedEntityType).HasConversion<string>().HasMaxLength(30);

            // Tiêu đề ngắn để màn danh sách không phải tải cả phần thân.
            e.Property(n => n.Title).HasMaxLength(200).IsRequired();

            // Body cố ý KHÔNG đặt HasMaxLength: để Npgsql map thành text. Lý do từ chối do quản lý
            // nhập được chép nguyên văn vào đây và không có độ dài trần hợp lý — cùng lối đã dùng cho
            // CancellationRequest.Reason / RejectReason.

            // 🔴 Chỉ mục của A6, đúng cặp cột A6 ghi: `Notifications (UserId, CreatedAt)`. Đây là câu
            // truy vấn DUY NHẤT của bảng ở giai đoạn này — "danh sách thông báo của tôi, mới nhất
            // trước" (Sprint 5, dòng 19) — và cũng là câu mà phép chống ghi trùng của
            // TicketRequestResultNotifier lọc trước khi ghi.
            //
            // Chỉ mục này cũng phục vụ luôn khoá ngoại UserId: EF thấy cột đã có chỉ mục nên không
            // sinh thêm chỉ mục FK trùng.
            e.HasIndex(n => new { n.UserId, n.CreatedAt });

            // ⚠️ Cố ý KHÔNG thêm chỉ mục cho (RelatedEntityType, RelatedEntityId): câu hỏi "yêu cầu này
            // đã được báo cho khách chưa" luôn lọc kèm UserId (một yêu cầu thuộc về đúng một người),
            // nên chỉ mục trên đã thu hẹp về vài chục dòng của một khách trước khi tới hai cột kia.
            // Thêm chỉ mục thứ hai là trả giá ghi cho mọi dòng thông báo để đổi lấy một phép lọc trên
            // tập đã nhỏ.

            // FK nghiệp vụ → Restrict tường minh (A5). Mặc định của EF là Cascade: xoá một tài khoản mà
            // kéo theo thông báo của họ là mất dấu vết "hệ thống đã báo gì cho khách, lúc nào" — đúng
            // loại dữ liệu phải tra khi khách khiếu nại là chưa nhận được thông báo. WithMany() rỗng:
            // không thêm collection nghịch vào User.cs.
            e.HasOne(n => n.User)
             .WithMany()
             .HasForeignKey(n => n.UserId)
             .OnDelete(DeleteBehavior.Restrict);

            // ⚠️ Cố ý KHÔNG có khoá phiên bản dòng (xmin) như CancellationRequests/TicketChanges: bảng
            // chỉ ghi thêm (A4), không có nhánh đua nào giữa hai lượt SỬA cùng một dòng để mà chặn. Chốt
            // chống ghi trùng của notifier là một phép ĐỌC TRƯỚC KHI GHI, và nó chỉ cần đúng khi chạy
            // một bản app — xem docs/33 §4.
        });
    }
}
