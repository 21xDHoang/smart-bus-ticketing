using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IPaymentTicketIssuer"/> — phát hành vé điện tử khi tiền về (US 4/6, task
/// *"API phát hành vé điện tử sau khi thanh toán thành công"* — Trần Trung Hiếu).
///
/// Chuỗi phát hành cho một giao dịch Success, đúng mục "Phát hành vé sau khi thanh toán" của
/// docs/api-contract.md:
/// 1. Kiểm idempotency: <c>Payment.TicketId</c> đã có → không làm gì (bản gửi lại);
/// 2. Đọc ghế của chuyến theo <c>Payment.SeatNumbers</c> (danh sách đã kiểm thuộc xe lúc tạo giao
///    dịch — số ghế trùng nhau nên không cần kiểm lại);
/// 3. Sinh <c>Ticket</c> <c>Paid</c> cho từng ghế, <c>Code</c> từ <see cref="ITicketQrService"/>,
///    giá mỗi vé = tổng tiền chia đều (làm tròn 2 chữ số);
/// 4. Lật <c>SeatHolds</c> còn <c>Holding</c> của phiên sang <c>Confirmed</c> + ghi
///    <c>SeatHoldLogs</c> <c>Confirmed</c> — đây là lúc ghế chính thức thuộc về vé. Hold đã
///    Expired/Released thì không lật (ghế đó có thể đã được giữ lại bởi khách khác — xung đột
///    ngoài phạm vi luồng thanh toán, chỉ mục unique trên Tickets vẫn chặn ghế trùng);
/// 5. Ghi <c>Payment.TicketId</c> = id vé đầu tiên trong CÙNG một <c>SaveChangesAsync</c>.
///
/// Nhánh cứu hỏng giữa đường: nếu <c>SaveChangesAsync</c> ném <see cref="DbUpdateException"/>
/// (đâm chỉ mục unique <c>(TripId, SeatId)</c> của Tickets — vé của chính giao dịch này đã tồn tại
/// từ lượt phát hành trước bị đứt giữa đường), đọc lại vé đã có rồi vá <c>TicketId</c> — không bao
/// giờ sinh vé thứ hai cho cùng một ghế.
/// </summary>
public class PaymentTicketIssuer : IPaymentTicketIssuer
{
    private readonly AppDbContext _db;
    private readonly ITicketQrService _qr;

    public PaymentTicketIssuer(AppDbContext db, ITicketQrService qr)
    {
        _db = db;
        _qr = qr;
    }

    /// <inheritdoc />
    public async Task<bool> IssueAsync(string paymentCode, CancellationToken cancellationToken = default)
    {
        var payment = await _db.Payments
            .FirstOrDefaultAsync(p => p.PaymentCode == paymentCode, cancellationToken);

        if (payment is null || payment.Status != PaymentStatus.Success || payment.TicketId is not null)
        {
            // Chưa Success (không có gì để phát hành) hoặc đã phát hành rồi (bản gửi lại) — cả hai
            // đều là trạng thái bình thường, không phải lỗi.
            return false;
        }

        var seatNumbers = payment.SeatNumbers
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var busId = await _db.Trips
            .Where(t => t.Id == payment.TripId)
            .Select(t => (Guid?)t.BusId)
            .FirstOrDefaultAsync(cancellationToken);

        if (busId is null)
        {
            // Chuyến đã bị xoá giữa lúc thu tiền và lúc phát hành — dữ liệu hỏng, không có đường
            // tự vá nào: ném lên để người gọi ghi log, vé nằm chờ người xử lý tay.
            throw new InvalidOperationException(
                $"Không tìm thấy chuyến của giao dịch {paymentCode} để phát hành vé.");
        }

        var seats = await _db.Seats
            .Where(s => s.BusId == busId.Value && seatNumbers.Contains(s.SeatNumber))
            .ToListAsync(cancellationToken);

        if (seats.Count != seatNumbers.Count)
        {
            throw new InvalidOperationException(
                $"Giao dịch {paymentCode}: danh sách ghế không khớp xe của chuyến — không phát hành vé.");
        }

        // Giá mỗi vé = tổng tiền đã thu chia đều — vé không có cột "tổng đơn" để giữ con số gốc,
        // chênh lệch làm tròn (tối đa vài đồng) chấp nhận ở quy mô đồ án.
        var pricePerTicket = Math.Round(payment.Amount / seats.Count, 2, MidpointRounding.AwayFromZero);

        var now = DateTime.UtcNow;
        Ticket? firstTicket = null;

        foreach (var seat in seats)
        {
            var ticket = new Ticket
            {
                TripId = payment.TripId,
                SeatId = seat.Id,
                UserId = payment.UserId,
                Price = pricePerTicket,
                Status = TicketStatus.Paid,
            };
            ticket.Code = _qr.GenerateCode(ticket.Id);

            _db.Tickets.Add(ticket);
            firstTicket ??= ticket;
        }

        // Lật các hold còn Holding của đúng (khách, chuyến, ghế) — hold đã hết hạn/nhả thì bỏ qua
        // (ghế có thể đã thuộc phiên khác, không đụng).
        var holds = await _db.SeatHolds
            .Where(h => h.TripId == payment.TripId
                && h.UserId == payment.UserId
                && h.Status == SeatHoldStatus.Holding
                && seats.Select(s => s.Id).Contains(h.SeatId))
            .ToListAsync(cancellationToken);

        foreach (var hold in holds)
        {
            hold.Status = SeatHoldStatus.Confirmed;
            hold.UpdatedAt = now;

            _db.SeatHoldLogs.Add(new SeatHoldLog
            {
                SeatHoldId = hold.Id,
                UserId = payment.UserId,
                SessionCode = hold.SessionCode,
                Action = SeatHoldLogAction.Confirmed,
                CreatedAt = now,
            });
        }

        payment.TicketId = firstTicket!.Id;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Đâm chỉ mục unique (TripId, SeatId) của Tickets — bằng chứng vé của chính giao dịch
            // này đã tồn tại (lượt phát hành trước bị đứt sau khi ghi vé, trước khi ghi TicketId).
            // Không phải lỗi dữ liệu: đọc lại rồi vá TicketId cho giao dịch.
            _db.ChangeTracker.Clear();

            var paymentLai = await _db.Payments
                .FirstOrDefaultAsync(p => p.PaymentCode == paymentCode, cancellationToken);

            if (paymentLai is null || paymentLai.TicketId is not null)
            {
                return false;
            }

            var veDaCo = await _db.Tickets
                .Where(t => t.TripId == paymentLai.TripId
                    && t.UserId == paymentLai.UserId
                    && seats.Select(s => s.Id).Contains(t.SeatId)
                    && t.Status == TicketStatus.Paid)
                .ToListAsync(cancellationToken);

            if (veDaCo.Count != seats.Count)
            {
                // Một phần vé tồn tại, một phần không — ca không tự vá được (SaveChanges là một
                // giao dịch nguyên tử nên trên PostgreSQL không xảy ra; InMemory của test thì có
                // thể). Ném lên để lộ ra thay vì im lặng bỏ qua.
                throw new InvalidOperationException(
                    $"Giao dịch {paymentCode}: chỉ tìm thấy {veDaCo.Count}/{seats.Count} vé đã phát hành — cần xử lý tay.");
            }

            paymentLai.TicketId = veDaCo[0].Id;
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
    }
}
