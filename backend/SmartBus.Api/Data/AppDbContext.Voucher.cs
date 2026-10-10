using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Data;

/// <summary>
/// Phần DbContext của nhóm nghiệp vụ Voucher (US 18 — Sprint 3): bảng <c>Vouchers</c> +
/// <c>VoucherUsages</c>. Task *"API kiểm tra và áp dụng voucher vào đơn hàng"* (dòng 52 + 53) —
/// Nguyễn Duy Kiên; Vàng Thị Dăm đã cho phép dựng bảng, xem comment ở <see cref="ConfigureVoucher"/>.
///
/// Hình dạng bảng khớp mục "Voucher — /vouchers" của docs/api-contract.md. Muốn đổi hình dạng thì
/// sửa hợp đồng TRƯỚC rồi báo người còn lại (luật 5).
///
/// ⚠️ VIỆC CÒN LẠI LÀ CỦA VÀNG THỊ DĂM — đúng MỘT lệnh, chạy trong backend/SmartBus.Api:
///     dotnet ef migrations add Sprint3_Vouchers_VoucherUsages
/// rồi soát file migration theo checklist ở docs/27-huong-dan-migrate-vouchers.md. Entity + cấu hình
/// dưới đây đã khớp hợp đồng — KHÔNG sửa hình dạng entity/cột, chỉ sinh migration. Test tích hợp
/// của dự án chạy trên EF InMemory nên không chờ migration.
///
/// Hook <c>ConfigureVoucher</c> đã khai báo sẵn trong Data/AppDbContext.cs (2 dòng, cùng nhánh này)
/// — file đó của Dăm, nhờ Dăm review trong PR.
/// </summary>
public partial class AppDbContext
{
    public DbSet<Voucher> Vouchers => Set<Voucher>();

    public DbSet<VoucherUsage> VoucherUsages => Set<VoucherUsage>();

