using SmartBus.Api.Dtos.Tickets;

namespace SmartBus.Api.Services;

/// <summary>
/// Tra cứu vé điện tử của chính người gọi — GET /api/tickets/me và GET /api/tickets/{id}
/// (US 4, task *"API tra cứu vé của tôi (đã mua / đã dùng / đã huỷ)"* — Trần Trung Hiếu, dòng 45).
/// Hợp đồng đầy đủ ở mục "Vé điện tử" của docs/api-contract.md.
/// </summary>
public interface IMyTicketsService
{
    /// <summary>
    /// Toàn bộ vé của một người — mọi trạng thái (Paid / Used / Cancelled), mảng trần, không phân
    /// trang. Không có vé nào thì mảng rỗng, không phải lỗi.
    /// </summary>
    Task<IReadOnlyList<TicketResponse>> ListAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Một vé theo id, nhưng chỉ khi vé thuộc <paramref name="userId"/> — vé không tồn tại hoặc
    /// của người khác đều là NotFound (404), không để lộ vé nào đang tồn tại.
    /// </summary>
    Task<ServiceResult<TicketResponse>> GetByIdAsync(Guid userId, Guid id, CancellationToken cancellationToken = default);
}
