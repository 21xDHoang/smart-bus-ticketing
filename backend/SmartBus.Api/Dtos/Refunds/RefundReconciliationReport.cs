namespace SmartBus.Api.Dtos.Refunds;

/// <summary>
/// Kết quả MỘT lượt đối soát hoàn tiền hằng ngày (US 8 — task *"Đối soát hoàn tiền hằng ngày + cảnh
/// báo khi lệch số liệu"*, dòng 32 Sprint 4, Nguyễn Duy Kiên).
///
/// Đây là DTO NỘI BỘ: không endpoint nào trả nó ra ngoài. Nó là hợp đồng giữa
/// <see cref="SmartBus.Api.Services.IRefundReconciliationService"/> (người tìm lệch) và
/// <see cref="SmartBus.Api.Services.IRefundReconciliationNotifier"/> (người báo cho quản trị) — tách hai việc để
/// phép đối soát test được mà không cần dựng host, và để notifier không phải biết một dòng lệch được
/// tìm ra bằng cách nào.
///
/// <para>
/// <b>Vì sao báo cáo KHÔNG có API:</b> bề mặt hoàn tiền của quản trị (`GET /admin/refunds`, dòng
/// 46–47) thuộc Nguyễn Đình Băng và chưa có endpoint nào đọc báo cáo đối soát. Thêm một endpoint ở
/// đây là tự mở hình dạng API mới mà không có màn hình nào gọi — đúng luật 5. Cảnh báo đi qua bảng
/// <c>Notifications</c> (A8.5), còn chi tiết kỹ thuật đi vào log máy chủ; mã khoản hoàn trong cảnh
/// báo đủ để quản trị mở đúng dòng ở màn quản lý.
/// </para>
/// </summary>
public class RefundReconciliationReport
{
    /// <summary>Mốc chạy lượt đối soát (UTC) — cũng là mốc dùng để chống báo trùng trong ngày.</summary>
    public DateTime RunAtUtc { get; init; }

    /// <summary>Đầu khoảng đối soát — 24 giờ trước <see cref="RunAtUtc"/>.</summary>
    public DateTime WindowStartUtc { get; init; }

    /// <summary>Cuối khoảng đối soát — chính <see cref="RunAtUtc"/>.</summary>
    public DateTime WindowEndUtc { get; init; }

    /// <summary>Số khoản hoàn đã nạp để kiểm bất biến (khoản đang mở + khoản vừa chạm trong khoảng).</summary>
    public int ScannedRefundCount { get; init; }

    /// <summary>
    /// Có đọc được sao kê cổng hay không. <c>false</c> là trạng thái BÌNH THƯỜNG của bản chạy hiện
    /// tại: chưa cổng nào trong repo có API tra cứu hoàn tiền (xem
    /// <see cref="SmartBus.Api.Services.IRefundStatementSource"/>), nên <c>false</c> KHÔNG phải lỗi và KHÔNG được
    /// làm hỏng lượt đối soát — các phép kiểm nội bộ vẫn chạy đủ.
    /// </summary>
    public bool StatementAvailable { get; init; }

    /// <summary>Các điểm lệch tìm được, đã sắp: nghiêm trọng trước, rồi theo mã khoản hoàn.</summary>
    public IReadOnlyList<RefundReconciliationIssue> Issues { get; init; } = [];

    /// <summary>Có ít nhất một điểm lệch — điều kiện duy nhất để phát cảnh báo.</summary>
    public bool HasMismatch => Issues.Count > 0;

    /// <summary>Số điểm lệch mức <see cref="RefundReconciliationSeverity.Critical"/>.</summary>
    public int CriticalIssueCount
        => Issues.Count(i => i.Severity == RefundReconciliationSeverity.Critical);
}

/// <summary>
/// Mức nghiêm trọng của một điểm lệch.
///
/// <para>
/// <b>Critical</b> = có khả năng TIỀN ĐÃ ĐI hoặc tiền đang không về được: bằng chứng cổng báo thành
/// công nhưng khoản hoàn chưa <c>Succeeded</c>, khoản <c>Pending</c> quá 24 giờ, mã giao dịch lệch,
/// một giao dịch có nhiều khoản hoàn cùng sống, hoặc sao kê cổng nói khác hệ thống. Những ca này phải
/// có người tra tay TRƯỚC khi bấm thử lại — gọi lại một khoản cổng đã trả là hoàn hai lần.
/// </para>
/// <para>
/// <b>Warning</b> = dữ liệu thiếu vệ tinh hoặc job chậm nhịp, chưa nói gì về việc tiền đã đi hay
/// chưa: khoản <c>Failed</c> thiếu lý do, khoản <c>Failed</c> có mốc thử lại quá hạn, sao kê có một
/// lượt hoàn THẤT BẠI mà hệ thống không có dòng tương ứng.
/// </para>
/// </summary>
public enum RefundReconciliationSeverity
{
    Warning,

