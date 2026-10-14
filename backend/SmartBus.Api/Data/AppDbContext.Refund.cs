using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Data;

/// <summary>
/// Phần DbContext của nhóm nghiệp vụ Hoàn tiền (US 8 — "Hoàn tiền tự động", Sprint 4): hai bảng
/// <c>Refunds</c> và <c>RefundLogs</c>. Vàng Thị Dăm sửa file này — chủ CSDL của nhóm.
///
/// Dựng theo task Sprint 4 *"Migrate bảng Refunds, RefundLogs"* (Vàng Thị Dăm, dòng 38).
///
/// ⚠️ A9 KHÔNG có hai bảng này — xem ghi chú "ngoài A9" ở <c>docs/35-csdl-hoan-tien.md</c> §1. Đây
/// là nhóm bảng thứ tư của dự án nằm ngoài danh sách 20 bảng đóng băng, sau
/// <c>CancellationRequests</c> + <c>TicketChanges</c> (<c>docs/32</c> §1), <c>Notifications</c>
/// (<c>docs/33</c> §1) và <c>TicketValidations</c> (<c>docs/34</c> §1).
///
/// Hai bảng nằm chung MỘT file — khác lối tách file của <c>CancellationRequests</c> /
/// <c>TicketChanges</c> (hai file, vì hai bảng đó thuộc hai nửa khác nhau của US 5: huỷ vé và đổi
/// vé). Ở đây <c>RefundLogs</c> không có đời sống riêng: mọi dòng của nó là một lượt gọi cổng của
/// một dòng <c>Refunds</c>, không truy vấn nào chạm nó mà không bắt đầu từ khoản hoàn. Cùng lối
/// <c>SeatHolds</c> + <c>SeatHoldLogs</c> nằm chung <c>AppDbContext.Seat.cs</c>.
/// </summary>
public partial class AppDbContext
{
    public DbSet<Refund> Refunds => Set<Refund>();

    public DbSet<RefundLog> RefundLogs => Set<RefundLog>();

