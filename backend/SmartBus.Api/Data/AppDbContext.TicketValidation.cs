using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Data;

/// <summary>
/// Phần DbContext của nhóm nghiệp vụ Soát vé QR (US 15 — Sprint 4): bảng <c>TicketValidations</c>.
/// Vàng Thị Dăm sửa file này — chủ CSDL của nhóm.
///
/// Dựng theo task Sprint 4 *"Migrate bảng TicketValidations"* (Vàng Thị Dăm, dòng 14).
///
/// ⚠️ A9 KHÔNG có bảng này — xem ghi chú "ngoài A9" ở <c>docs/34-csdl-soat-ve-qr.md</c> §1. Đây là
/// bảng thứ ba của dự án nằm ngoài danh sách 20 bảng đóng băng (hai bảng trước:
/// <c>CancellationRequests</c> + <c>TicketChanges</c>, <c>docs/32</c> §1).
///
/// Bảng CHỈ GHI THÊM (A4): không có <c>UpdatedAt</c>, và cố ý không có khoá phiên bản dòng
/// <c>xmin</c> — cùng lý do đã ghi ở <c>AppDbContext.Notification.cs</c>: không có nhánh đua nào giữa
/// hai lượt SỬA cùng một dòng để mà chặn. Việc chống soát trùng (dòng 16) là chống hai lượt THÊM
/// khác nhau, và nó được chặn ở <c>Tickets.Status = 'Used'</c> chứ không ở bảng này.
/// </summary>
public partial class AppDbContext
{
    public DbSet<TicketValidation> TicketValidations => Set<TicketValidation>();

    partial void ConfigureTicketValidation(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TicketValidation>(e =>
        {
            // Nguyên văn mã QR đã quét — cùng độ dài với Tickets.Code vì đây đúng là chuỗi đó (A3:
            // chuỗi ngắn dùng varchar(n) có giới hạn, không dùng text).
            e.Property(v => v.ScannedCode).HasMaxLength(200).IsRequired();

            // Kết quả lưu chuỗi (A3). varchar(20): giá trị dài nhất hiện tại là "AlreadyUsed" (11 ký
            // tự), còn dư cho một lý do từ chối mới mà không phải đổi cột. Cùng lối Ticket.Status.
            // KHÔNG đặt HasDefaultValue — lượt soát nào cũng đã có kết quả ngay lúc ghi, nên một
            // DEFAULT ở tầng CSDL chỉ tạo thêm một đường ghi ra dòng mà không ai chủ ý. Xem ⚠️ ở
            // doc-comment TicketValidation.Result.
            e.Property(v => v.Result).HasConversion<string>().HasMaxLength(20).IsRequired();

            // "Vé này đã bị soát những lần nào" — câu hỏi của màn đối chiếu khi khách khiếu nại, và
            // của chính bước kiểm AlreadyUsed. Cũng phục vụ luôn khoá ngoại TicketId: EF thấy cột đã
            // có chỉ mục nên không sinh thêm chỉ mục FK trùng.
            e.HasIndex(v => v.TicketId);

            // Màn thống kê lượt soát theo chuyến (dòng 24, Dương Thị Hạnh): lọc theo TripId rồi sắp
            // theo thời gian quét. ScannedAt (giờ máy quét) chứ không phải CreatedAt: hàng đợi ngoại
            // tuyến đồng bộ muộn sẽ dồn cục nếu sắp theo giờ tới server — xem doc-comment
            // TicketValidation.ScannedAt.
            //
            // Cặp cột này cũng phục vụ khoá ngoại TripId (TripId là cột tiền tố).
            e.HasIndex(v => new { v.TripId, v.ScannedAt });

            // 🔴 Chống trùng khi hàng đợi ngoại tuyến đồng bộ lại (task dòng 18, Phùng Duy Hoàng).
            // Gửi lại một lượt soát là chuyện bình thường (mất mạng giữa chừng, app bị kill, bấm đồng
            // bộ hai lần); không có ràng buộc này thì mỗi lượt gửi lại đẻ thêm một dòng, màn thống kê
            // đếm khống, và một lượt soát gửi lại còn trông như bằng chứng gian lận của phụ xe.
            //
            // UNIQUE nhưng NULLABLE: lượt soát online không cần khoá này, và PostgreSQL cho phép nhiều
            // NULL trên chỉ mục UNIQUE nên các lượt online không chặn nhau. Đây là lý do phải là chỉ
            // mục UNIQUE thường, KHÔNG phải partial index — partial index ở đây sẽ không chặn được gì
            // thêm mà lại bỏ sót đúng ca cần chặn.
            e.HasIndex(v => v.ClientScanId).IsUnique();

            // ⚠️ Cố ý KHÔNG khai chỉ mục cho ValidatedByUserId và BoardingStopId: chưa có truy vấn nào
            // của Sprint 4 hỏi "tài khoản này soát bao nhiêu lượt" hay "trạm này có bao nhiêu khách
            // lên", nên EF tự sinh chỉ mục khoá ngoại là đủ. Đây là bảng ghi dày nhất dự án (mỗi lượt
            // quét một dòng), thêm chỉ mục là trả giá ghi cho MỌI lượt soát — chỉ thêm khi có câu truy
            // vấn thật. Ai cần thì nhắn Dăm.

            // FK nghiệp vụ → Restrict tường minh (A5). Mặc định của EF là Cascade, và ở đây hậu quả
            // đúng như cảnh báo đầu dự án: xoá một chuyến / một trạm / một vé / một tài khoản mà kéo
            // theo nhật ký soát vé là mất dấu vết kiểm toán — đúng thứ phải tra khi có tranh chấp về
            // một lượt soát. WithMany() rỗng: không thêm collection nghịch vào các entity kia.
            e.HasOne(v => v.Ticket)
             .WithMany()
             .HasForeignKey(v => v.TicketId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(v => v.Trip)
             .WithMany()
             .HasForeignKey(v => v.TripId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(v => v.ValidatedBy)
             .WithMany()
             .HasForeignKey(v => v.ValidatedByUserId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(v => v.BoardingStop)
             .WithMany()
             .HasForeignKey(v => v.BoardingStopId)
             .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
