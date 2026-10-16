using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Payments;
using SmartBus.Api.Dtos.Refunds;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt THẬT của <see cref="IRefundGatewayCaller"/> — task *"API gọi cổng thanh toán để hoàn tiền
/// (MoMo/VNPay/ZaloPay)"* (Sprint 4 dòng 29 — Trần Trung Hiếu). Thay
/// <c>PlaceholderRefundGatewayCaller</c> trong đăng ký DI của Program.cs (đúng một dòng — hợp đồng đã
/// ghi sẵn ở <see cref="IRefundGatewayCaller"/>).
///
/// Việc của lớp này, theo đúng hợp đồng của seam:
/// <list type="number">
/// <item>Đọc lại khoản hoàn + giao dịch gốc từ CSDL — CHỈ đọc: mọi lượt ghi trạng thái là việc của
/// <see cref="RefundAttemptService"/> (bên đã giành dòng bằng xmin trước khi gọi tới đây);</item>
/// <item>Chặn các ca KHÔNG gọi cổng được và có câu trả lời chắc chắn → <c>Failed</c>: khoản hoàn
/// không còn, số tiền có phần lẻ (cả ba cổng chỉ nhận VND nguyên), phương thức không có trong bảng
/// tra. Thử lại cũng không đổi nên KHÔNG được coi là <c>Unresolved</c>;</item>
/// <item>Tra client cổng theo <c>MethodCode</c> của GIAO DỊCH GỐC (docs/40 §2.5: hoàn tiền luôn quay
/// về đúng kênh đã thu — không có tham số chọn kênh từ người dùng);</item>
/// <item>Bọc lỗi vận chuyển → <c>Unresolved</c>: cổng không trả lời thì không ai được đoán, khoản giữ
/// nguyên <c>Pending</c> cho bước đối soát (dòng 43) tra sao kê. Trừ lúc app đang tắt — ném tiếp để
/// tầng trên dừng đúng cách (đã ghi ở <see cref="IRefundGatewayCaller"/>).</item>
/// </list>
///
/// Tra client qua <see cref="IServiceProvider"/> theo mã (đúng lối <see cref="PaymentGatewayResolver"/>):
/// máy chỉ cấu hình một cổng thì chỉ client cổng đó được dựng — client cổng khác ném lỗi thiếu khoá
/// (luật 2) không kéo sập cả dây chuyền.
/// </summary>
public class RefundGatewayCaller : IRefundGatewayCaller
{
    /// <summary>
    /// Bảng đăng ký mã phương thức → client hoàn tiền — NGUỒN DUY NHẤT của phép tra, cùng lối bảng
    /// của <see cref="PaymentGatewayResolver"/>. BankCard là kênh thẻ của VNPay
    /// (<c>BankCardGatewayAdapter</c> ghim <c>vnp_BankCode</c>) nên lệnh hoàn cũng đi qua API hoàn
    /// của VNPay. Thêm cổng mới: viết client rồi thêm đúng một dòng vào đây + một dòng AddHttpClient
    /// trong Program.cs.
    /// </summary>
    private static readonly Dictionary<string, Type> Registered = new(StringComparer.OrdinalIgnoreCase)
    {
        [PaymentProviderCodes.MoMo] = typeof(MoMoRefundClient),
        [PaymentProviderCodes.VnPay] = typeof(VnPayRefundClient),
        [PaymentProviderCodes.ZaloPay] = typeof(ZaloPayRefundClient),
        [PaymentProviderCodes.BankCard] = typeof(VnPayRefundClient),
    };

    private readonly AppDbContext _db;
    private readonly IServiceProvider _services;

    public RefundGatewayCaller(AppDbContext db, IServiceProvider services)
    {
        _db = db;
        _services = services;
    }

    /// <inheritdoc />
    public async Task<RefundGatewayCallOutcome> CallAsync(
        RefundGatewayCallRequest request,
        CancellationToken cancellationToken = default)
    {
        var payment = await _db.Refunds
            .Where(r => r.Id == request.RefundId)
            .Select(r => r.Payment)
            .FirstOrDefaultAsync(cancellationToken);

        // Khoản hoàn không còn (hoặc giao dịch gốc mất — dữ liệu hỏng): không có gì để gọi.
        // Câu trả lời chắc chắn → Failed, không phải Unresolved.
        if (payment is null)
        {
            return new RefundGatewayCallOutcome
            {
                Result = RefundLogResult.Failed,
                Message = "Không tìm thấy khoản hoàn hoặc giao dịch gốc để gọi cổng hoàn tiền.",
            };
        }

        // Cả ba cổng chỉ nhận VND nguyên (MoMo/ZaloPay khai amount là Long, VNPay nhân 100 thành số
        // nguyên) — phần lẻ nghĩa là dữ liệu khoản hoàn sai, thử lại cũng không đổi → Failed.
        if (request.Amount != Math.Truncate(request.Amount))
        {
            return new RefundGatewayCallOutcome
            {
                Result = RefundLogResult.Failed,
                Message = $"Số tiền hoàn {request.Amount} có phần lẻ — các cổng chỉ nhận VND nguyên.",
            };
        }

        if (!Registered.TryGetValue(request.MethodCode, out var clientType))
        {
            return new RefundGatewayCallOutcome
            {
                Result = RefundLogResult.Failed,
                Message = $"Phương thức thanh toán '{request.MethodCode}' không hỗ trợ hoàn tiền qua cổng.",
            };
        }

        // Dựng MUỘN có chủ đích (cùng lối PaymentGatewayResolver): chỉ cổng được hỏi mới chạm tới
        // client của nó và cấu hình của nó.
        var client = (IRefundGatewayClient)_services.GetRequiredService(clientType);

        try
        {
            return await client.RefundAsync(request, payment, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // App đang tắt giữa lượt gọi: KHÔNG đoán, KHÔNG ghi. Khoản nằm nguyên Pending và bước
            // đối soát (dòng 43) sẽ tra với sao kê cổng. Ném tiếp để tầng trên dừng đúng cách.
            throw;
        }
        catch (OperationCanceledException)
        {
            // Timeout nội bộ của client (token ngoài KHÔNG bị huỷ): cổng không trả lời kịp.
            return Unresolved("Cổng hoàn tiền không trả lời kịp (hết thời gian chờ).");
        }
        catch (Exception ex)
        {
            // Mọi kiểu hỏng vận chuyển khác — HTTP chết, đứt kết nối giữa chừng — là "cổng không trả
            // lời". KHÔNG đoán thành thất bại: Failed mở đường cho job thử lại gọi một khoản có thể
            // đã hoàn (hoàn hai lần), Succeeded là nói dối (tiền chưa chắc đã đi).
            return Unresolved($"Cổng hoàn tiền không trả lời: {ex.Message}");
        }
    }

    private static RefundGatewayCallOutcome Unresolved(string message) => new()
    {
        Result = RefundLogResult.Unresolved,
        Message = message,
    };
}