    partial void ConfigureRefund(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Refund>(e =>
        {
            // Mã khoản hoàn: unique, cùng độ dài Payments.PaymentCode (64) vì cùng vai trò — khoá
            // chống trùng lúc tạo và mã tra cứu bằng mắt.
            e.Property(r => r.RefundCode).HasMaxLength(64).IsRequired();
            e.HasIndex(r => r.RefundCode).IsUnique();

            // Nguồn và trạng thái lưu chuỗi (A3). KHÔNG đặt HasDefaultValue — theo đúng khuôn
            // Ticket.Status, SeatHold.Status, CancellationRequest.Status: service tạo khoản hoàn luôn
            // ghi 'Pending' tường minh, nên một DEFAULT ở tầng CSDL chỉ tạo thêm một đường ghi ra dòng
            // mới mà không ai chủ ý.
            //
            // varchar(30): giá trị dài nhất là "TicketCancellation" (18 ký tự) và "PaymentFailure"
            // (14). Chừa tới 30 vì cột này là thứ frontend đã chốt cứng (refundAdminApi.ts) — nếu
            // sau này phải thêm nguồn thứ ba thì thêm được mà không phải đổi kiểu cột. Trạng thái chỉ
            // 20: dài nhất hiện tại là "Succeeded" (9).
            e.Property(r => r.Source).HasConversion<string>().HasMaxLength(30).IsRequired();
            e.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

            // Tiền: numeric(12,2) — cùng đơn vị và cùng độ rộng với Tickets.Price, Payments.Amount,
            // CancellationRequests.CancellationFee. Không dùng float/double cho tiền (A3).
            e.Property(r => r.Amount).HasPrecision(12, 2);

            // Mã giao dịch phía cổng và lý do thất bại — chép nguyên độ dài của Payments
            // (GatewayTransactionId 50, Message 500) vì đây đúng là hai chuỗi cổng trả về.
            e.Property(r => r.GatewayTransactionId).HasMaxLength(50);
            e.Property(r => r.FailReason).HasMaxLength(500);

            // 🔴 Một giao dịch chỉ được có MỘT khoản hoàn CHƯA THẤT BẠI. Đây là chốt chống hoàn hai
            // lần ở tầng CSDL, cùng tinh thần chỉ mục điều kiện chống bán trùng ghế của A6 và chỉ mục
            // 'Pending' của CancellationRequests.
            //
            // Vì sao điều kiện là IN ('Pending', 'Succeeded') chứ không phải = 'Pending': một lượt
            // thử lại làm khoản hoàn rời khỏi Pending rồi có thể quay lại (Failed → Pending). Nếu điều
            // kiện chỉ bắt 'Pending', khe hở giữa hai lượt là đúng lúc dòng mang 'Failed' — và một
            // service gọi hai lần sẽ tạo được khoản hoàn thứ hai cho cùng giao dịch. Gộp 'Succeeded'
            // vào điều kiện thì sau khi tiền đã đi, KHÔNG khoản hoàn nào khác được tạo cho giao dịch
            // đó nữa, kể cả khi dòng cũ bị ai đó sửa hỏng.
            //
            // Vì sao là chỉ mục ĐIỀU KIỆN chứ không phải unique thường trên PaymentId: một giao dịch
            // hoàn toàn có thể có nhiều khoản hoàn theo thời gian — khoản đầu thất bại hết lịch thử
            // lại và bị bỏ, rồi phát sinh khoản mới. Unique thường sẽ chặn vĩnh viễn khoản thứ hai,
            // tức là chặn luôn con đường duy nhất để trả tiền cho khách trong ca đó.
            //
            // ⚠️ Khoản 'Failed' bị bỏ rồi tạo lại được nghĩa là luôn tồn tại khả năng hai dòng cùng
            // trỏ một giao dịch (một Failed cũ + một Active mới) — đó là lý do vẫn cần chỉ mục thường
            // ở ngay dưới, và là lý do service tra "giao dịch này đã hoàn chưa" phải lọc theo Status
            // chứ không được đếm số dòng.
            e.HasIndex(r => r.PaymentId, "IX_Refunds_PaymentId_Active")
             .IsUnique()
             .HasFilter("\"Status\" IN ('Pending', 'Succeeded')");

            // 🔴 Chỉ mục này KHÔNG thừa dù chỉ mục điều kiện ở trên cũng mở đầu bằng PaymentId — đúng
            // bài học đã ghi ở docs/28 §4 cho IX_Tickets_TripId và lặp lại ở
            // AppDbContext.CancellationRequest.cs. PostgreSQL chỉ dùng được một partial index khi
            // truy vấn CHỨNG MINH được điều kiện của nó; câu hỏi "giao dịch này từng bị hoàn chưa"
            // không nói gì về Status nên không thoả, và bộ lập kế hoạch sẽ bỏ qua chỉ mục kia.
            //
            // Vì sao phải gọi HasIndex kèm TÊN: EF nhận diện chỉ mục theo danh sách cột, nên lần gọi
            // thứ hai cho cùng một cột sẽ trả về đúng cái builder của lần gọi đầu (đã unique + có
            // điều kiện) chứ không tạo chỉ mục thứ hai — im lặng, không báo lỗi. Có tên riêng thì EF
            // coi là hai chỉ mục khác nhau và sinh đủ cả hai. Tên đặt tay vì tên theo quy ước
            // (IX_Refunds_PaymentId) đã bị chỉ mục điều kiện ở trên chiếm.
            //
            // Chỉ mục này cũng phục vụ luôn khoá ngoại PaymentId — EF thấy cột đã có chỉ mục nên
            // không sinh thêm chỉ mục FK trùng.
            e.HasIndex(r => r.PaymentId, "IX_Refunds_PaymentId");

            // "Hoàn tiền của tôi" của màn hành khách (task dòng 44, Dương Thị Hạnh) — lọc thẳng theo
            // cột này chứ không join sang Payments, xem doc-comment Refund.UserId. Cũng là chỉ mục cho
            // khoá ngoại UserId.
            e.HasIndex(r => r.UserId);

            // Hàng đợi màn Admin quản lý hoàn tiền (task dòng 46–47, Nguyễn Đình Băng): "các khoản
            // đang chờ xử lý, mới nhất trước". Status đứng đầu vì Pending là số ít so với
            // Succeeded/Failed, CreatedAt đứng sau để sắp được luôn. Cùng khuôn với
            // CancellationRequests và DiscountRequests nên màn duyệt gộp đọc được cả ba bảng.
            //
            // Lưu ý: chỉ mục này KHÔNG dùng được cho job thử lại — điều kiện của job là
            // Status = 'Failed' + NextRetryAt tới hạn, mà NextRetryAt không có mặt ở đây. Xem chỉ mục
            // kế tiếp.
            e.HasIndex(r => new { r.Status, r.CreatedAt });

            // Đường quét của job thử lại theo lịch (task dòng 41, Phùng Duy Hoàng):
            // WHERE Status = 'Failed' AND NextRetryAt <= now(). Status đứng đầu vì nó là cột chọn lọc
            // nhất (đa số dòng sẽ là Succeeded), NextRetryAt đứng sau để lấy được luôn phần sắp xếp.
            //
            // Vì sao KHÔNG gộp chỉ mục này với (Status, CreatedAt) ở trên thành một chỉ mục ba cột:
            // hai câu truy vấn khác nhau về bản chất — một câu hỏi "khoản nào mới nhất" cho màn quản
            // trị, một câu hỏi "khoản nào tới hạn gọi lại" cho job. Gộp lại thì chỉ mục phình thêm một
            // cột mà không phục vụ trọn câu nào (CreatedAt và NextRetryAt không bao giờ được sắp cùng
            // nhau), và job — câu chạy dày nhất trong hai — phải quét qua cột không liên quan.
            e.HasIndex(r => new { r.Status, r.NextRetryAt });

            // ⚠️ Cố ý KHÔNG khai chỉ mục cho CancellationRequestId và TicketId: chưa có truy vấn nào
            // hỏi "yêu cầu huỷ này sinh khoản hoàn nào" hay "vé này hoàn bao nhiêu lần" — hai cột đó
            // là cột hiển thị (màn Admin join lấy mã vé / tuyến / giờ chạy), nên EF tự sinh chỉ mục
            // khoá ngoại là đủ. Đây là bảng gắn với dòng tiền, thêm chỉ mục là trả giá ghi cho mọi
            // lượt thử lại — chỉ thêm khi có câu truy vấn thật. Ai cần thì nhắn Dăm.
            //
            // Cũng KHÔNG có chỉ mục UNIQUE riêng trên CancellationRequestId dù "một yêu cầu huỷ chỉ
            // được sinh một khoản hoàn" cũng là luật thật: khoản hoàn của một yêu cầu huỷ luôn trỏ
            // đúng giao dịch của chiếc vé đó, nên chỉ mục IX_Refunds_PaymentId_Active ở trên đã chặn
            // trùng cho CẢ HAI nguồn (PaymentFailure lẫn TicketCancellation). Thêm một chỉ mục unique
            // nữa là dựng chốt thứ hai cho cùng một lỗi, mà lại chỉ chặn được một nửa số ca.

            // FK nghiệp vụ → Restrict tường minh (A5). Mặc định của EF là Cascade, và ở đây hậu quả
            // đúng như cảnh báo đầu dự án: xoá một giao dịch / một yêu cầu huỷ / một vé / một tài
            // khoản mà kéo theo khoản hoàn là mất dấu vết của một dòng tiền đã chi ra — đúng thứ phải
            // tra khi đối soát với sao kê cổng. WithMany() rỗng: không thêm collection nghịch vào
            // Payment.cs / CancellationRequest.cs / Ticket.cs / User.cs — bốn file mà người khác đang
            // đọc, và AppDbContext.Ticket.cs đã đi trước đúng lối này.
            e.HasOne(r => r.Payment)
             .WithMany()
             .HasForeignKey(r => r.PaymentId)
             .OnDelete(DeleteBehavior.Restrict);

            // Cột nullable nên quan hệ là tuỳ chọn: khoản hoàn của nhánh PaymentFailure không có yêu
            // cầu huỷ nào.
            e.HasOne(r => r.CancellationRequest)
             .WithMany()
             .HasForeignKey(r => r.CancellationRequestId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(r => r.Ticket)
             .WithMany()
             .HasForeignKey(r => r.TicketId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(r => r.User)
             .WithMany()
             .HasForeignKey(r => r.UserId)
             .OnDelete(DeleteBehavior.Restrict);

            // Khoá phiên bản dòng cho nhánh đua "hai nơi cùng gọi cổng cho MỘT khoản hoàn" — cùng lối
            // đã dùng ở Payments (callback trùng), Vouchers (hai lượt áp cùng lúc),
            // CancellationRequests và TicketChanges (hai admin cùng duyệt).
            //
            // Vì sao bảng này cần: job thử lại theo lịch (dòng 41) và nút "Thử lại" của Admin (dòng
            // 46) có thể chạy trùng, và nếu triển khai nhiều bản app thì hai job của hai bản cùng
            // nhặt một dòng. Cả hai đều đọc thấy Failed rồi cùng gọi cổng — mà gọi cổng hoàn tiền hai
            // lần cho một khoản là TRẢ TIỀN HAI LẦN, lỗi không sửa được bằng cách sửa dữ liệu.
            //
            // ⚠️ Cơ chế này chỉ chặn được lượt ghi THỨ HAI; nó KHÔNG tự làm cho lượt gọi cổng trở nên
            // an toàn. Job vẫn phải giành dòng bằng một lượt UPDATE khoá phiên bản (đọc → sửa trạng
            // thái → SaveChanges) TRƯỚC khi gọi cổng, để lượt thua nhận DbUpdateConcurrencyException
            // và thoát ra mà chưa gọi gì. Chốt này phải nằm ở tầng service — xem
            // docs/35-csdl-hoan-tien.md §4.2.
            //
            // xmin là cột hệ thống của PostgreSQL — KHÔNG sinh cột mới trong migration.
            e.Property<uint>("xmin").HasColumnName("xmin").IsRowVersion();
        });

        modelBuilder.Entity<RefundLog>(e =>
        {
            // Kết quả và nguồn khởi động lưu chuỗi (A3). varchar(20) cho cả hai: giá trị dài nhất là
            // "ScheduledRetry" (14) và "Unresolved" (10), còn dư cho một giá trị mới. KHÔNG đặt
            // HasDefaultValue — đây là bảng nhật ký, mỗi dòng đều mang kết quả ngay lúc ghi, nên một
            // DEFAULT ở tầng CSDL chỉ tạo thêm đường ghi ra dòng mà không ai chủ ý. Cùng lối
            // TicketValidation.Result.
            //
            // ⚠️ Riêng cột Result, giá trị 0 của enum là Unresolved chứ không phải giá trị "tốt
            // nhất" — lý do ở doc-comment RefundLogResult. Một DEFAULT ở đây mà lỡ mang nghĩa
            // "thành công" thì đúng là lỗi ghi sổ im lặng; đó là lý do thứ hai để không đặt DEFAULT.
            e.Property(l => l.Result).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(l => l.Trigger).HasConversion<string>().HasMaxLength(20).IsRequired();

            // Cùng độ dài với Refund.GatewayTransactionId và Refund.FailReason — đều là chuỗi cổng
            // trả về.
            e.Property(l => l.GatewayTransactionId).HasMaxLength(50);
            e.Property(l => l.Message).HasMaxLength(500);

            // Đường đọc duy nhất của bảng này: dựng lại diễn biến các lượt gọi cổng của MỘT khoản
            // hoàn, cũ nhất trước — phục vụ màn chi tiết của Admin (dòng 46), lý do thất bại của khách
            // (dòng 44), và phép đếm retryCount mà API trả cho frontend. Cặp cột này cũng phục vụ luôn
            // khoá ngoại RefundId (RefundId là cột tiền tố): EF thấy cột đã có chỉ mục nên không sinh
            // thêm chỉ mục FK trùng.
            e.HasIndex(l => new { l.RefundId, l.CreatedAt });

            // ⚠️ Cố ý KHÔNG khai chỉ mục cho GatewayTransactionId dù bước đối soát (dòng 43) tra
            // ngược từ mã giao dịch phía cổng. Lý do: bước đó đối chiếu theo LÔ — đọc sao kê cổng rồi
            // đối với các khoản hoàn trong ngày qua chỉ mục (Status, CreatedAt) của bảng Refunds, chứ
            // không tra từng mã một. Thêm chỉ mục ở đây là trả giá ghi cho mọi lượt gọi cổng để phục
            // vụ một truy vấn chưa tồn tại. Ai viết bước đối soát mà cần tra ngược từng mã thì nhắn
            // Dăm.

            // FK nghiệp vụ → Restrict tường minh (A5): xoá một khoản hoàn mà kéo theo nhật ký các lượt
            // gọi cổng là mất bằng chứng của đúng những lượt gọi không có câu trả lời
            // (RefundLogResult.Unresolved) — thứ duy nhất còn lại để tra khi cổng và hệ thống lệch sổ.
            e.HasOne(l => l.Refund)
             .WithMany()
             .HasForeignKey(l => l.RefundId)
             .OnDelete(DeleteBehavior.Restrict);

            // ⚠️ Cố ý KHÔNG có khoá phiên bản dòng (xmin) như bảng Refunds ở trên: bảng này chỉ ghi
            // thêm (A4), không có nhánh đua nào giữa hai lượt SỬA cùng một dòng để mà chặn. Nhánh đua
            // thật của nghiệp vụ — hai nơi cùng gọi cổng cho một khoản hoàn — được chặn ở chính dòng
            // Refunds (khoá phiên bản ở trên), vì dòng đó mới là thứ bị tranh nhau ghi. Cùng lối lập
            // luận đã ghi ở AppDbContext.Notification.cs và AppDbContext.TicketValidation.cs.
            //
            // Hệ quả: nhật ký có thể có NHIỀU dòng cho một lượt gọi nếu tầng service ghi log hai lần.
            // Đó là lý do phép đếm retryCount phải đếm theo Trigger chứ không đếm thô số dòng — xem
            // docs/35-csdl-hoan-tien.md §4.4.
        });
    }
}
