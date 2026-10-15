using SmartBus.Api.Dtos.Refunds;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// 🔴 Bản cài đặt TẠM của <see cref="IRefundGatewayCaller"/> — luôn trả
/// <see cref="RefundLogResult.Unresolved"/> và không bao giờ nói tiền đã đi.
///
/// Vì sao cố ý "không làm gì" thay vì trả kết quả giả: một bản giả trả <c>Succeeded</c> là
/// <b>sổ sách ghi đã hoàn tiền trong khi tiền chưa rời đâu</b> — đúng kiểu hỏng im lặng mà cả
/// doc-comment <see cref="RefundLogResult"/> dựng ra để chặn. Trả <c>Failed</c> cũng sai không
/// kém: nó mở đường cho job thử lại gọi liên tục những lượt chắc chắn không đi tới đâu. Chỉ
/// <c>Unresolved</c> là câu trả lời trung thực của "chưa nối cổng thật": khoản hoàn nằm nguyên ở
/// <see cref="RefundStatus.Pending"/>, không lượt tự động nào chạm tới, và nó lộ ra ở bước đối
/// soát (dòng 43).
///
/// Đăng ký trong DI (Program.cs) — nối cổng thật (task dòng 29, Hiếu) là thay đúng dòng đó:
/// <c>AddScoped&lt;IRefundGatewayCaller, PlaceholderRefundGatewayCaller&gt;()</c>.
/// </summary>
public class PlaceholderRefundGatewayCaller : IRefundGatewayCaller
{
    private readonly ILogger<PlaceholderRefundGatewayCaller> _logger;

    public PlaceholderRefundGatewayCaller(ILogger<PlaceholderRefundGatewayCaller> logger) =>
        _logger = logger;

    /// <inheritdoc />
    public Task<RefundGatewayCallOutcome> CallAsync(
        RefundGatewayCallRequest request,
        CancellationToken cancellationToken = default)
    {
        // ⚠️ Cảnh báo mỗi lượt gọi, cố ý: nếu dòng này xuất hiện trên môi trường thật nghĩa là
        // cổng hoàn tiền chưa được nối và tiền của khách đang nằm chờ ở bước đối soát.
        _logger.LogWarning(
            "Chưa nối cổng hoàn tiền thật (chờ task dòng 29). Lượt gọi cho khoản {RefundCode} " +
            "trả Unresolved — khoản hoàn giữ nguyên Pending.",
            request.RefundCode);

        return Task.FromResult(new RefundGatewayCallOutcome
        {
            Result = RefundLogResult.Unresolved,
            Message = "Chưa nối cổng hoàn tiền thật (task dòng 29).",
            GatewayTransactionId = null,
        });
    }
}
