using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Data;

/// <summary>
/// Phần DbContext của nhóm nghiệp vụ Thông báo — bảng <c>NotificationTemplates</c> (MẪU nội dung thông
/// báo). Tách khỏi <c>AppDbContext.Notification.cs</c> dù cùng nhóm: bảng kia là **thông báo đã gửi**
/// (chỉ ghi thêm, không sửa được), bảng này là **cấu hình do quản trị viên sửa** — hai vòng đời khác
/// hẳn nhau, và file kia đang gắn với một nhánh đã xong (docs/33) nên không đụng vào.
///
/// Dựng theo task *"Migrate bảng Notifications, NotificationTemplates, Subscriptions"* của bảng phân
/// công **Sprint 5, dòng 14** (Vàng Thị Dăm). Bảng <c>Notifications</c> đã được kéo trước ở Sprint 4
/// vì task dòng 7 không chạy được nếu thiếu nó (<c>docs/33</c> §1) — nên dòng 14 còn lại **đúng hai
/// bảng**, không làm lại bảng thứ ba. Chi tiết: <c>docs/37-csdl-mau-thong-bao-va-theo-doi.md</c>.
/// </summary>
public partial class AppDbContext
{
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();

    partial void ConfigureNotificationTemplate(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NotificationTemplate>(e =>
        {
            // Loại thông báo lưu chuỗi (A3). CÙNG độ rộng varchar(30) với Notification.Type — không
            // phải để cho đẹp: giá trị ở cột này được chép thẳng sang cột kia khi dùng mẫu, nên hai
            // cột lệch độ rộng là một cái bẫy chỉ nổ ở lượt gửi (xem doc-comment của entity).
            //
            // KHÔNG đặt HasDefaultValue — cùng lối Notification.Type và Ticket.Status: tầng service
            // luôn ghi giá trị tường minh, nên một DEFAULT ở tầng CSDL chỉ tạo thêm một đường ghi ra
            // dòng mới mà không ai chủ ý.
            e.Property(t => t.Type).HasConversion<string>().HasMaxLength(30).IsRequired();

            // 200 là trần của Notification.Title, không phải một con số chọn tuỳ. Xem entity.
            e.Property(t => t.Title).HasMaxLength(200).IsRequired();

            // Body cố ý KHÔNG đặt HasMaxLength: để Npgsql map thành text, khớp Notification.Body —
            // cùng lối CancellationRequest.Reason.

            // IsActive không cần cấu hình: bool đã ra `boolean NOT NULL`, và giá trị mặc định true
            // nằm ở entity (C#) chứ không ở CSDL. Cố ý không HasDefaultValue — lý do như trên.

            // 🔴 KHOÁ NGHIỆP VỤ của bảng: mỗi NotificationType tối đa MỘT mẫu.
            //
            // Vì sao phải chặn ở tầng CSDL chứ không để tầng service kiểm: khi một loại có hai mẫu thì
            // "lấy mẫu nào" không còn câu trả lời đúng — service sẽ lấy theo thứ tự dòng, tức là nội
            // dung thông báo khách nhận phụ thuộc vào thứ tự bản ghi trong CSDL. Lỗi đó chỉ lộ ra khi
            // có người tạo mẫu thứ hai và rất khó lần, nên chặn ngay lúc tạo là rẻ nhất.
            //
            // Chỉ mục này cũng phục vụ luôn câu tra cứu DUY NHẤT của bảng: "mẫu đang bật của loại T"
            // — một phép tra theo đúng cột này.
            e.HasIndex(t => t.Type).IsUnique();
        });
    }
}
