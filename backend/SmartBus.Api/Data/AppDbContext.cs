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
        ConfigureTicket(modelBuilder);
        ConfigurePayment(modelBuilder);
        ConfigureVoucher(modelBuilder);
        ConfigureCancellationRequest(modelBuilder);
        ConfigureTicketChange(modelBuilder);
        ConfigureNotification(modelBuilder);
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

    // US 4 (Tickets — A9 #14, Vàng Thị Dăm): task "Migrate bảng Tickets, TicketQRCodes" của bảng
    // phân công Sprint 3. Dựng MỘT bảng: mã QR là cột Code của Tickets, không có bảng TicketQRCodes
    // riêng (A9 không có bảng đó, A6 đặt khoá duy nhất QrCode trên Tickets) — xem docs/28 §2.
    // Xem AppDbContext.Ticket.cs.
    partial void ConfigureTicket(ModelBuilder modelBuilder);

    // US 6 (Payments — dòng 27, Vàng Thị Dăm): dựng theo uỷ quyền 10/10/2026 cho task dòng 33
    // *"Xử lý idempotency: chống trừ tiền 2 lần khi callback trùng"* (Phùng Duy Hoàng).
    // Xem AppDbContext.Payment.cs.
    partial void ConfigurePayment(ModelBuilder modelBuilder);

    // US 18 (Vouchers + VoucherUsages — dòng 50, Vàng Thị Dăm): dựng theo uỷ quyền cho task dòng 52
    // *"API kiểm tra và áp dụng voucher vào đơn hàng"* (Nguyễn Duy Kiên). Migration còn thiếu — việc
    // của Dăm, xem docs/27-huong-dan-migrate-vouchers.md. Xem AppDbContext.Voucher.cs.
    partial void ConfigureVoucher(ModelBuilder modelBuilder);

    // US 5 (CancellationRequests — Sprint 4, Vàng Thị Dăm): task "Migrate bảng TicketChanges,
    // CancellationRequests" của bảng phân công Sprint 4 (dòng 2). Yêu cầu HUỶ VÉ —
    // bảng anh em TicketChanges ở ConfigureTicketChange ngay dưới, cố ý tách hai file theo lối "một
    // file một nghiệp vụ". A9 KHÔNG có hai bảng này, xem docs/32-csdl-huy-doi-ve.md §1.
    // Xem AppDbContext.CancellationRequest.cs.
    partial void ConfigureCancellationRequest(ModelBuilder modelBuilder);

    // US 5 (TicketChanges — Sprint 4, Vàng Thị Dăm): cùng task Sprint 4 dòng 2. Yêu cầu ĐỔI CHUYẾN;
    // duyệt một yêu cầu đổi thì HUỶ vé cũ rồi phát hành vé MỚI, không sửa vé cũ — lý do ở
    // docs/32-csdl-huy-doi-ve.md §2. Xem AppDbContext.TicketChange.cs.
    partial void ConfigureTicketChange(ModelBuilder modelBuilder);

    // US 10 (Notifications — A9 #15, Vàng Thị Dăm): dựng SỚM theo task Sprint 4 dòng 7 *"Ghi log và
    // thông báo kết quả xử lý cho hành khách"* — A8.5 chốt thông báo đi in-app qua SignalR + lưu bảng
    // này, nên task dòng 7 không chạy được nếu thiếu nó. Bảng này là việc của Sprint 5 theo bảng phân
    // công (dòng 14 *"Migrate bảng Notifications, NotificationTemplates, Subscriptions"*): nhánh này
    // kéo trước ĐÚNG MỘT bảng, hai bảng còn lại vẫn để nguyên ở Sprint 5.
    // Xem docs/33-thong-bao-ket-qua-huy-doi-ve.md §1. Xem AppDbContext.Notification.cs.
    partial void ConfigureNotification(ModelBuilder modelBuilder);
}
