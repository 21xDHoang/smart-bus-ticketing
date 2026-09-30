using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Data;

/// <summary>
/// Phần DbContext của nhóm nghiệp vụ Ưu đãi (DiscountRequests).
/// Vàng Thị Dăm sửa file này — chủ CSDL của nhóm.
///
/// Task migrate của story 17 trong Product_Backlog ghi "Migrate bảng PriorityGroups,
/// PriorityApprovals" — HAI bảng. Quy ước A9 #13 đóng băng danh sách bảng lại chỉ có MỘT bảng
/// tên DiscountRequests, kèm ghi chú "bảng riêng, không thêm cột vào Users". Đã theo A9:
/// A9 là danh sách đã chốt, còn luật 2.4 cấm tự thêm bảng ngoài A9. Việc lệch tên này đã báo
/// nhóm trong PR — xem mô tả PR.
///
/// Vì sao gộp được hai bảng backlog thành một: "PriorityGroups" (danh mục nhóm ưu đãi) không có
/// gì để lưu ngoài chính đối tượng ưu đãi, mà đối tượng ưu đãi đã có sẵn enum PassengerType
/// dùng chung với bảng Fares. Tách bảng danh mục cho một enum là thêm bảng mà không story nào
/// trả lời (cùng lối lập luận với A8.3 — không tách bảng Schedule).
/// </summary>
public partial class AppDbContext
{
    public DbSet<DiscountRequest> DiscountRequests => Set<DiscountRequest>();

    partial void ConfigureDiscountRequest(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DiscountRequest>(e =>
        {
            // Hai enum đều lưu dạng chuỗi, không phải số nguyên (A3).
            e.Property(d => d.PassengerType).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(d => d.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

            // EvidenceUrl và RejectReason cố ý KHÔNG đặt HasMaxLength: để Npgsql map thành text.
            // Chúng là chuỗi dài (A3) — URL ảnh và lý do từ chối không có độ dài trần hợp lý.

            // "Hồ sơ ưu đãi của tôi". Cũng là chỉ mục cho khoá ngoại UserId — EF gộp hai khai báo
            // này làm một nên không sinh chỉ mục trùng.
            e.HasIndex(d => d.UserId);

            // Hàng đợi duyệt của HR: "các hồ sơ đang chờ, cũ nhất trước". Status đứng đầu vì
            // Pending là số ít so với Approved/Rejected, CreatedAt đứng sau để sắp được luôn.
            e.HasIndex(d => new { d.Status, d.CreatedAt });

            // Job quét thẻ sắp hết hạn: "hồ sơ đã duyệt và ExpiresAt sắp tới". Cũng phục vụ luôn
            // truy vấn thống kê số lượng hồ sơ theo trạng thái vì Status là cột đầu.
            e.HasIndex(d => new { d.Status, d.ExpiresAt });

            // Người nộp hồ sơ bị xoá không được kéo theo hồ sơ (A5). Users không nằm trong danh
            // sách ngoại lệ Cascade của A5 nên theo mặc định: Restrict. Hệ quả chấp nhận được —
            // hệ thống "xoá" tài khoản bằng User.IsActive chứ không xoá cứng (A4).
            //
            // WithMany() rỗng cho CẢ HAI khoá ngoại: không thêm collection nghịch vào User.cs
            // (file của Hiếu), và Trip.Driver ở AppDbContext.Trip.cs đã đi trước đúng lối này.
            e.HasOne(d => d.User)
             .WithMany()
             .HasForeignKey(d => d.UserId)
             .OnDelete(DeleteBehavior.Restrict);

            // Người duyệt cũng là một hàng Users, nên đây là khoá ngoại thứ hai cùng trỏ Users.
            // Postgres và EF đều xử lý bình thường vì hai navigation có tên khác nhau
            // (User vs ReviewedBy) và đều khai báo tường minh — không dựa vào quy ước.
            //
            // Ngoại lệ của Restrict ở đây: hồ sơ đã duyệt vẫn giữ nguyên người duyệt kể cả khi
            // tài khoản đó bị khoá, vì đó là dấu vết nghiệp vụ.
            e.HasOne(d => d.ReviewedBy)
             .WithMany()
             .HasForeignKey(d => d.ReviewedByUserId)
             .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