    partial void ConfigureVoucher(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Voucher>(e =>
        {
            // Mã khách gõ: duy nhất toàn hệ thống. Service chuẩn hoá Trim().ToUpperInvariant() trên
            // MỌI đường đọc/ghi, nên unique index thường ở đây là đủ cho luồng đi qua API.
            //
            // ⚠️ CHỌN LỰA ĐÃ CÂN NHẮC: unique index của PostgreSQL PHÂN BIỆT hoa thường, nên về mặt
            // CSDL "summer10" và "SUMMER10" là hai mã khác nhau — ghi thẳng xuống bảng bằng chữ
            // thường vẫn lọt. Ba đường đã xét:
            //   (1) chuẩn hoá HOA ở tầng service + unique index thường  ← ĐANG DÙNG
            //   (2) index trên upper("Code") — triệt để, nhưng HasIndex không diễn đạt được, phải
            //       viết SQL tay trong migration của Dăm (một đối tượng schema không test nào phủ)
            //   (3) kiểu citext — cần extension PostgreSQL mà dự án chưa dùng ở đâu, là quyết định
            //       của cả schema chứ không phải của một tính năng
            // Bản nháp FE (voucherApi.ts) cũng đã chuẩn hoá HOA ở cả create lẫn update, nên (1) giữ
            // được một hình dạng chuẩn duy nhất cho toàn hệ thống. Dăm cân nhắc (2) lúc sinh
            // migration — nếu chọn thì checklist trong docs/27 phải thêm một dòng.
            e.Property(v => v.Code).HasMaxLength(20).IsRequired();
            e.HasIndex(v => v.Code).IsUnique();

            e.Property(v => v.Name).HasMaxLength(200).IsRequired();

            // Kiểu giảm giá và trạng thái là chuỗi đọc được trong CSDL (A3) — "Percent"/"Active",
            // không phải số. Default ở tầng CSDL phải là giá trị enum HỢP LỆ (cùng lối
            // AppDbContext.Seat.cs): để mặc định thì EF sinh DEFAULT '' và dòng nào lỡ dính default
            // đó sẽ làm EF ném lỗi ngay khi đọc.
            e.Property(v => v.DiscountType).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(v => v.Status)
             .HasConversion<string>()
             .HasMaxLength(20)
             .IsRequired()
             .HasDefaultValue(VoucherStatus.Active);

            // Tiền là numeric(12,2) (A3) — KHÔNG float/double, sai số dấu phẩy động trên tiền là lỗi
            // không sửa được. Cùng độ rộng với Fares.Price và Payments.Amount.
            e.Property(v => v.DiscountValue).HasPrecision(12, 2);
            e.Property(v => v.MinOrderValue).HasPrecision(12, 2);
            e.Property(v => v.MaxDiscount).HasPrecision(12, 2);

            // Voucher mới chưa ai dùng — 0 là giá trị đúng, không phải "chưa biết".
            e.Property(v => v.UsedCount).HasDefaultValue(0);

            // Đường quét "mã còn dùng được không" của service kiểm tra: bật/tắt + còn trong khoảng
            // hiệu lực. Một index phủ đúng bộ ba đó.
            e.HasIndex(v => new { v.Status, v.ValidFrom, v.ValidUntil });

            // Điều kiện tuyến của dòng 53 — tra "tuyến này có mã nào không".
            e.HasIndex(v => v.RouteId);

            // FK nghiệp vụ → Restrict tường minh (A5): xoá một tuyến KHÔNG được kéo theo voucher.
            // Nullable vì phần lớn voucher áp cho mọi tuyến — xem Voucher.RouteId.
            e.HasOne(v => v.Route)
             .WithMany()
             .HasForeignKey(v => v.RouteId)
             .OnDelete(DeleteBehavior.Restrict);

            // 🔴 Chốt chống tiêu thụ QUÁ quantity khi hai khách cùng áp lượt cuối. Cùng đúng một
            // dòng, cùng đúng cơ chế mà AppDbContext.Payment.cs đã dùng cho nhánh đua callback trùng
            // (task dòng 33): hai lượt cùng đọc usedCount = quantity - 1 rồi cùng ghi — EF thêm
            // "WHERE xmin = ..." vào câu UPDATE, lượt thua nhận DbUpdateConcurrencyException và
            // service đổi thành 409. xmin là cột HỆ THỐNG của PostgreSQL, KHÔNG sinh cột mới trong
            // migration.
            //
            // ⚠️ Provider InMemory BỎ QUA concurrency token — bộ test của dự án KHÔNG chứng minh
            // được chốt này, đúng như mục "Giữ chỗ" của hợp đồng đã ghi cho partial unique index
            // chống trùng ghế. Đừng đọc "test xanh" thành "đã chống được đua".
            e.Property<uint>("xmin").HasColumnName("xmin").IsRowVersion();
        });

        modelBuilder.Entity<VoucherUsage>(e =>
        {
            // Mã giao dịch đã tiêu thụ voucher — độ dài khớp Payments.PaymentCode (cùng 64) dù hai
            // bảng chưa nối FK, để ngày nối FK không phải sửa kiểu cột.
            e.Property(u => u.PaymentCode).HasMaxLength(64).IsRequired();

            // 🔴 CHỐT IDEMPOTENCY của phần "áp dụng". Callback cổng gửi lại (MoMo retry tới khi nhận
            // 204) hay job đối soát chạy đè đều gọi RedeemAsync lần hai cho CÙNG giao dịch; unique
            // index này là thứ chặn cứng lượt tiêu thụ thứ hai, không phụ thuộc việc service có nhớ
            // kiểm hay không. Đây cũng là chốt cho luật "một đơn một mã" — hợp đồng chưa chốt có
            // cho nhiều mã một đơn hay không, và index này đang chặn cứng điều đó (xem "Câu hỏi mở").
            e.HasIndex(u => u.PaymentCode).IsUnique();

            // Tiền — cùng numeric(12,2) với Vouchers, A3.
            e.Property(u => u.OrderAmount).HasPrecision(12, 2);
            e.Property(u => u.DiscountAmount).HasPrecision(12, 2);

            // Đếm lượt dùng của một voucher theo thời gian — nền của API thống kê hiệu quả voucher
            // (dòng 54): đếm lượt, cộng tiền giảm, lọc theo khoảng ngày. Cùng lối
            // FeedbackReplies (FeedbackId, CreatedAt).
            e.HasIndex(u => new { u.VoucherId, u.CreatedAt });

            // "Voucher tôi đã dùng" và — khi nhóm chốt luật "mỗi khách một lượt" — điều kiện EXISTS
            // theo người dùng. Ghi sẵn index để thêm luật đó không phải sinh migration nữa.
            e.HasIndex(u => u.UserId);

            // FK nghiệp vụ → Restrict tường minh (A5): xoá một voucher hay một tài khoản KHÔNG được
            // kéo theo dấu vết tiền đã giảm.
            e.HasOne(u => u.Voucher)
             .WithMany(v => v.Usages)
             .HasForeignKey(u => u.VoucherId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(u => u.User)
             .WithMany()
             .HasForeignKey(u => u.UserId)
             .OnDelete(DeleteBehavior.Restrict);

            // Không cấu hình UpdatedAt: bảng chỉ ghi thêm nên entity không có cột đó.
        });
    }
}
