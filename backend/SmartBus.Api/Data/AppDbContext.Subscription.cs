using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Data;

/// <summary>
/// Phần DbContext của nhóm nghiệp vụ Thông báo — bảng <c>Subscriptions</c> (ĐĂNG KÝ THEO DÕI thông báo
/// theo tuyến / trạm). Cùng task với <c>AppDbContext.NotificationTemplate.cs</c>: *"Migrate bảng
/// Notifications, NotificationTemplates, Subscriptions"*, bảng phân công **Sprint 5, dòng 14** (Vàng
/// Thị Dăm).
///
/// Hai bảng nằm hai file chứ không gộp một file như <c>Refunds + RefundLogs</c>
/// (<c>AppDbContext.Refund.cs</c>): chúng không có quan hệ cha–con, không bảng nào là nhật ký của bảng
/// kia, và người tiêu thụ cũng khác nhau (mẫu → màn quản lý mẫu dòng 24; lượt theo dõi → API dòng 17
/// và hai service dòng 15/29). Chi tiết: <c>docs/37-csdl-mau-thong-bao-va-theo-doi.md</c>.
/// </summary>
public partial class AppDbContext
{
    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    partial void ConfigureSubscription(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Subscription>(e =>
        {
            // "Danh sách theo dõi của tôi" — câu của màn dòng 20 (component bật/tắt theo trạm) và của
            // API dòng 17. Chỉ mục trên MỘT cột UserId (không phải (UserId, RouteId)) vì màn đó hiển
            // thị mọi lượt theo dõi của khách — cả lượt theo trạm lẫn lượt theo cả tuyến — rồi mới
            // nhóm theo tuyến ở tầng ứng dụng.
            //
            // Chỉ mục này cũng phủ luôn khoá ngoại UserId: EF thấy cột đã có chỉ mục nên không sinh
            // thêm chỉ mục FK trùng — cùng lối đã ghi ở AppDbContext.Notification.cs.
            e.HasIndex(s => s.UserId);

            // 🔴 HAI CHỈ MỤC DUY NHẤT BỘ PHẬN, không phải một chỉ mục duy nhất thường.
            //
            // Ý định là "một khách chỉ theo dõi một tuyến/trạm đúng MỘT lần" — hai dòng trùng nhau
            // nghĩa là khách nhận HAI thông báo y hệt cho cùng một lượt xe, một lỗi nhìn thấy được.
            //
            // Nhưng viết một chỉ mục duy nhất trên (UserId, RouteId, StopId) là SAI: trong PostgreSQL
            // hai giá trị NULL được coi là KHÁC nhau, nên (khách A, tuyến 01, NULL) chèn được bao
            // nhiêu lần cũng được — đúng cái ca "theo dõi cả tuyến" mà dòng 29 cần. Ràng buộc sẽ
            // không chặn được gì ở đúng chỗ dễ trùng nhất, mà lại im lặng: không có lỗi nào để lần.
            //
            // Nên tách làm hai, mỗi bên một điều kiện — cùng lối partial index mà A6 đã dùng cho phép
            // chống bán trùng ghế:
            //
            //   · StopId IS NULL      → "theo dõi cả tuyến", mỗi (khách, tuyến) một dòng
            //   · StopId IS NOT NULL  → "theo dõi một trạm", mỗi (khách, tuyến, trạm) một dòng
            e.HasIndex(s => new { s.UserId, s.RouteId })
             .IsUnique()
             .HasFilter("\"StopId\" IS NULL");

            e.HasIndex(s => new { s.UserId, s.RouteId, s.StopId })
             .IsUnique()
             .HasFilter("\"StopId\" IS NOT NULL");

            // Khoá ngoại RouteId và StopId: KHÔNG khai chỉ mục ở đây — EF tự sinh một chỉ mục cho mỗi
            // khoá ngoại chưa được phủ, và cả hai đều cần thật:
            //   · IX_Subscriptions_RouteId  → câu của dòng 29, "ai đang theo dõi tuyến này"
            //   · IX_Subscriptions_StopId   → câu của dòng 15, "ai đang theo dõi trạm này"
            // Hai chỉ mục duy nhất ở trên không phủ được: RouteId và StopId đều không đứng ĐẦU danh
            // sách cột của chúng (UserId mới đứng đầu).

            // Ba khoá ngoại nghiệp vụ → Restrict tường minh (A5). Mặc định của EF là Cascade, và ở
            // đây hậu quả nặng hơn bình thường: xoá một tuyến mà cascade thì **âm thầm xoá hết lượt
            // theo dõi của mọi khách trên tuyến đó** — khách không nhận được thông báo nữa mà không ai
            // biết vì sao. Với Users và Stops cũng cùng lý do.
            //
            // WithMany() rỗng ở cả ba: User.cs là file của Hiếu, Route.cs/Stop.cs không cần collection
            // nghịch — cùng lối Trip.CurrentStop ở AppDbContext.Trip.cs.
            e.HasOne(s => s.User)
             .WithMany()
             .HasForeignKey(s => s.UserId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(s => s.Route)
             .WithMany()
             .HasForeignKey(s => s.RouteId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(s => s.Stop)
             .WithMany()
             .HasForeignKey(s => s.StopId)
             .OnDelete(DeleteBehavior.Restrict);

            // ⚠️ Cố ý KHÔNG có khoá phiên bản dòng (xmin) như CancellationRequests/TicketChanges: bảng
            // chỉ ghi thêm rồi xoá (A4), không có nhánh SỬA cùng một dòng để mà chặn đua. Hai lượt
            // đăng ký trùng chạy song song thì lượt thua nhận lỗi chỉ mục duy nhất ở trên — đúng thứ
            // cần, và ở tầng CSDL chứ không phải ở tầng ứng dụng.
        });
    }
}
