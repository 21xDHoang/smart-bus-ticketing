using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Refunds;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IRefundReconciliationService"/> — bước đối soát hằng ngày của US 8.
///
/// <para>
/// <b>Hai nhóm phép kiểm, cố ý tách rời:</b>
/// </para>
/// <list type="number">
/// <item>
/// <b>Sổ nội bộ</b> — luôn chạy. Kiểm những bất biến mà docs/40 §4.3 và doc-comment các entity đã
/// chốt: <c>FailReason ≠ null ⟺ Status = Failed</c>; <c>NextRetryAt ≠ null</c> chỉ khi <c>Failed</c>;
/// nguồn <c>TicketCancellation</c> phải có yêu cầu huỷ + vé; khoản hoàn không được lớn hơn giao dịch
/// gốc; một giao dịch chỉ có một khoản hoàn chưa thất bại; và quan trọng nhất — <b>nhật ký có lượt
/// gọi thành công nhưng khoản hoàn chưa Succeeded</b>. Ca cuối là lý do tồn tại của task: nó xảy ra
/// khi cổng đã trả tiền nhưng lượt ghi kết quả bị mất (xem nhánh <c>DbUpdateConcurrencyException</c>
/// trong <see cref="RefundAttemptService"/>) và không có cách nào phát hiện nếu chỉ nhìn trạng thái.
/// </item>
/// <item>
/// <b>Sao kê cổng</b> — chỉ chạy khi <see cref="IRefundStatementSource"/> trả về dữ liệu. Bản chạy
/// hiện tại không có nguồn thật nên nhóm này im lặng, và báo cáo ghi
/// <see cref="RefundReconciliationReport.StatementAvailable"/> = false. Báo cáo không được suy ra
/// "không lệch" từ chỗ không có dữ liệu.
/// </item>
/// </list>
///
/// <para>
/// <b>Vì sao quét TOÀN BỘ bảng Refunds thay vì chỉ 24 giờ:</b> khoản <c>Pending</c> cũ không có bất
/// kỳ job nào nhặt (job thử lại chỉ nhặt <c>Failed</c>), nên một khoản mắc kẹt từ tuần trước vẫn phải
/// hiện ra hôm nay. Bảng này chỉ có một dòng cho mỗi giao dịch hỏng / mỗi yêu cầu huỷ được duyệt —
/// nhỏ hơn hẳn bảng vé — và job chạy một lần mỗi ngày, nên một lượt đọc toàn bảng là cái giá đúng.
/// Nhờ vậy cũng không phải thêm chỉ mục mới (mọi chỉ mục của bảng thuộc Vàng Thị Dăm).
/// </para>
///
/// <para>
/// <b>Vì sao không tự tính lại số tiền từ <c>Ticket.Price</c>:</b> <see cref="Refund.Amount"/> là con
/// số ĐÃ GỬI LÊN CỔNG. So nó với một công thức theo quy định hôm nay sẽ báo lệch giả mỗi lần nhà xe
/// đổi chính sách phí huỷ. Ở đây chỉ kiểm những gì không phụ thuộc chính sách: số dương, và không
/// lớn hơn số tiền giao dịch gốc.
/// </para>
/// </summary>
public sealed class RefundReconciliationService : IRefundReconciliationService
{
    /// <summary>Độ dài khoảng đối soát mỗi lượt — một ngày.</summary>
    public const int WindowHours = 24;

    /// <summary>
    /// Khoản <c>Pending</c> quá ngưỡng này thì báo. Chọn 24 giờ vì mọi khoản <c>Failed</c> đều có
    /// lịch thử lại trong tối đa 24 giờ; một khoản còn <c>Pending</c> quá một ngày nghĩa là nó không
    /// nằm trong bất kỳ đường tự động nào (kết quả lượt gọi là <c>Unresolved</c>, hoặc app tắt giữa
    /// lúc gọi cổng) và phải có người tra sao kê.
    /// </summary>
    public static readonly TimeSpan PendingAlertThreshold = TimeSpan.FromHours(24);

