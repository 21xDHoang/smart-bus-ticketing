using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Data;

/// <summary>
/// Phần DbContext của nhóm nghiệp vụ Vé tháng (MonthlyPasses, PassTypes).
/// Vàng Thị Dăm sửa file này — chủ CSDL của nhóm.
///
/// Xem <see cref="PassType"/> để biết vì sao có bảng PassTypes dù A9 không liệt kê nó, và
/// <see cref="MonthlyPassStatus"/> để biết vì sao ở đây CÓ Expired còn DiscountRequestStatus thì không.
/// </summary>
public partial class AppDbContext
{
    public DbSet<MonthlyPass> MonthlyPasses => Set<MonthlyPass>();

    public DbSet<PassType> PassTypes => Set<PassType>();

    partial void ConfigureMonthlyPass(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PassType>(e =>
        {
            e.Property(p => p.Code).HasMaxLength(20).IsRequired();
            e.Property(p => p.Name).HasMaxLength(100).IsRequired();

            // numeric(12,2) — cùng đơn vị và cùng độ rộng với Fares.Price và MonthlyPass.Price.
            // Không dùng float/double cho tiền (A3).
            e.Property(p => p.Price).HasPrecision(12, 2);

            // Mã loại vé là khoá nghiệp vụ: POST /monthly-passes nhận đúng chuỗi này, nên hai
            // loại vé trùng mã là hợp đồng API mơ hồ. Chặn ở tầng CSDL (A6 — cùng lối Routes.Code).
            e.HasIndex(p => p.Code).IsUnique();

            // Không chỉ mục nào khác: bảng danh mục cỡ vài dòng, Postgres đọc cả bảng còn rẻ hơn
            // đi qua chỉ mục. Chỉ mục cho khoá ngoại nằm ở phía MonthlyPasses.
        });

        modelBuilder.Entity<MonthlyPass>(e =>
        {
            e.Property(m => m.Code).HasMaxLength(40).IsRequired();
            e.Property(m => m.Price).HasPrecision(12, 2);

            // Trạng thái lưu dạng chuỗi, không phải số nguyên (A3).
            e.Property(m => m.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

            // Mã QR — tra cứu lúc soát vé và là khoá nghiệp vụ, phải unique (A6).
            e.HasIndex(m => m.Code).IsUnique();

            // "Vé tháng đang hoạt động của tôi" (GET /monthly-passes/me) và câu hỏi lúc soát vé:
            // WHERE UserId = @u AND Status = 'Active' AND ValidFrom <= now AND ValidTo >= now.
            // UserId đứng đầu vì luôn biết trước; Status đứng sau để loại vé hết hạn ngay trong
            // chỉ mục; ValidTo đứng cuối để so được bằng range scan.
            //
            // Chỉ mục này cũng phục vụ luôn khoá ngoại UserId (UserId là cột tiền tố), nên EF
            // không sinh thêm chỉ mục FK riêng cho UserId — có thêm là chỉ mục thừa.
            e.HasIndex(m => new { m.UserId, m.Status, m.ValidTo });

            // Job quét vé hết hạn (BackgroundService của story 16): "các vé còn Active mà ValidTo
            // đã qua". Cùng khuôn với (Status, ExpiresAt) của DiscountRequests.
            e.HasIndex(m => new { m.Status, m.ValidTo });

            // 🔴 KHÔNG đặt unique trên (UserId, RouteId) dù task "Validate trùng vé tháng đang
            // hoạt động trên cùng tuyến" nghe như cần — cố ý, và đây là chỗ dễ đặt sai nhất.
            //
            // Partial unique index kiểu IX_Tickets_TripId_SeatId_Active của A6 chỉ diễn đạt được
            // "Status = 'Active'", mà điều kiện thật của "đang hoạt động" là
            // ValidFrom <= now() <= ValidTo. now() không phải hàm immutable nên Postgres KHÔNG
            // cho đưa vào điều kiện của partial index — không có cách nào khoá được ràng buộc này
            // ở tầng CSDL.
            //
            // Đặt unique trên (UserId, RouteId) sẽ SAI NGHIÊM TRỌNG: gia hạn ghi dòng mới với
            // ValidFrom = ValidTo của vé cũ, tức vé kỳ sau đã Active ngay từ lúc đăng ký trong khi
            // vé kỳ trước còn Active — hai dòng cùng (UserId, RouteId) là hợp lệ. Ràng buộc unique
            // sẽ chặn đúng nghiệp vụ gia hạn, tức tính năng chính của story 16.
            //
            // Vì vậy việc này thuộc tầng service (task của Trần Trung Hiếu), và service phải so
            // khoảng ValidFrom/ValidTo chồng lấn chứ không so Status.
            //
            // Khoá ngoại RouteId và PassTypeId không khai chỉ mục ở đây: chúng không phải cột tiền
            // tố của chỉ mục nào, nên quy ước ForeignKeyIndexConvention của EF tự sinh chỉ mục một
            // cột cho mỗi khoá — đúng thứ cần cho việc kiểm tra khoá ngoại khi xoá (A5, Restrict).

            // Xoá hành khách không được kéo theo vé tháng — Users không nằm trong danh sách ngoại
            // lệ Cascade của A5. Hệ thống "xoá" tài khoản bằng User.IsActive chứ không xoá cứng (A4).
            //
            // WithMany() rỗng cho cả ba khoá ngoại: không thêm collection nghịch vào User.cs và
            // Route.cs (file của người khác), đúng lối Trip.Driver và DiscountRequest.User đã đi trước.
            e.HasOne(m => m.User)
             .WithMany()
             .HasForeignKey(m => m.UserId)
             .OnDelete(DeleteBehavior.Restrict);

            // Xoá một tuyến đang có người giữ vé tháng phải bị CHẶN, không được âm thầm xoá vé đã
            // bán — đó là mất dữ liệu doanh thu, cùng lập luận với RouteStops.Stop ở A5.
            e.HasOne(m => m.Route)
             .WithMany()
             .HasForeignKey(m => m.RouteId)
             .OnDelete(DeleteBehavior.Restrict);

            // Xoá một loại vé đã từng bán cũng phải chặn, nếu không giá đã chụp trong Price mất
            // ngữ cảnh và không biết vé đó là loại gì.
            e.HasOne(m => m.PassType)
             .WithMany()
             .HasForeignKey(m => m.PassTypeId)
             .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
