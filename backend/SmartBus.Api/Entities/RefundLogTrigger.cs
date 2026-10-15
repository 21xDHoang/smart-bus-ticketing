namespace SmartBus.Api.Entities;

/// <summary>
/// Việc gì đã khởi động MỘT lượt gọi cổng hoàn tiền — ghi vào <see cref="RefundLog"/> (US 8). Lưu
/// dạng chuỗi trong CSDL (quy ước A3).
///
/// Cần cột này vì bảng <c>Refunds</c> chỉ giữ trạng thái CUỐI, còn câu hỏi của nghiệp vụ là QUÁ
/// TRÌNH — cùng lối lập luận đã dùng cho <see cref="SeatHoldLog"/>: *"khoản này bị gọi lại mấy lần,
/// và mấy lần là do người bấm?"*. Ba giá trị ở đây là ba đường có thật dẫn tới một lượt gọi:
///
/// - <see cref="Initial"/> — service hoàn tiền tự động (task dòng 39) tạo khoản rồi gọi luôn;
/// - <see cref="ScheduledRetry"/> — job thử lại theo lịch (task dòng 41);
/// - <see cref="ManualRetry"/> — quản trị bấm nút "Thử lại" ở màn Admin quản lý hoàn tiền (task
///   dòng 46). Nút này đã có thật ở frontend đã merge (<c>refundAdminApi.retry</c>).
///
/// ⚠️ Cột này KHÔNG ghi <b>ai</b> bấm: <c>POST /admin/refunds/{id}/retry</c> là một endpoint quản
/// trị, nên nó đã được <c>AuditLogMiddleware</c> ghi vào bảng <c>AuditLogs</c> kèm người thực hiện
/// (US 23). Chép thêm một cột người-vừa-bấm vào đây là tạo nguồn sự thật thứ hai cho cùng một câu
/// hỏi — hai chỗ trả lời "ai bấm" thì sớm muộn cũng lệch nhau. Xem
/// <c>docs/40-csdl-hoan-tien.md</c> §2.5.
/// </summary>
public enum RefundLogTrigger
{
    /// <summary>
    /// Lượt gọi đầu tiên, do service hoàn tiền tự động thực hiện ngay khi khoản hoàn được tạo
    /// (task dòng 39). Mỗi khoản hoàn có <b>đúng một</b> lượt <c>Initial</c>.
    /// </summary>
    Initial,

    /// <summary>
    /// Lượt gọi lại do job thử lại theo lịch (task dòng 41) — chỉ xảy ra với khoản đang
    /// <see cref="RefundStatus.Failed"/> và đã tới <see cref="Refund.NextRetryAt"/>.
    /// </summary>
    ScheduledRetry,

    /// <summary>
    /// Lượt gọi lại do quản trị bấm tay ở màn Admin (task dòng 46), dùng cho khoản đã hết lịch thử
    /// lại tự động mà vẫn muốn thử thêm. Cùng một khoản có thể có nhiều lượt <c>ManualRetry</c>.
    /// </summary>
    ManualRetry,
}
