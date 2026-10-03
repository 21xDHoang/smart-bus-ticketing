using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Data;

/// <summary>
/// Phần DbContext của nhóm nghiệp vụ Phản ánh (US 24): bảng <c>Feedbacks</c> + <c>FeedbackReplies</c>.
/// Tách file theo đúng lối AppDbContext.*.cs của dự án (E2 — người phụ trách nghiệp vụ sửa file của mình).
///
/// ⚠️ VIỆC CÒN LẠI LÀ CỦA VÀNG THỊ DĂM — đúng MỘT lệnh, chạy trong backend/SmartBus.Api:
///     dotnet ef migrations add Sprint2_Feedbacks_FeedbackReplies
/// rồi soát file migration theo checklist ở docs/24-huong-dan-migrate-feedbacks.md và commit/PR như
/// các migration trước. Entity + cấu hình dưới đây đã khớp hợp đồng "Phản ánh — /feedbacks" của
/// docs/api-contract.md — KHÔNG sửa hình dạng entity/cột, chỉ sinh migration.
///
/// Hook <c>ConfigureFeedback</c> đã khai báo sẵn trong Data/AppDbContext.cs (2 dòng, cùng PR này).
/// </summary>
public partial class AppDbContext
{
    public DbSet<Feedback> Feedbacks => Set<Feedback>();

    public DbSet<FeedbackReply> FeedbackReplies => Set<FeedbackReply>();

    partial void ConfigureFeedback(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Feedback>(feedback =>
        {
            // Trạng thái và loại là chuỗi đọc được trong CSDL (A3) — "New"/"Complaint", không phải số.
            feedback.Property(f => f.Type)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();

            feedback.Property(f => f.Status)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();

            // Content/AttachmentUrl để kiểu text mặc định (không HasMaxLength): nội dung phản ánh
            // dài tuỳ ý; hợp đồng chặn độ dài ở tầng API, không phải ở cột.

            // Hàng đợi xử lý của màn hình Admin: lọc theo trạng thái rồi sắp mới nhất trước
            // (GET /admin/feedbacks) — một index phủ đúng cặp đó.
            feedback.HasIndex(f => new { f.Status, f.CreatedAt });

            // "Phản ánh của tôi" của hành khách (task Nguyễn Duy Kiên) và tra theo chuyến.
            feedback.HasIndex(f => f.UserId);
            feedback.HasIndex(f => f.TripId);

            // FK nghiệp vụ → Restrict tường minh (A5): xoá người dùng hay chuyến KHÔNG được kéo
            // theo phản ánh — cùng lối mọi FK nghiệp vụ khác của dự án.
            feedback.HasOne(f => f.User)
                .WithMany()
                .HasForeignKey(f => f.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // TripId nullable: Restrict vẫn đúng — xoá chuyến bị chặn nếu còn phản ánh trỏ tới.
            feedback.HasOne(f => f.Trip)
                .WithMany()
                .HasForeignKey(f => f.TripId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FeedbackReply>(reply =>
        {
            // Cặp (phản ánh, thời điểm) đúng thứ tự đọc của màn hình chi tiết: phản hồi của một
            // phản ánh, cũ → mới.
            reply.HasIndex(r => new { r.FeedbackId, r.CreatedAt });
            reply.HasIndex(r => r.UserId);

            reply.HasOne(r => r.Feedback)
                .WithMany(f => f.Replies)
                .HasForeignKey(r => r.FeedbackId)
                .OnDelete(DeleteBehavior.Restrict);

            reply.HasOne(r => r.User)
                .WithMany()
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Không cấu hình UpdatedAt: bảng chỉ ghi thêm nên entity không có cột đó.
        });
    }
}
