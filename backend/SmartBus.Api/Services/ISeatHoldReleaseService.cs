using SmartBus.Api.Dtos.SeatHolds;

namespace SmartBus.Api.Services;

/// <summary>
/// Nhả ghế khi khách huỷ thao tác giữ chỗ theo mã phiên —
/// POST /api/seat-holds/{sessionCode}/release (US 3).
/// Task *"API nhả ghế khi hết hạn hoặc khách huỷ thao tác"* — Phùng Duy Hoàng.
/// Hợp đồng đầy đủ ở mục "Giữ chỗ — /seat-holds" của docs/api-contract.md.
///
/// Yêu cầu đăng nhập nhưng không policy vai trò — hành khách tự nhả phiên giữ chỗ của mình,
/// quyền sở hữu kiểm ngay trong truy vấn theo UserId.
///
/// Vế "hết hạn" của tên task KHÔNG nằm ở đây: job nền `SeatHoldExpiryBackgroundService`
/// tự lật Holding → Expired khi quá ExpiresAt (docs/26 §4). Endpoint này là vế còn lại — khách
/// chủ động huỷ — nhưng vẫn nhận cả phiên vừa quá hạn mà job chưa quét (cố ý không kiểm ExpiresAt).
/// </summary>
public interface ISeatHoldReleaseService
{
    /// <summary>
    /// Nhả toàn bộ lượt giữ của phiên: lật nhóm dòng cùng <paramref name="sessionCode"/> đang
    /// Holding sang Released + ghi một dòng log Released cho mỗi lượt. Chỉ tìm trong phiên của
    /// <paramref name="userId"/> — phiên không tồn tại và phiên của người khác cùng là
    /// <see cref="ServiceResult{T}.NotFound"/>. Phiên đã Expired / Released từ trước trả về đúng
    /// trạng thái hiện tại (thao tác kết thúc, gọi lại vẫn thành công); phiên đã Confirmed là
    /// <see cref="ServiceResult{T}.Conflict"/> — không nhả phần đã bán.
    /// </summary>
    Task<ServiceResult<SeatHoldSessionResponse>> ReleaseAsync(
        Guid userId,
        string sessionCode,
        CancellationToken cancellationToken = default);
}
