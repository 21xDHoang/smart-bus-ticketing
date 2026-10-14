namespace SmartBus.Api.Entities;

/// <summary>
/// Bảng <c>RefundLogs</c> — nhật ký TỪNG LƯỢT GỌI CỔNG hoàn tiền của một khoản hoàn (US 8). Task
/// Sprint 4 *"Migrate bảng Refunds, RefundLogs"* — Vàng Thị Dăm, dòng 38.
///
/// <para>
/// Vì sao cần bảng log riêng khi <see cref="Refund.Status"/> đã nói lên trạng thái: cột Status chỉ
/// giữ trạng thái CUỐI, còn câu hỏi của nghiệp vụ là QUÁ TRÌNH — "khoản này đã gọi cổng mấy lần",
/// "lần thứ ba cổng nói gì mà vẫn hỏng", "lượt nào là do người bấm tay". Ghi đè lên một cột thì trả
/// lời được trạng thái hiện tại, không trả lời được lịch sử. Cùng lối
/// <see cref="SeatHold"/> / <see cref="SeatHoldLog"/> đã đi trước.
/// </para>
///
/// <para>
/// 🔴 Bảng này là <b>bằng chứng của lượt gọi <see cref="RefundLogResult.Unresolved"/></b> — ca cổng
/// không trả lời mà <see cref="Refund.Status"/> cố ý giữ nguyên <c>Pending</c>. Không có bảng này
/// thì một lượt gọi timeout để lại <b>không dấu vết nào</b>: khoản hoàn nằm im ở <c>Pending</c> và
/// không ai biết vì sao, trong khi bước đối soát hằng ngày (task dòng 43) cần đúng thông tin đó.
/// </para>
///
/// <para>
/// Bảng CHỈ GHI THÊM: không có <c>UpdatedAt</c> (quy ước A4 — cùng lối <see cref="AuditLog"/>,
/// <see cref="SeatHoldLog"/>) và không sửa/xoá bản ghi. Sửa được nhật ký thì nhật ký mất giá trị.
/// </para>
///
/// <para>
/// Cũng vì chỉ ghi thêm nên bảng <b>không có cột <c>xmin</c></b>: khoá chống ghi đè chỉ có nghĩa
/// với dòng bị sửa. Xem <c>docs/35-csdl-hoan-tien.md</c> §3.3.
/// </para>
/// </summary>
public class RefundLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Khoản hoàn mà lượt gọi cổng này thuộc về.</summary>
    public Guid RefundId { get; set; }

    public Refund? Refund { get; set; }

    /// <summary>
    /// Kết quả cổng trả về cho lượt gọi này — xem <see cref="RefundLogResult"/>. Bắt buộc: một lượt
    /// gọi không có kết quả thì không phải là bản ghi nhật ký, mà là một lỗi lập trình (xem bảng lập
    /// luận ở doc-comment <see cref="RefundLogResult"/> về việc vì sao giá trị 0 là
    /// <c>Unresolved</c>).
    ///
    /// Cột này là nguồn sự thật cho hai phép đếm ở tầng API:
    /// <c>retryCount</c> mà màn Admin hiển thị = số lượt có <c>Trigger != Initial</c>;
    /// "lượt gần nhất cổng nói gì" = dòng mới nhất theo <c>CreatedAt</c>.
    /// </summary>
    public RefundLogResult Result { get; set; }

    /// <summary>Việc gì khởi động lượt gọi này — xem <see cref="RefundLogTrigger"/>.</summary>
    public RefundLogTrigger Trigger { get; set; }

    /// <summary>
    /// Mã giao dịch phía cổng của RIÊNG lượt này, khi cổng có trả về mã.
    ///
    /// ⚠️ Cố ý có mặt ở cả lượt <see cref="RefundLogResult.Failed"/>: cổng có thể đã tạo lệnh hoàn
    /// rồi mới từ chối, và mã đó chính là thứ để tra ngược với sao kê. Vì vậy
    /// <see cref="Refund.GatewayTransactionId"/> không suy ra được từ cột này của lượt cuối — xem
    /// doc-comment cột đó.
    /// </summary>
    public string? GatewayTransactionId { get; set; }

    /// <summary>
    /// Thông điệp cổng trả về, nguyên văn — bằng chứng cho lượt gọi này. Ở lượt thành công có thể là
    /// mô tả ngắn của cổng; ở lượt thất bại là lý do từ chối; ở lượt không có câu trả lời là mô tả
    /// kiểu hỏng (hết thời gian chờ, đứt kết nối).
    ///
    /// Cùng độ dài <c>varchar(500)</c> với <see cref="Refund.FailReason"/> và
    /// <see cref="Payment.Message"/> vì đều là chuỗi cổng trả về. <see cref="Refund.FailReason"/>
    /// chép lại thông điệp của lượt thất bại gần nhất để danh sách khỏi join; bản đầy đủ của TỪNG
    /// lượt nằm ở đây.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Thời điểm hệ thống gọi cổng (UTC) — cột "thời gian" của bảng nhật ký, và là cột sắp xếp để
    /// dựng lại diễn biến các lượt gọi. Chỉ mục <c>(RefundId, CreatedAt)</c> ở
    /// AppDbContext.Refund.cs phục vụ đúng truy vấn đó.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
