using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Refunds;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Thực hiện MỘT lượt gọi cổng hoàn tiền (US 8 — task *"Xử lý hoàn tiền thất bại + retry theo
/// lịch"*, Phùng Duy Hoàng). Trình tự bắt buộc, đúng docs/35 §4.2:
///
/// 1. Đọc khoản hoàn + giao dịch gốc; kiểm tra lượt gọi có đủ điều kiện với <c>trigger</c> không;
/// 2. GIÀNH DÒNG bằng một lượt ghi có khoá phiên bản <c>xmin</c> (<c>SaveChanges</c> trên entity
///    đang được theo dõi) — XONG TRƯỚC KHI gọi cổng;
/// 3. Chỉ lượt giành được mới gọi cổng, rồi ghi kết quả + đúng một dòng <see cref="RefundLog"/>.
///
/// Nếu đảo bước 2 xuống sau bước 3 thì <c>xmin</c> chỉ phát hiện cuộc đua SAU KHI cả hai lời gọi đã
/// đi — tức là đã hoàn tiền hai lần rồi mới biết. Lượt thua nhận
/// <c>DbUpdateConcurrencyException</c> ngay ở bước 2, thoát ra mà chưa gọi gì.
///
/// Lượt đã giành dòng giữ khoản ở <see cref="RefundStatus.Pending"/> suốt lúc gọi cổng, nên mọi
/// lượt khác (job theo lịch lẫn nút Admin) đều trượt bước kiểm điều kiện: mỗi khoản có đúng MỘT
/// lượt gọi tại một thời điểm.
/// </summary>
public class RefundAttemptService : IRefundAttemptService
{
    /// <summary>Khớp <c>varchar(50)</c> của <c>Refunds.GatewayTransactionId</c> và <c>RefundLogs.GatewayTransactionId</c>.</summary>
    private const int GatewayTransactionIdMaxLength = 50;

    /// <summary>Khớp <c>varchar(500)</c> của <c>Refunds.FailReason</c> và <c>RefundLogs.Message</c>.</summary>
    private const int MessageMaxLength = 500;

    private readonly AppDbContext _db;
    private readonly IRefundGatewayCaller _gateway;
    private readonly ILogger<RefundAttemptService> _logger;

    public RefundAttemptService(
        AppDbContext db,
        IRefundGatewayCaller gateway,
        ILogger<RefundAttemptService> logger)
    {
        _db = db;
        _gateway = gateway;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<RefundAttemptOutcome> ExecuteAsync(
        Guid refundId,
        RefundLogTrigger trigger,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        // Nạp kèm giao dịch gốc: phương thức hoàn tiền đọc từ đó (docs/35 §6 — hoàn tiền luôn quay
        // về đúng kênh đã thu, không có tham số chọn kênh từ người dùng).
        var refund = await _db.Refunds
            .Include(r => r.Payment)
            .FirstOrDefaultAsync(r => r.Id == refundId, cancellationToken);

        // Khoản không còn (job quét xong rồi mới tới lượt, hoặc id cũ gọi tay): không phải lỗi —
        // không có gì để giành, trả về "không giành được dòng" rồi đi tiếp.
        if (refund is null)
        {
            return new RefundAttemptOutcome { RefundId = refundId, Claimed = false };
        }

        if (!IsEligible(refund, trigger, now))
        {
            return new RefundAttemptOutcome
            {
                RefundId = refund.Id,
                RefundCode = refund.RefundCode,
                Claimed = false,
            };
        }

        // ── GIÀNH DÒNG (docs/35 §4.2) ────────────────────────────────────────────────────────
        // Đưa khoản về trạng thái "đang gọi": Pending + xoá sạch dấu vết lượt trước.
        refund.Status = RefundStatus.Pending;
        refund.NextRetryAt = null;
        refund.FailReason = null;
        refund.GatewayTransactionId = null;   // cột này chỉ được ghi lại ở nhánh Succeeded
        refund.ProcessedAt = null;

        // ⚠️ `UpdatedAt` là cột LUÔN đổi (bắt buộc theo quy ước audit) và đó chính là thứ bảo đảm
        // câu UPDATE có lý do tồn tại: không có cột nào đổi giá trị thì EF không phát UPDATE, xmin
        // không có gì để so, và hai lượt đua nhau cùng "giành" thành công. Khe hẹp còn lại — hai
        // lượt Initial song song cùng một tick (trùng cả `now`) — nằm ngoài hợp đồng của người gọi:
        // service hoàn tiền tự động (dòng 28) gọi Initial đúng một lần cho một khoản.
        refund.UpdatedAt = now;

        try
        {
            // Đây CHÍNH LÀ lượt giành dòng. Cố ý dùng SaveChanges (không ExecuteUpdateAsync): chỉ
            // đường này mới so khoá phiên bản xmin — ExecuteUpdate đi vòng qua change tracker.
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Thua cuộc đua: dòng đã bị lượt khác sửa kể từ lúc mình đọc (xmin lệch). Chưa gọi
            // cổng, chưa ghi gì — thoát êm và trả về "không giành được".
            _logger.LogInformation(
                "Khoản {RefundCode}: thua cuộc đua giành dòng ở lượt {Trigger} — bỏ lượt, không gọi cổng.",
                refund.RefundCode, trigger);
            return new RefundAttemptOutcome
            {
                RefundId = refund.Id,
                RefundCode = refund.RefundCode,
                Claimed = false,
            };
        }

        // ── ĐÃ GIỮ DÒNG ─────────────────────────────────────────────────────────────────────
        // Từ đây khoản mang Status = Pending nên mọi lượt khác trượt IsEligible; chỉ còn đúng lượt
        // này chạm được khoản cho tới khi nó ghi kết quả bên dưới.
        var call = await CallGatewaySafelyAsync(refund, cancellationToken);

        // Áp kết quả — ba bất biến service-level của docs/35 §4.3 nằm gọn trong khối này:
        //   • FailReason khác null ⟺ Status = Failed;
        //   • NextRetryAt khác null CHỈ khi Status = Failed;
        //   • Unresolved giữ nguyên Pending — không xếp lịch, không chốt ProcessedAt.
        switch (call.Result)
        {
            case RefundLogResult.Succeeded:
                refund.Status = RefundStatus.Succeeded;
                // Mã giao dịch vào cột của khoản: cột này dành riêng cho lượt hoàn THÀNH CÔNG (xem
                // doc-comment cột trong entity).
                refund.GatewayTransactionId = Truncate(call.GatewayTransactionId, GatewayTransactionIdMaxLength);
                refund.FailReason = null;
                refund.NextRetryAt = null;
                refund.ProcessedAt = now;   // cổng đã chốt: tiền đi rồi
                break;

            case RefundLogResult.Failed:
                refund.Status = RefundStatus.Failed;

                // Bất biến 2: thất bại thì FailReason KHÔNG được null — cổng không nêu lý do thì
                // chuỗi dự phòng vào đây, để màn Admin luôn có cái hiển thị và người đối soát luôn
                // có cái tra.
                refund.FailReason = string.IsNullOrWhiteSpace(call.Message)
                    ? "Cổng từ chối mà không nêu lý do."
                    : Truncate(call.Message, MessageMaxLength);

                // Mã giao dịch của lượt THẤT BẠI cố ý không vào Refunds.GatewayTransactionId (cột
                // chỉ dành cho lượt thành công) — bản của lượt này nằm ở dòng RefundLog bên dưới.
                // Hai chỗ lệch nhau là ca đối soát (dòng 43) phải bắt.
                refund.GatewayTransactionId = null;

                // Bất biến 3: chỉ lượt thất bại mới xếp lịch. Số lượt đã thử được đếm TRƯỚC khi
                // dòng log của lượt này tồn tại (nó nằm dưới khối switch) — xem CountRetriesAsync.
                refund.NextRetryAt = RefundRetryPolicy.NextRetryAt(
                    now, await CountRetriesAsync(refund.Id, trigger, cancellationToken));
                refund.ProcessedAt = now;   // phán quyết cuối của lượt này: thất bại
                break;

            default:
                // Unresolved (và mọi giá trị lạ sau này — cố ý không liệt kê riêng để một giá trị
                // mới KHÔNG rơi vào nhánh đoán nào): cổng không trả lời thì khoản nằm nguyên
                // Pending. KHÔNG xếp lịch (gọi lại một lượt chưa rõ kết quả là hoàn hai lần) và
                // KHÔNG chốt ProcessedAt (chưa có phán quyết cuối). Bước đối soát (dòng 43) tra
                // sao kê cổng rồi mới lật — đây là mấu chốt của "không bao giờ đoán".
                refund.Status = RefundStatus.Pending;
                refund.NextRetryAt = null;
                refund.FailReason = null;
                break;
        }

        // Nhật ký cho MỌI lượt đã gọi — kể cả Unresolved (docs/35 §6): thiếu dòng log của lượt
        // không trả lời là mất đúng bằng chứng mà bước đối soát cần.
        _db.RefundLogs.Add(new RefundLog
        {
            RefundId = refund.Id,
            Result = call.Result,
            Trigger = trigger,
            GatewayTransactionId = Truncate(call.GatewayTransactionId, GatewayTransactionIdMaxLength),
            Message = Truncate(call.Message, MessageMaxLength),
            CreatedAt = now,   // dùng lại now của lượt gọi, không tự gọi UtcNow lần nữa
        });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Cửa hẹp: ai đó sửa khoản NGAY TRONG lúc ta gọi cổng. Hôm nay không luồng nào làm thế
            // (khoản đang Pending, mọi lượt khác đều trượt điều kiện) — cửa này mở ra khi bước đối
            // soát (dòng 43) đi vào. Lúc đó TIỀN CÓ THỂ ĐÃ ĐI mà kết quả không ghi được: ca duy
            // nhất phải ồn ào, vì chỉ bước đối soát mới tra ra được.
            _logger.LogError(
                "Khoản {RefundCode}: ghi kết quả lượt gọi ({Result}) KHÔNG thành công vì dòng bị " +
                "luồng khác sửa trong lúc gọi cổng — khoản nằm lại ở Pending, bước đối soát (dòng " +
                "43) phải tra sao kê cổng.",
                refund.RefundCode, call.Result);

            // Thay đổi trên khoản nhường cho luồng kia (đúng — nó mới là bên quyết), nhưng dòng
            // nhật ký của lượt vừa đi thì không được mất: nó là bằng chứng duy nhất của lượt đó.
            _db.Entry(refund).State = EntityState.Detached;
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Khoản {RefundCode}: dòng nhật ký của lượt gọi cũng không ghi được.",
                    refund.RefundCode);
            }
        }

        return new RefundAttemptOutcome
        {
            RefundId = refund.Id,
            RefundCode = refund.RefundCode,
            Claimed = true,
            Result = call.Result,
            // Với lượt thất bại trả về lý do đã chuẩn hoá (có chuỗi dự phòng) — người gọi ghi log
            // đúng thứ đã vào FailReason, không phải chuỗi rỗng của cổng.
            Message = call.Result == RefundLogResult.Failed ? refund.FailReason : call.Message,
            NextRetryAt = refund.NextRetryAt,
        };
    }

    /// <summary>
    /// Lượt gọi này có đủ điều kiện cho khoản ở trạng thái hiện tại không. Điều kiện khác nhau theo
    /// <paramref name="trigger"/>; thiếu vế nào là mở đường cho một lượt gọi trùng (docs/35 §4.2).
    /// </summary>
    private static bool IsEligible(Refund refund, RefundLogTrigger trigger, DateTime now) => trigger switch
    {
        // Lượt đầu: khoản vừa được tạo còn nguyên Pending. Khoản đã Failed thì service hoàn tiền tự
        // động KHÔNG được gọi lại — việc đó là của job theo lịch hoặc quản trị.
        RefundLogTrigger.Initial => refund.Status == RefundStatus.Pending,

        // Lượt theo lịch: CHỈ khoản Failed, CÓ lịch, và đã tới hạn. Job đã lọc sẵn khi quét hàng
        // đợi, nhưng kiểm lại ở đây vì giữa lúc quét và lúc chạy, lượt khác có thể vừa đổi khoản.
        RefundLogTrigger.ScheduledRetry =>
            refund.Status == RefundStatus.Failed
            && refund.NextRetryAt is not null
            && refund.NextRetryAt <= now,

        // Lượt bấm tay từ màn Admin: khoản Failed là đủ — quản trị được phép thử SỚM hơn lịch, nên
        // cố ý KHÔNG đòi NextRetryAt (kể cả khoản đã hết thang, NextRetryAt = null, nút vẫn chạy).
        RefundLogTrigger.ManualRetry => refund.Status == RefundStatus.Failed,

        _ => false,
    };

    /// <summary>
    /// Số lượt THỬ LẠI đã thực hiện tính đến VÀ BAO GỒM lượt đang xét — tham số <c>retriesAttempted</c>
    /// của <see cref="RefundRetryPolicy.NextRetryAt"/>. Lượt <c>Initial</c> không phải thử lại nên
    /// lần hỏng đầu tiên ra 0 (chờ 5 phút); lượt bấm tay tiêu nấc thang y như lượt theo lịch.
    ///
    /// Đếm trên <see cref="RefundLog"/> chứ không có cột đếm riêng (entity <c>Refund</c> cố ý không
    /// có — xem doc-comment <c>Refund.Status</c>): mỗi lượt đã chạy trọn vẹn đều để lại đúng một
    /// dòng log. ⚠️ Phải gọi TRƯỚC khi thêm dòng log của lượt hiện tại, nếu không nó tự đếm mình.
    /// </summary>
    private async Task<int> CountRetriesAsync(
        Guid refundId,
        RefundLogTrigger trigger,
        CancellationToken cancellationToken)
    {
        var previousRetries = await _db.RefundLogs.CountAsync(
            l => l.RefundId == refundId && l.Trigger != RefundLogTrigger.Initial,
            cancellationToken);

        return previousRetries + (trigger == RefundLogTrigger.Initial ? 0 : 1);
    }

    /// <summary>
    /// Gọi cổng và không bao giờ để lỗi cổng ném ra ngoài — mọi hỏng hóc thành <c>Unresolved</c>
    /// (trừ lúc app tắt, xem trong hàm).
    /// </summary>
    private async Task<RefundGatewayCallOutcome> CallGatewaySafelyAsync(
        Refund refund,
        CancellationToken cancellationToken)
    {
        var request = new RefundGatewayCallRequest
        {
            RefundId = refund.Id,
            RefundCode = refund.RefundCode,
            // FK Restrict bảo đảm Payment luôn tồn tại; `?.` + `??` chỉ là chốt chặn cuối để một
            // dòng dữ liệu hỏng không làm nổ cả lượt gọi.
            MethodCode = refund.Payment?.MethodCode ?? string.Empty,
            Amount = refund.Amount,
        };

        try
        {
            return await _gateway.CallAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // App đang tắt giữa lượt gọi: KHÔNG đoán, KHÔNG ghi. Khoản nằm nguyên Pending (đã giành
            // dòng trước đó) và bước đối soát (dòng 43) sẽ tra với sao kê cổng. Ném tiếp để tầng
            // trên dừng đúng cách.
            throw;
        }
        catch (Exception ex)
        {
            // Mọi kiểu hỏng khác — kể cả timeout nội bộ của cổng (TaskCanceledException KHÔNG do
            // token của ta) — là "cổng không trả lời". Đây là câu trả lời trung thực duy nhất:
            // Succeeded là nói dối (tiền chưa chắc đã đi), Failed là mở đường cho job gọi lại một
            // khoản có thể đã hoàn.
            return new RefundGatewayCallOutcome
            {
                Result = RefundLogResult.Unresolved,
                Message = $"Lỗi khi gọi cổng hoàn tiền: {ex.Message}",
            };
        }
    }

    /// <summary>
    /// Cắt chuỗi về đúng độ dài cột trước khi ghi — cổng trả mã/lý do dài bất thường không được làm
    /// hỏng cả lượt ghi (mất cả kết quả lẫn dòng log). Null giữ nguyên null.
    /// </summary>
    private static string? Truncate(string? value, int maxLength) =>
        value is not null && value.Length > maxLength ? value[..maxLength] : value;
}
