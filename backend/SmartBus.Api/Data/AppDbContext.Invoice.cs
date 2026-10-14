using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Data;

/// <summary>
/// Phần DbContext của nhóm nghiệp vụ Thanh toán — bảng <c>Invoices</c> (HOÁ ĐƠN ĐIỆN TỬ). Tách khỏi
/// <c>AppDbContext.Payment.cs</c> dù cùng nhóm và có khoá ngoại sang <c>Payments</c>: bảng kia là
/// <b>dòng tiền</b> (ghi một lần, không sửa nội dung), bảng này là <b>chứng từ phát hành cho khách</b>
/// (có sinh file, có gửi email, có gửi lại) — hai vòng đời khác nhau, cùng lối tách đã dùng giữa
/// <c>AppDbContext.Notification.cs</c> và <c>AppDbContext.NotificationTemplate.cs</c>.
///
/// Dựng theo task *"Migrate bảng Invoices"* của bảng phân công **Sprint 4, dòng 27** (Vàng Thị Dăm),
/// phục vụ US 7. Bảng KHÔNG có trong A9 — lần thứ sáu việc "A9 lệch thực tế" được nhắc.
/// Chi tiết: <c>docs/38-csdl-hoa-don.md</c>.
/// </summary>
public partial class AppDbContext
{
    public DbSet<Invoice> Invoices => Set<Invoice>();

    partial void ConfigureInvoice(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Invoice>(e =>
        {
            // numeric(12,2) — cùng đơn vị và cùng độ rộng với Payments.Amount và Fares.Price (A3).
            // KHÔNG dùng float/double: sai số dấu phẩy động trên tiền là lỗi không sửa được.
            e.Property(i => i.Amount).HasPrecision(12, 2);

            // PdfUrl cố ý KHÔNG đặt HasMaxLength: để Npgsql map thành text, vì signed URL dài
            // 300-800+ ký tự. Cùng lối DiscountRequest.EvidenceUrl và CancellationRequest.Reason.

            // 🔴 CHỈ MỤC DUY NHẤT — "một giao dịch, một hoá đơn". Lý do đầy đủ ở doc-comment của
            // Invoice.PaymentId; tóm lại: cổng thanh toán gọi callback TRÙNG (cả bảng Payments được
            // dựng quanh giả định đó), nên không có khoá này thì một callback gửi lại sẽ phát hành
            // hoá đơn thứ hai cho cùng một lần trả tiền. Chỉ mục cũng phục vụ luôn đường tra của màn
            // Admin (dòng 35): PaymentCode → Payments → Invoices.
            //
            // Chặn ở tầng CSDL chứ không để tầng service kiểm: hai callback chạy song song thì tầng
            // service kiểu "đọc trước, ghi sau" vẫn lọt — cùng lý do chỉ mục duy nhất của
            // Payments.PaymentCode nằm ở CSDL.
            e.HasIndex(i => i.PaymentId).IsUnique();

            // "Danh sách hoá đơn của tôi" (dòng 32), mới nhất trước — cùng khuôn chỉ mục A6 đã chốt
            // cho Notifications: (UserId, CreatedAt). Chỉ mục này phủ luôn khoá ngoại UserId nên EF
            // không sinh thêm chỉ mục FK trùng.
            e.HasIndex(i => new { i.UserId, i.CreatedAt });

            // Ba khoá ngoại nghiệp vụ → Restrict tường minh (A5). Mặc định của EF là Cascade, và ở
            // đây hậu quả là mất chứng từ tài chính: xoá một lượt thanh toán mà cascade thì hoá đơn
            // biến mất theo, không còn gì trong CSDL nói nó từng tồn tại. Với Users cũng cùng lý do —
            // và hệ thống "xoá" tài khoản bằng User.IsActive chứ không xoá cứng (A4), nên Restrict
            // không chặn nhầm thao tác nào có thật.
            //
            // WithMany() rỗng ở cả hai: User.cs là file của Hiếu, Payment.cs không cần collection
            // nghịch — cùng lối Trip.CurrentStop ở AppDbContext.Trip.cs.
            e.HasOne(i => i.Payment)
             .WithMany()
             .HasForeignKey(i => i.PaymentId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(i => i.User)
             .WithMany()
             .HasForeignKey(i => i.UserId)
             .OnDelete(DeleteBehavior.Restrict);

            // ⚠️ Cố ý KHÔNG có khoá phiên bản dòng (xmin) như Payments: ở bảng kia xmin chặn nhánh
            // đua "hai luồng cùng LẬT một dòng" (IPN gửi lại, callback + job đối soát chạy trùng).
            // Ở đây nhánh đua duy nhất là "hai luồng cùng PHÁT HÀNH hoá đơn cho một giao dịch", và
            // chỉ mục duy nhất ở trên đã chặn: lượt thua nhận lỗi vi phạm khoá duy nhất. Còn nhánh
            // "hai lượt gửi lại email chạy trùng" thì cả hai cùng ghi SentAt — ghi đè lẫn nhau vô hại,
            // không có dữ liệu nào sai đi.
        });
    }
}