    Critical,
}

/// <summary>
/// Loại điểm lệch. Tập giá trị này CHỈ sống trong log và cảnh báo nội bộ — không phải hợp đồng API,
/// nên thêm một giá trị không ảnh hưởng frontend (khác <see cref="SmartBus.Api.Entities.NotificationType"/>).
/// </summary>
public enum RefundReconciliationIssueKind
{
    /// <summary>Khoản hoàn Pending quá 24 giờ — không có đường tự động nào gọi lại khoản Pending.</summary>
    StuckPending,

    /// <summary>Nhật ký có lượt gọi THÀNH CÔNG nhưng khoản hoàn không ở trạng thái Succeeded.</summary>
    SuccessEvidenceOnOpenRefund,

    /// <summary>Khoản Succeeded thiếu mã giao dịch, mốc xử lý, hoặc dòng nhật ký thành công.</summary>
    SucceededRefundMissingEvidence,

    /// <summary>Mã giao dịch trên khoản hoàn khác mã của lượt gọi thành công trong nhật ký.</summary>
    SucceededRefundGatewayIdMismatch,

    /// <summary>Một khoản hoàn có nhiều hơn một lượt gọi cổng báo thành công.</summary>
    MultipleSucceededLogs,

    /// <summary>Khoản Failed không có lý do thất bại.</summary>
    FailedRefundMissingReason,

    /// <summary>Khoản đã chốt trạng thái nhưng không có dòng nhật ký nào.</summary>
    MissingAttemptLogs,

    /// <summary>Các cột trạng thái của khoản hoàn mâu thuẫn nhau (Pending có dấu vết kết quả, Succeeded còn lịch thử lại…).</summary>
    StatusFieldInvariantBroken,

    /// <summary>Nguồn khoản hoàn mâu thuẫn với yêu cầu huỷ / vé / trạng thái giao dịch gốc.</summary>
    SourceInvariantBroken,

    /// <summary>Số tiền hoàn không dương hoặc lớn hơn số tiền giao dịch gốc.</summary>
    InvalidAmount,

    /// <summary>Khoản hoàn trỏ tới giao dịch không còn trong CSDL.</summary>
    MissingPayment,

    /// <summary>Một giao dịch có nhiều khoản hoàn chưa thất bại — lẽ ra chỉ mục điều kiện đã chặn.</summary>
    MultipleActiveRefundsForPayment,

    /// <summary>Khoản Failed có mốc thử lại đã quá hạn hơn một giờ — job thử lại có thể không chạy.</summary>
    RetryOverdue,

    /// <summary>Khoản Succeeded có trong hệ thống nhưng không có trên sao kê cổng.</summary>
    StatementMissingRefund,

    /// <summary>Sao kê cổng có giao dịch hoàn mà hệ thống không có khoản hoàn tương ứng.</summary>
    StatementUnknownRefund,

    /// <summary>Số tiền trên sao kê khác số tiền khoản hoàn đã gửi.</summary>
    StatementAmountMismatch,

    /// <summary>Trạng thái trên sao kê khác trạng thái khoản hoàn trong hệ thống.</summary>
    StatementStatusMismatch,
}

/// <summary>Một điểm lệch cụ thể — đủ để người trực tra đúng dòng và đủ để đếm trong cảnh báo.</summary>
public class RefundReconciliationIssue
{
    public RefundReconciliationIssueKind Kind { get; init; }

    public RefundReconciliationSeverity Severity { get; init; }

    /// <summary>Khoản hoàn liên quan; null với điểm lệch chỉ có ở phía sao kê.</summary>
    public Guid? RefundId { get; init; }

    /// <summary>Mã khoản hoàn — thứ quản trị gõ vào màn quản lý để mở đúng dòng.</summary>
    public string? RefundCode { get; init; }

    public Guid? PaymentId { get; init; }

    /// <summary>Số tiền của khoản hoàn (không phải số tiền trên sao kê) — chỉ dùng trong log.</summary>
    public decimal? Amount { get; init; }

    /// <summary>Câu giải thích bằng tiếng Việt, đọc được trong log vận hành.</summary>
    public string Message { get; init; } = string.Empty;
}