    /// <summary>
    /// Khoản <c>Failed</c> có mốc thử lại trễ quá ngưỡng này thì báo. Job thử lại quét mỗi phút, nên
    /// trễ một giờ gần như chắc chắn là job không chạy (app chết, DI thiếu) chứ không phải nhiễu nhịp.
    /// </summary>
    public static readonly TimeSpan RetryOverdueGrace = TimeSpan.FromHours(1);

    private readonly AppDbContext _db;

    private readonly IRefundStatementSource _statementSource;

    private readonly ILogger<RefundReconciliationService> _logger;

    public RefundReconciliationService(
        AppDbContext db,
        IRefundStatementSource statementSource,
        ILogger<RefundReconciliationService> logger)
    {
        _db = db;
        _statementSource = statementSource;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<RefundReconciliationReport> RunAsync(
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var windowStart = utcNow.AddHours(-WindowHours);

        // Đọc TOÀN BỘ khoản hoàn một lần — lý do ở doc-comment lớp. Sắp xếp sẵn để kết quả ổn định
        // giữa các lượt chạy và thứ tự điểm lệch không nhảy theo thứ tự dòng CSDL trả về.
        var allRefunds = await _db.Refunds
            .AsNoTracking()
            .OrderBy(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);

        // Khoản cần kiểm bất biến: mọi khoản đang mở (Pending) cộng mọi khoản vừa chạm trong khoảng.
        // Khoản Succeeded/Failed cũ đã chốt thì không kiểm lại mỗi ngày — trừ khi chúng vừa bị sửa.
        var inspected = allRefunds
            .Where(r => r.Status == RefundStatus.Pending
                || r.CreatedAt >= windowStart
                || (r.UpdatedAt is not null && r.UpdatedAt >= windowStart))
            .ToList();

        var inspectedIds = inspected.Select(r => r.Id).ToList();

        var logs = inspectedIds.Count == 0
            ? new List<RefundLog>()
            : await _db.RefundLogs
                .AsNoTracking()
                .Where(l => inspectedIds.Contains(l.RefundId))
                .OrderBy(l => l.CreatedAt)
                .ThenBy(l => l.Id)
                .ToListAsync(cancellationToken);

        var logsByRefund = new Dictionary<Guid, List<RefundLog>>();
        foreach (var log in logs)
        {
            if (!logsByRefund.TryGetValue(log.RefundId, out var list))
            {
                logsByRefund[log.RefundId] = list = [];
            }

            list.Add(log);
        }

        var paymentIds = inspected.Select(r => r.PaymentId).Distinct().ToList();
        var payments = paymentIds.Count == 0
            ? new Dictionary<Guid, Payment>()
            : await _db.Payments
                .AsNoTracking()
                .Where(p => paymentIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, cancellationToken);

        var issues = new List<RefundReconciliationIssue>();

        foreach (var refund in inspected)
        {
            logsByRefund.TryGetValue(refund.Id, out var refundLogs);
            refundLogs ??= [];
            payments.TryGetValue(refund.PaymentId, out var payment);

            InspectRefund(refund, refundLogs, payment, utcNow, issues);
        }

        InspectDuplicateActiveRefunds(allRefunds, issues);

        var statement = await TryGetStatementAsync(windowStart, utcNow, cancellationToken);

        if (statement is not null)
        {
            await InspectStatementAsync(
                statement,
                allRefunds,
                windowStart,
                utcNow,
                issues,
                cancellationToken);
        }

        // Nghiêm trọng trước, rồi theo mã khoản hoàn (thứ quản trị gõ để tra). Sắp trong bộ nhớ chứ
        // không tin thứ tự phát hiện.
        var ordered = issues
            .OrderByDescending(i => i.Severity)
            .ThenBy(i => i.RefundCode ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(i => i.Kind)
            .ToList();

        foreach (var issue in ordered)
        {
            if (issue.Severity == RefundReconciliationSeverity.Critical)
            {
                _logger.LogError(
                    "Đối soát hoàn tiền — {Kind} (nghiêm trọng), khoản {RefundCode}: {Message}",
                    issue.Kind,
                    issue.RefundCode ?? "(không có khoản hoàn)",
                    issue.Message);
            }
            else
            {
                _logger.LogWarning(
                    "Đối soát hoàn tiền — {Kind}, khoản {RefundCode}: {Message}",
                    issue.Kind,
                    issue.RefundCode ?? "(không có khoản hoàn)",
                    issue.Message);
            }
        }

        if (ordered.Count == 0)
        {
            _logger.LogInformation(
                "Đối soát hoàn tiền lúc {RunAt:O}: không phát hiện lệch trong {Count} khoản đã kiểm "
                + "(sao kê cổng: {Statement}).",
                utcNow,
                inspected.Count,
                statement is null ? "không khả dụng" : "đã đối chiếu");
        }

        return new RefundReconciliationReport
        {
            RunAtUtc = utcNow,
            WindowStartUtc = windowStart,
            WindowEndUtc = utcNow,
            ScannedRefundCount = inspected.Count,
            StatementAvailable = statement is not null,
            Issues = ordered,
        };
    }

    /// <summary>
    /// Kiểm bất biến của MỘT khoản hoàn. Mỗi loại điểm lệch phát tối đa một lần cho một khoản — nhờ
    /// vậy một dòng hỏng nặng không dội mười dòng cảnh báo giống nhau.
    /// </summary>
    private static void InspectRefund(
        Refund refund,
        List<RefundLog> logs,
        Payment? payment,
        DateTime utcNow,
        List<RefundReconciliationIssue> issues)
    {
        var succeededLogs = logs.Where(l => l.Result == RefundLogResult.Succeeded).ToList();

        // ── Giao dịch gốc ───────────────────────────────────────────────────────────────────────
        if (payment is null)
        {
            // FK Restrict bảo đảm trên PostgreSQL điều này không xảy ra; nếu có thì dòng tiền đã
            // mất liên kết và không đối chiếu được với bất kỳ sao kê nào.
            issues.Add(Issue(refund, RefundReconciliationIssueKind.MissingPayment,
                RefundReconciliationSeverity.Critical,
                "Khoản hoàn trỏ tới giao dịch không còn trong CSDL."));
        }

        // ── Số tiền ─────────────────────────────────────────────────────────────────────────────
        if (refund.Amount <= 0)
        {
            issues.Add(Issue(refund, RefundReconciliationIssueKind.InvalidAmount,
                RefundReconciliationSeverity.Critical,
                $"Số tiền hoàn phải lớn hơn 0, đang là {refund.Amount}."));
        }
        else if (payment is not null && refund.Amount > payment.Amount)
        {
            issues.Add(Issue(refund, RefundReconciliationIssueKind.InvalidAmount,
                RefundReconciliationSeverity.Critical,
                $"Số tiền hoàn {refund.Amount} lớn hơn số tiền giao dịch gốc {payment.Amount}."));
        }

        // ── Bất biến nguồn ──────────────────────────────────────────────────────────────────────
        // CancellationRequestId ≠ null ⟺ Source = TicketCancellation (docs/40 §4.3). Kiểm cả hai
        // chiều, và kiểm thêm trạng thái giao dịch gốc vì mỗi nguồn ứng với đúng một trạng thái:
        // vé đã huỷ chỉ sinh khoản hoàn từ giao dịch ĐÃ THU tiền; nhánh PaymentFailure chỉ sinh khoản
        // hoàn từ giao dịch cổng báo hỏng nhưng đã giữ tiền.
        var sourceProblems = new List<string>();

        if (refund.Source == RefundSource.TicketCancellation)
        {
            if (refund.CancellationRequestId is null)
            {
                sourceProblems.Add("nguồn TicketCancellation nhưng thiếu CancellationRequestId");
            }

            if (refund.TicketId is null)
            {
                sourceProblems.Add("nguồn TicketCancellation nhưng thiếu TicketId");
            }

            if (payment is not null && payment.Status != PaymentStatus.Success)
            {
                sourceProblems.Add($"giao dịch gốc đang {payment.Status}, phải Success mới có tiền để hoàn");
            }
        }
        else if (refund.Source == RefundSource.PaymentFailure)
        {
            if (refund.CancellationRequestId is not null)
            {
                sourceProblems.Add("nguồn PaymentFailure nhưng lại có CancellationRequestId");
            }

            if (payment is not null && payment.Status != PaymentStatus.Failed)
            {
                sourceProblems.Add($"giao dịch gốc đang {payment.Status}, phải Failed mới đúng nhánh hoàn do thanh toán hỏng");
            }
        }

        if (payment is not null && payment.UserId != refund.UserId)
        {
            sourceProblems.Add("người nhận hoàn khác chủ giao dịch gốc");
        }

        if (sourceProblems.Count > 0)
        {
            issues.Add(Issue(refund, RefundReconciliationIssueKind.SourceInvariantBroken,
                RefundReconciliationSeverity.Critical,
                $"Nguồn khoản hoàn mâu thuẫn: {string.Join("; ", sourceProblems)}."));
        }

        // ── Bất biến cột trạng thái ─────────────────────────────────────────────────────────────
        // Ba bất biến service-level của RefundAttemptService: FailReason khác null ⟺ Failed;
        // NextRetryAt khác null CHỈ khi Failed; Pending không mang dấu vết của một phán quyết nào.
        var statusProblems = new List<string>();

        switch (refund.Status)
        {
            case RefundStatus.Pending:
                if (refund.FailReason is not null)
                {
                    statusProblems.Add("Pending còn FailReason");
                }

                if (refund.NextRetryAt is not null)
                {
                    statusProblems.Add("Pending còn NextRetryAt");
                }

                if (refund.ProcessedAt is not null)
                {
                    statusProblems.Add("Pending còn ProcessedAt");
                }

                if (!string.IsNullOrWhiteSpace(refund.GatewayTransactionId))
                {
                    statusProblems.Add("Pending còn GatewayTransactionId");
                }

                if (refund.CreatedAt <= utcNow - PendingAlertThreshold)
                {
                    issues.Add(Issue(refund, RefundReconciliationIssueKind.StuckPending,
                        RefundReconciliationSeverity.Critical,
                        "Khoản hoàn Pending quá 24 giờ. Không có job nào gọi lại khoản Pending "
                        + "(kết quả lượt trước là Unresolved, hoặc app tắt giữa lúc gọi cổng) — "
                        + "phải tra sao kê cổng trước khi gọi lại."));
                }

                break;

            case RefundStatus.Succeeded:
                if (refund.FailReason is not null)
                {
                    statusProblems.Add("Succeeded còn FailReason");
                }

                if (refund.NextRetryAt is not null)
                {
                    statusProblems.Add("Succeeded còn NextRetryAt");
                }

                // Bằng chứng tối thiểu của một khoản đã trả: mã giao dịch cổng, mốc xử lý, và đúng
                // một dòng nhật ký thành công. Thiếu bất kỳ mảnh nào là khoản không đối chiếu được.
                if (string.IsNullOrWhiteSpace(refund.GatewayTransactionId)
                    || refund.ProcessedAt is null
                    || succeededLogs.Count == 0)
                {
                    issues.Add(Issue(refund, RefundReconciliationIssueKind.SucceededRefundMissingEvidence,
                        RefundReconciliationSeverity.Critical,
                        "Khoản hoàn Succeeded nhưng thiếu bằng chứng: "
                        + (string.IsNullOrWhiteSpace(refund.GatewayTransactionId)
                            ? "không có GatewayTransactionId; "
                            : string.Empty)
                        + (refund.ProcessedAt is null ? "không có ProcessedAt; " : string.Empty)
                        + (succeededLogs.Count == 0 ? "không có nhật ký lượt gọi thành công; " : string.Empty)
                        + "không đối chiếu được với sao kê."));
                }

                if (!string.IsNullOrWhiteSpace(refund.GatewayTransactionId)
                    && succeededLogs.Count > 0
                    && !succeededLogs.Any(l => string.Equals(
                        l.GatewayTransactionId,
                        refund.GatewayTransactionId,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    // Đây là ca "mã của lượt cuối" và "mã của lượt đã trả tiền" khác nhau — đúng thứ
                    // doc-comment Refund.GatewayTransactionId cảnh báo. Có thể chỉ là lượt thành công
                    // không mang mã, nhưng cũng có thể là dấu hiệu của một lần hoàn khác.
                    issues.Add(Issue(refund, RefundReconciliationIssueKind.SucceededRefundGatewayIdMismatch,
                        RefundReconciliationSeverity.Critical,
                        $"Mã giao dịch trên khoản hoàn ({refund.GatewayTransactionId}) không khớp mã "
                        + "của lượt gọi thành công trong nhật ký."));
                }

                if (succeededLogs.Count > 1)
                {
                    issues.Add(Issue(refund, RefundReconciliationIssueKind.MultipleSucceededLogs,
                        RefundReconciliationSeverity.Critical,
                        $"Có {succeededLogs.Count} lượt gọi cổng báo thành công cho cùng một khoản — "
                        + "nguy cơ đã hoàn tiền hai lần."));
                }

                break;

            case RefundStatus.Failed:
                if (string.IsNullOrWhiteSpace(refund.FailReason))
                {
                    issues.Add(Issue(refund, RefundReconciliationIssueKind.FailedRefundMissingReason,
                        RefundReconciliationSeverity.Warning,
                        "Khoản hoàn Failed nhưng không có lý do thất bại."));
                }

                if (refund.NextRetryAt is not null && refund.NextRetryAt <= utcNow - RetryOverdueGrace)
                {
                    issues.Add(Issue(refund, RefundReconciliationIssueKind.RetryOverdue,
                        RefundReconciliationSeverity.Warning,
                        $"Mốc thử lại {refund.NextRetryAt:O} đã quá hạn hơn một giờ — job thử lại "
                        + "có thể không chạy."));
                }

                if (logs.Count == 0)
                {
                    issues.Add(Issue(refund, RefundReconciliationIssueKind.MissingAttemptLogs,
                        RefundReconciliationSeverity.Warning,
                        "Khoản hoàn đã chốt Failed nhưng không có dòng nhật ký nào."));
                }

                break;
        }

        if (statusProblems.Count > 0)
        {
            issues.Add(Issue(refund, RefundReconciliationIssueKind.StatusFieldInvariantBroken,
                RefundReconciliationSeverity.Critical,
                $"Cột trạng thái mâu thuẫn: {string.Join("; ", statusProblems)}."));
        }

        // ── Bằng chứng thành công trên khoản CHƯA thành công ────────────────────────────────────
        // 🔴 Điểm lệch quan trọng nhất của bước đối soát. Đường sinh ra nó: cổng đã trả tiền, nhưng
        // lượt SaveChanges ghi kết quả nhận DbUpdateConcurrencyException (xem RefundAttemptService) —
        // dòng Refunds nằm lại Pending trong khi RefundLogs giữ đúng một dòng Succeeded. Không có phép
        // kiểm này thì khoản đó chỉ được nhìn thấy khi khách khiếu nại.
        if (refund.Status != RefundStatus.Succeeded && succeededLogs.Count > 0)
        {
            issues.Add(Issue(refund, RefundReconciliationIssueKind.SuccessEvidenceOnOpenRefund,
                RefundReconciliationSeverity.Critical,
                "Nhật ký có lượt gọi cổng THÀNH CÔNG nhưng khoản hoàn chưa ở trạng thái Succeeded — "
                + "tiền có thể đã đi. Phải tra sao kê cổng trước khi gọi lại (gọi lại là hoàn hai lần)."));
        }
    }

    /// <summary>
    /// Tìm hai khoản hoàn cùng sống trên một giao dịch. Chỉ mục điều kiện
    /// <c>IX_Refunds_PaymentId_Active</c> đã chặn ở tầng CSDL trên PostgreSQL; phép kiểm này bắt ca
    /// chỉ mục bị thiếu hoặc dữ liệu được nạp tay. Quét TOÀN BỘ bảng, không chỉ khoảng 24 giờ: một
    /// cặp trùng từ tuần trước vẫn là một cặp trùng.
    /// </summary>
    private static void InspectDuplicateActiveRefunds(
        List<Refund> allRefunds,
        List<RefundReconciliationIssue> issues)
    {
        var duplicateGroups = allRefunds
            .Where(r => r.Status != RefundStatus.Failed)
            .GroupBy(r => r.PaymentId)
            .Where(g => g.Count() > 1);

        foreach (var group in duplicateGroups)
        {
            var first = group.First();
            var codes = string.Join(", ", group.Select(r => r.RefundCode));

            issues.Add(new RefundReconciliationIssue
            {
                Kind = RefundReconciliationIssueKind.MultipleActiveRefundsForPayment,
                Severity = RefundReconciliationSeverity.Critical,
                RefundId = first.Id,
                RefundCode = first.RefundCode,
                PaymentId = first.PaymentId,
                Amount = first.Amount,
                Message = $"Giao dịch có {group.Count()} khoản hoàn chưa thất bại: {codes}. "
                    + "Chỉ mục IX_Refunds_PaymentId_Active lẽ ra phải chặn trường hợp này.",
            });
        }
    }

    /// <summary>
    /// Đọc sao kê cổng. Mọi lỗi của nguồn sao kê bị nuốt thành "không khả dụng" — bước đối soát nội
    /// bộ đã chạy xong và không được mất kết quả vì cổng phụ trả lời chậm.
    /// </summary>
    private async Task<RefundStatement?> TryGetStatementAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _statementSource.GetStatementAsync(fromUtc, toUtc, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Không đọc được sao kê hoàn tiền — bỏ phần đối chiếu sao kê, vẫn giữ kết quả đối soát nội bộ.");
            return null;
        }
    }

    /// <summary>
    /// Đối chiếu sổ nội bộ với sao kê cổng. Khoá đối chiếu là mã giao dịch hoàn phía cổng.
    ///
    /// <para>
    /// Đối tượng so sánh là <see cref="Refund.Amount"/> — con số ĐÃ GỬI LÊN CỔNG — chứ không phải số
    /// tiền tính lại từ <c>Ticket.Price</c>. Đây đúng lý do cột đó tồn tại (doc-comment
    /// <c>Refund.Amount</c>): so một con số tính bằng chính sách hôm nay với giao dịch gửi bằng chính
    /// sách hôm đó sẽ báo lệch giả mỗi lần nhà xe đổi quy định.
    /// </para>
    /// </summary>
    private async Task InspectStatementAsync(
        RefundStatement statement,
        List<Refund> allRefunds,
        DateTime windowStart,
        DateTime windowEnd,
        List<RefundReconciliationIssue> issues,
        CancellationToken cancellationToken)
    {
        var refundById = allRefunds.ToDictionary(r => r.Id);

        var refundsByGatewayId = new Dictionary<string, Refund>(StringComparer.OrdinalIgnoreCase);
        foreach (var refund in allRefunds)
        {
            if (!string.IsNullOrWhiteSpace(refund.GatewayTransactionId))
            {
                refundsByGatewayId.TryAdd(refund.GatewayTransactionId, refund);
            }
        }

        // Lượt thất bại cũng mang mã giao dịch phía cổng (cổng tạo lệnh rồi mới từ chối) — nạp thêm
        // các mã trong khoảng sao kê để một lệnh THẤT BẠI có thể khớp về đúng khoản hoàn của nó thay
        // vì bị báo "không có trong hệ thống". Chỉ đọc log trong khoảng: mã của một lượt cũ hơn
        // không thể xuất hiện trên sao kê của kỳ này.
        var statementLogs = await _db.RefundLogs
            .AsNoTracking()
            .Where(l => l.GatewayTransactionId != null
                && l.CreatedAt >= windowStart
                && l.CreatedAt <= windowEnd)
            .Select(l => new { l.RefundId, l.GatewayTransactionId })
            .ToListAsync(cancellationToken);

        foreach (var log in statementLogs)
        {
            if (!string.IsNullOrWhiteSpace(log.GatewayTransactionId)
                && !refundsByGatewayId.ContainsKey(log.GatewayTransactionId)
                && refundById.TryGetValue(log.RefundId, out var logRefund))
            {
                refundsByGatewayId[log.GatewayTransactionId] = logRefund;
            }
        }

        // Sao kê có thể in cùng một mã hai lần (một dòng gốc + một dòng điều chỉnh). Giữ dòng đầu để
        // mỗi mã chỉ sinh tối đa một điểm lệch; đây là dữ liệu của cổng, không phải thứ ta sửa được.
        var entriesByGatewayId = new Dictionary<string, RefundStatementEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in statement.Entries)
        {
            if (!string.IsNullOrWhiteSpace(entry.GatewayTransactionId))
            {
                entriesByGatewayId.TryAdd(entry.GatewayTransactionId, entry);
            }
        }

        // ── Chiều 1: khoản đã trả trong kỳ phải có trên sao kê ──────────────────────────────────
        var succeededInWindow = allRefunds
            .Where(r => r.Status == RefundStatus.Succeeded
                && r.ProcessedAt is not null
                && r.ProcessedAt >= windowStart
                && r.ProcessedAt <= windowEnd)
            .ToList();

        var comparedSucceeded = new HashSet<Guid>();

        foreach (var refund in succeededInWindow)
        {
            comparedSucceeded.Add(refund.Id);

            // Khoản thiếu mã giao dịch đã được báo ở nhóm bất biến nội bộ — không đối chiếu được, và
            // báo thêm "không có trên sao kê" chỉ là cùng một sự thật nói hai lần.
            if (string.IsNullOrWhiteSpace(refund.GatewayTransactionId))
            {
                continue;
            }

            if (!entriesByGatewayId.TryGetValue(refund.GatewayTransactionId, out var entry))
            {
                issues.Add(Issue(refund, RefundReconciliationIssueKind.StatementMissingRefund,
                    RefundReconciliationSeverity.Critical,
                    $"Sao kê cổng không có lệnh hoàn {refund.GatewayTransactionId} dù hệ thống đã "
                    + "Succeeded trong kỳ — cổng có thể chưa thực sự trả tiền."));
                continue;
            }

            if (entry.Amount != refund.Amount)
            {
                issues.Add(Issue(refund, RefundReconciliationIssueKind.StatementAmountMismatch,
                    RefundReconciliationSeverity.Critical,
                    $"Sao kê ghi {entry.Amount} nhưng hệ thống đã gửi {refund.Amount}."));
            }

            if (entry.Status != RefundStatementEntryStatus.Succeeded)
            {
                issues.Add(Issue(refund, RefundReconciliationIssueKind.StatementStatusMismatch,
                    RefundReconciliationSeverity.Critical,
                    "Hệ thống ghi khoản hoàn Succeeded nhưng sao kê cổng ghi THẤT BẠI."));
            }
        }

        // ── Chiều 2: mọi dòng sao kê phải khớp một khoản hoàn hoặc một lượt gọi đã biết ─────────
        foreach (var entry in entriesByGatewayId.Values)
        {
            if (!refundsByGatewayId.TryGetValue(entry.GatewayTransactionId, out var refund))
            {
                // Tiền đã rời cổng mà hệ thống không có dòng nào là ca nghiêm trọng nhất của chiều
                // này; một lệnh THẤT BẠI lạ chỉ là dấu vết cần đối chiếu tay.
                issues.Add(new RefundReconciliationIssue
                {
                    Kind = RefundReconciliationIssueKind.StatementUnknownRefund,
                    Severity = entry.Status == RefundStatementEntryStatus.Succeeded
                        ? RefundReconciliationSeverity.Critical
                        : RefundReconciliationSeverity.Warning,
                    Message = $"Sao kê cổng có lệnh hoàn {entry.Status} mã {entry.GatewayTransactionId} "
                        + $"({entry.Amount}) mà hệ thống không có khoản hoàn tương ứng.",
                });
                continue;
            }

            // Khoản Succeeded trong kỳ đã được đối chiếu đầy đủ ở chiều 1.
            if (comparedSucceeded.Contains(refund.Id))
            {
                continue;
            }

            if (refund.Status == RefundStatus.Succeeded)
            {
                // Khoản Succeeded cũ (ngoài kỳ) xuất hiện trên sao kê kỳ này: số tiền vẫn phải khớp,
                // và sao kê nói thất bại thì mâu thuẫn trực tiếp.
                if (entry.Amount != refund.Amount)
                {
                    issues.Add(Issue(refund, RefundReconciliationIssueKind.StatementAmountMismatch,
                        RefundReconciliationSeverity.Critical,
                        $"Sao kê ghi {entry.Amount} nhưng hệ thống đã gửi {refund.Amount}."));
                }

                if (entry.Status != RefundStatementEntryStatus.Succeeded)
                {
                    issues.Add(Issue(refund, RefundReconciliationIssueKind.StatementStatusMismatch,
                        RefundReconciliationSeverity.Critical,
                        "Hệ thống ghi khoản hoàn Succeeded nhưng sao kê cổng ghi THẤT BẠI."));
                }
            }
            else if (entry.Status == RefundStatementEntryStatus.Succeeded)
            {
                // Đây là ca ngược của "SuccessEvidenceOnOpenRefund" nhưng bằng chứng đến từ cổng:
                // tiền đã đi, dòng Refunds còn Pending/Failed. Không được gọi lại.
                issues.Add(Issue(refund, RefundReconciliationIssueKind.StatementStatusMismatch,
                    RefundReconciliationSeverity.Critical,
                    $"Sao kê cổng ghi lệnh hoàn THÀNH CÔNG nhưng khoản hoàn đang {refund.Status} — "
                    + "tiền có thể đã đi. Phải đối chiếu tay trước khi gọi lại."));
            }
            else if (entry.Amount != refund.Amount)
            {
                // Cả hai bên đều nói lượt này không trả được tiền; lệch số tiền chỉ là dấu vết dữ
                // liệu, không phải nguy cơ hoàn hai lần.
                issues.Add(Issue(refund, RefundReconciliationIssueKind.StatementAmountMismatch,
                    RefundReconciliationSeverity.Warning,
                    $"Sao kê ghi {entry.Amount} nhưng hệ thống đã gửi {refund.Amount}."));
            }
        }
    }

    private static RefundReconciliationIssue Issue(
        Refund refund,
        RefundReconciliationIssueKind kind,
        RefundReconciliationSeverity severity,
        string message)
        => new()
        {
            Kind = kind,
            Severity = severity,
            RefundId = refund.Id,
            RefundCode = refund.RefundCode,
            PaymentId = refund.PaymentId,
            Amount = refund.Amount,
            Message = message,
        };
}
