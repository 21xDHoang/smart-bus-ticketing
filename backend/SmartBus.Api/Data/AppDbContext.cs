using Microsoft.EntityFrameworkCore;

namespace SmartBus.Api.Data;

/// <summary>
/// DbContext dùng chung — CHỈ Vàng Thị Dăm sửa file này (xem docs/01-kien-truc.md).
/// Nghiệp vụ tách sang các file partial để mỗi người sửa một file riêng:
/// AppDbContext.Auth.cs, AppDbContext.Route.cs, AppDbContext.Ticket.cs, ...
/// Thêm nhóm nghiệp vụ mới: khai báo thêm một dòng ConfigureXxx ở đây rồi nhắn Dăm.
/// </summary>
public partial class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ConfigureAuth(modelBuilder);
        ConfigureUserRoles(modelBuilder);
        ConfigureRoute(modelBuilder);
        ConfigureTrip(modelBuilder);
        ConfigureAuditLog(modelBuilder);
        ConfigureDiscountRequest(modelBuilder);
        ConfigureMonthlyPass(modelBuilder);
        ConfigureFeedback(modelBuilder);
        ConfigureSeat(modelBuilder);
        ConfigurePayment(modelBuilder);
    }

    partial void ConfigureAuth(ModelBuilder modelBuilder);

    partial void ConfigureUserRoles(ModelBuilder modelBuilder);

    partial void ConfigureRoute(ModelBuilder modelBuilder);

    partial void ConfigureTrip(ModelBuilder modelBuilder);

    partial void ConfigureAuditLog(ModelBuilder modelBuilder);

    partial void ConfigureDiscountRequest(ModelBuilder modelBuilder);

    partial void ConfigureMonthlyPass(ModelBuilder modelBuilder);

    // US 24 (Feedbacks + FeedbackReplies): entity + cấu hình đã có, migration còn thiếu —
    // việc của Vàng Thị Dăm, xem docs/24-huong-dan-migrate-feedbacks.md.
    partial void ConfigureFeedback(ModelBuilder modelBuilder);

    // US 2 + US 3 (SeatLayouts + Seats + SeatHolds): nhóm nghiệp vụ sơ đồ ghế & giữ chỗ — Sprint 3.
    // Cấu hình Seat chuyển từ AppDbContext.Trip.cs về AppDbContext.Seat.cs, mô hình EF không đổi.
    partial void ConfigureSeat(ModelBuilder modelBuilder);

    // US 6 (Payments — dòng 27, Vàng Thị Dăm): dựng theo uỷ quyền 10/10/2026 cho task dòng 33
    // *"Xử lý idempotency: chống trừ tiền 2 lần khi callback trùng"* (Phùng Duy Hoàng).
    // Xem AppDbContext.Payment.cs.
    partial void ConfigurePayment(ModelBuilder modelBuilder);
}
