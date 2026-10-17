using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Data;

/// <summary>
/// Phần DbContext của nhóm "xuất file bất đồng bộ": bảng <c>ExportJobs</c> — Vàng Thị Dăm sửa file
/// này (chủ CSDL của nhóm).
///
/// Bảng thuộc task *"Xử lý xuất file lớn bất đồng bộ + thông báo khi xong"* (Sprint 5 dòng 64).
/// Không có quan hệ nghiệp vụ nào ngoài FK tới <c>Users</c>: job là yêu cầu của một người, không
/// sở hữu dữ liệu báo cáo.
/// </summary>
public partial class AppDbContext
{
    public DbSet<ExportJob> ExportJobs => Set<ExportJob>();

    partial void ConfigureExportJob(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ExportJob>(e =>
        {
            // Ba enum lưu chuỗi (A3). Độ dài rộng hơn giá trị dài nhất một khoảng: dài nhất hiện
            // tại là "Processing" (10) và "AuditLogs" (9), còn 20/30 để giá trị hợp lệ kế tiếp
            // không vỡ ở tầng CSDL.
            e.Property(j => j.ReportType).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(j => j.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

            // Format giữ chuỗi "xlsx"/"pdf" — không phải enum của entity, xem doc-comment.
            e.Property(j => j.Format).HasMaxLength(10).IsRequired();

            e.Property(j => j.RequestedByName).HasMaxLength(200).IsRequired();

            // JSON bộ lọc: text, không đặt trần độ dài. Bộ lọc là một object nhỏ cố định; đặt
            // HasMaxLength ở đây chỉ tạo thêm một chỗ có thể vỡ khi thêm tham số báo cáo mới.
            e.Property(j => j.ParametersJson).IsRequired();

            e.Property(j => j.FileName).HasMaxLength(200);
            e.Property(j => j.ContentType).HasMaxLength(100);

            // byte[] mặc định map sang bytea trên Npgsql — không cần HasColumnType, và provider
            // InMemory của test cũng hiểu kiểu này.

            e.Property(j => j.ErrorMessage).HasMaxLength(1000);

            // Câu truy vấn 1 — "job của tôi, mới nhất trước": lọc UserId rồi sắp CreatedAt. Cũng
            // phục vụ luôn FK UserId nên EF không sinh thêm chỉ mục FK trùng.
            e.HasIndex(j => new { j.UserId, j.CreatedAt });

            // Câu truy vấn 2 — job nền nhặt việc: lọc Status = Pending rồi sắp CreatedAt. Không
            // có chỉ mục này thì mỗi nhịp quét là một lượt đọc toàn bảng.
            e.HasIndex(j => new { j.Status, j.CreatedAt });

            // Câu truy vấn 3 — dọn job hết hạn: lọc ExpiresAt <= now. Chỉ mục riêng vì cột này
            // không nằm trong hai chỉ mục trên.
            e.HasIndex(j => j.ExpiresAt);

            // FK nghiệp vụ → Restrict tường minh (A5). Xoá một tài khoản không được kéo theo lịch
            // sử yêu cầu xuất của họ: đó là dấu vết "ai đã xuất báo cáo gì, lúc nào", đúng loại
            // dữ liệu cần khi kiểm toán. WithMany() rỗng — không thêm collection nghịch vào User.
            e.HasOne(j => j.User)
             .WithMany()
             .HasForeignKey(j => j.UserId)
             .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
