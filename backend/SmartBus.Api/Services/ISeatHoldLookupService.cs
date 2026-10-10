using SmartBus.Api.Dtos.SeatHolds;

namespace SmartBus.Api.Services;

/// <summary>
/// Kiểm tra trạng thái giữ chỗ theo mã phiên — GET /api/seat-holds/{sessionCode} (US 3).
/// Task *"API kiểm tra trạng thái giữ chỗ theo mã phiên"* — Trần Trung Hiếu.
/// Hợp đồng đầy đủ ở mục "Giữ chỗ — /seat-holds" của docs/api-contract.md.
///
/// Chỉ đọc, yêu cầu đăng nhập nhưng không policy vai trò — hành khách tự xem phiên giữ chỗ của
/// mình, quyền sở hữu kiểm ngay trong truy vấn theo UserId.
/// </summary>
public interface ISeatHoldLookupService
{
    /// <summary>
    /// Trả phiên giữ chỗ theo mã phiên. Chỉ tìm trong phiên của <paramref name="userId"/> —
    /// phiên không tồn tại và phiên của người khác cùng là <see cref="ServiceResult{T}.NotFound"/>.
    /// </summary>
    Task<ServiceResult<SeatHoldSessionResponse>> GetBySessionCodeAsync(
        Guid userId,
        string sessionCode,
        CancellationToken cancellationToken = default);
}
