using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Payments;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IPaymentReconciliationService"/> — hỏi cổng trạng thái của giao dịch Pending
/// quá hạn rồi chốt qua <see cref="PaymentSettlementService"/> (US 6, task *"đối soát tự động"* —
/// Trần Trung Hiếu). Hai chốt của hợp đồng: nhịp quét 1 phút nằm ở job nền, ngưỡng quá hạn 5 phút
/// nằm ở đây.
///
/// Dịch kết quả hỏi cổng sang <see cref="PaymentCallbackResult"/> — cùng một hình dạng với adapter
/// callback, để đi qua ĐÚNG MỘT cửa chốt. Khác biệt duy nhất với callback: khi cổng báo THẤT BẠI
/// thì <c>Amount</c> gửi vào cửa chốt là số tiền CỦA BẢN GHI (không có số tiền cổng để đối chiếu —
/// giao dịch không tồn tại thì cổng trả amount 0), còn khi cổng báo THÀNH CÔNG thì gửi số tiền
/// CỔNG TRẢ VỀ để phép đối chiếu amount của cửa chốt vẫn là phép kiểm thật.
///
/// ZaloPay <c>return_code = 3</c> (đang xử lý) → bỏ qua lượt này, không đoán — y hệt tinh thần
/// "cổng không trả lời thì không đổi trạng thái".
/// </summary>
public class PaymentReconciliationService : IPaymentReconciliationService
{
    /// <summary>Ngưỡng quá hạn — IPN bình thường về trong vài giây; 5 phút tránh hỏi cổng trùng lúc IPN đang bay.</summary>
    public static readonly TimeSpan OverdueThreshold = TimeSpan.FromMinutes(5);

    private readonly AppDbContext _db;
    private readonly IMoMoGatewayService _momo;
    private readonly IZaloPayGatewayService _zaloPay;
    private readonly IPaymentSettlementService _settlement;
    private readonly IPaymentTicketIssuer _issuer;
    private readonly ILogger<PaymentReconciliationService> _logger;

    public PaymentReconciliationService(
        AppDbContext db,
        IMoMoGatewayService momo,
        IZaloPayGatewayService zaloPay,
        IPaymentSettlementService settlement,
        IPaymentTicketIssuer issuer,
        ILogger<PaymentReconciliationService> logger)
    {
        _db = db;
        _momo = momo;
        _zaloPay = zaloPay;
        _settlement = settlement;
        _issuer = issuer;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> SweepOnceAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        var cutoff = now - OverdueThreshold;

        var pending = await _db.Payments
            .AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Pending && p.CreatedAt <= cutoff)
            .ToListAsync(cancellationToken);

        var processed = 0;

        foreach (var payment in pending)
        {
            try
            {
                if (await SweepOneAsync(payment, now, cancellationToken))
                {
                    processed++;
                }
            }
            catch (Exception ex)
            {
                // Một giao dịch hỏng (cổng không trả lời, CSDL chập chờn) không được kéo sập cả
                // vòng quét — ghi log rồi đi tiếp; lượt sau hỏi lại chính giao dịch này.
                _logger.LogError(ex,
                    "Đối soát giao dịch {PaymentCode}: lỗi ở lượt quét — bỏ qua, lượt sau thử lại.",
                    payment.PaymentCode);
            }
        }

        return processed;
    }

    /// <summary>Hỏi và chốt MỘT giao dịch. Trả true khi cổng đã trả lời và cửa chốt đã chạy.</summary>
    private async Task<bool> SweepOneAsync(
        Payment payment,
        DateTime now,
        CancellationToken cancellationToken)
    {
        PaymentCallbackResult callback;
        string providerCode;

        switch (payment.MethodCode)
        {
            case PaymentProviderCodes.MoMo:
            {
                var query = await _momo.QueryTransactionAsync(payment.PaymentCode, cancellationToken);

                providerCode = PaymentProviderCodes.MoMo;
                callback = new PaymentCallbackResult
                {
                    IsValid = true,
                    Succeeded = query.Success,
                    PaymentCode = payment.PaymentCode,
                    // Thành công → số tiền cổng xác nhận (phép đối chiếu amount thật); thất bại →
                    // số tiền của bản ghi (cổng không có gì để đối chiếu — xem doc-comment đầu file).
                    Amount = query.Success ? query.Amount : payment.Amount,
                    GatewayTransactionId = query.TransId > 0
                        ? query.TransId.ToString(CultureInfo.InvariantCulture)
                        : string.Empty,
                    ProviderResponseCode = query.ResultCode.ToString(CultureInfo.InvariantCulture),
                    Message = query.Message,
                };
                break;
            }

            case PaymentProviderCodes.ZaloPay:
            {
                var query = await _zaloPay.QueryOrderAsync(
                    ZaloPayGatewayService.BuildAppTransId(payment.PaymentCode, payment.CreatedAt),
                    cancellationToken);

                // "Đang xử lý" là câu trả lời chưa ngã ngũ — không đoán, không chốt, lượt sau hỏi lại.
                if (query.IsProcessing)
                {
                    return false;
                }

                providerCode = PaymentProviderCodes.ZaloPay;
                callback = new PaymentCallbackResult
                {
                    IsValid = true,
                    Succeeded = query.Success,
                    PaymentCode = payment.PaymentCode,
                    Amount = query.Success ? query.Amount : payment.Amount,
                    GatewayTransactionId = query.ZpTransId > 0
                        ? query.ZpTransId.ToString(CultureInfo.InvariantCulture)
                        : string.Empty,
                    ProviderResponseCode = query.ReturnCode.ToString(CultureInfo.InvariantCulture),
                    Message = query.Message,
                };
                break;
            }

            default:
                // VNPay không có API hỏi trạng thái — kết quả của nó chỉ đến qua Return URL / IPN.
                return false;
        }

        var settle = await _settlement.SettleAsync(providerCode, callback, cancellationToken);

        if (!settle.Success)
        {
            // Invalid/NotFound của cửa chốt: dữ liệu lệch không tự vá được (chuyện phải LỘ ra để
            // người xử lý, không phải lỗi vòng quét). Ghi log thay vì im lặng.
            _logger.LogWarning(
                "Đối soát giao dịch {PaymentCode}: cửa chốt từ chối ({ErrorKind}) — {Error}",
                payment.PaymentCode, settle.ErrorKind, settle.Error);
            return true;
        }

        if (settle.Data!.SettledNow && settle.Data.Status == PaymentStatus.Success && settle.Data.ShouldIssueTickets)
        {
            // Lượt chốt này vừa lật Success → phát hành vé y hệt nhánh callback. Hỏng ở đây ghi log
            // rồi bỏ qua: bản gửi lại của callback (hoặc lượt quét sau) sẽ cứu vé nhờ idempotency.
            try
            {
                await _issuer.IssueAsync(settle.Data.PaymentCode, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Đối soát giao dịch {PaymentCode}: phát hành vé lỗi — bỏ qua, lượt sau thử lại.",
                    payment.PaymentCode);
            }
        }

        return true;
    }
}
