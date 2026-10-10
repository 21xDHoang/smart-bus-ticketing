using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Payments;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Chốt kết quả một giao dịch thanh toán — điểm vào DUY NHẤT cho cả callback cổng (MoMo IPN,
/// VNPay Return/IPN) lẫn job đối soát. Task *"Xử lý idempotency: chống trừ tiền 2 lần khi callback
/// trùng"* — Phùng Duy Hoàng.
///
/// Cách chống trùng (chốt ở docs/api-contract.md mục "Thanh toán"): bản ghi Payment có
/// <c>PaymentCode</c> duy nhất; lượt chốt chỉ lật trạng thái khi còn <c>Pending</c> — cổng gửi
/// lại callback (MoMo retry tới khi nhận 204) hay job đối soát chạy trùng đều thấy trạng thái đã
/// ngã ngũ và bỏ qua, KHÔNG trừ tiền lần hai, KHÔNG phát hành vé lần hai.
///
/// Nhánh đua thật (hai lượt chốt chạy song song) được chặn ở tầng CSDL bằng concurrency token
/// xmin (xem AppDbContext.Payment.cs): lượt thua nhận DbUpdateConcurrencyException, đọc lại bản
/// ghi rồi cũng trả kết quả "bỏ qua" — người gọi luôn thấy 204 như nhau.
/// </summary>
public class PaymentSettlementService : IPaymentSettlementService
{
    private readonly AppDbContext _db;

    public PaymentSettlementService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<PaymentSettlementOutcome>> SettleAsync(
        string providerCode, PaymentCallbackResult callback, CancellationToken cancellationToken = default)
    {
        // Chữ ký sai nghĩa là dữ liệu người ngoài gửi tới — chặn TRƯỚC khi chạm CSDL (tài liệu cả
        // hai cổng đều bắt thứ tự này). 400 cho cổng biết callback không được nhận.
        if (!callback.IsValid)
        {
            const string message = "Chữ ký cổng không hợp lệ";

            return ServiceResult<PaymentSettlementOutcome>.Invalid(
                message,
                new Dictionary<string, string[]> { ["signature"] = [message] });
        }

        var payment = await _db.Payments
            .FirstOrDefaultAsync(p => p.PaymentCode == callback.PaymentCode, cancellationToken);

        // Đối chiếu theo tài liệu hai cổng (MoMo partnerCode/orderId/amount · VNPay TxnRef/amount):
        // sai mã phương thức hoặc lệch tiền là callback không thuộc về bản ghi này — trả 404 như
        // "không tìm thấy"; bên gọi không sửa được gì nên 400 cũng vô nghĩa.
        if (payment is null
            || !string.Equals(payment.MethodCode, providerCode, StringComparison.OrdinalIgnoreCase)
            || payment.Amount != callback.Amount)
        {
            return ServiceResult<PaymentSettlementOutcome>.NotFound(
                "Không tìm thấy giao dịch khớp mã đơn, cổng và số tiền");
        }

        // Trái tim của task: giao dịch đã ngã ngũ (Success/Failed) thì mọi callback sau chỉ là bản
        // gửi lại — trả Ok kèm SettledNow = false, không ghi gì thêm.
        if (payment.Status != PaymentStatus.Pending)
        {
            return Ok(payment, settledNow: false);
        }

        if (callback.Succeeded)
        {
            payment.Status = PaymentStatus.Success;
            payment.GatewayTransactionId = string.IsNullOrWhiteSpace(callback.GatewayTransactionId)
                ? null
                : callback.GatewayTransactionId;
            // Cổng không kèm thời điểm (hiếm) thì lấy giờ hệ thống — cột chỉ để đối soát.
            payment.PaidAt = callback.PaidAt ?? DateTime.UtcNow;
        }
        else
        {
            payment.Status = PaymentStatus.Failed;

            // Cột Message tối đa 500 ký tự — thông điệp cổng có thể dài; chuỗi trắng coi như không có.
            var message = callback.Message.Trim();
            payment.Message = message.Length == 0
                ? null
                : message.Length > 500 ? message[..500] : message;
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Lượt chốt song song đã lật dòng này trước (xmin đổi) — đọc lại trạng thái thật của
            // bên thắng rồi trả kết quả "bỏ qua" đúng như một bản gửi lại; endpoint vẫn 204.
            await _db.Entry(payment).ReloadAsync(cancellationToken);
            return Ok(payment, settledNow: false);
        }

        return Ok(payment, settledNow: true);
    }

    private static ServiceResult<PaymentSettlementOutcome> Ok(Payment payment, bool settledNow)
        => ServiceResult<PaymentSettlementOutcome>.Ok(new PaymentSettlementOutcome
        {
            PaymentId = payment.Id,
            PaymentCode = payment.PaymentCode,
            UserId = payment.UserId,
            TripId = payment.TripId,
            Status = payment.Status,
            SettledNow = settledNow,
            ShouldIssueTickets = payment.Status == PaymentStatus.Success && payment.TicketId is null,
        });
}
