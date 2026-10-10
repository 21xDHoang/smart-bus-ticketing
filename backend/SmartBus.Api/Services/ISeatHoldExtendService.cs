using SmartBus.Api.Dtos.SeatHolds;

namespace SmartBus.Api.Services;

/// <summary>
/// Gia hạn thời gian giữ chỗ theo mã phiên — POST /api/seat-holds/{sessionCode}/extend (US 3).
/// Task *"API gia hạn thời gian giữ chỗ (tối đa 1 lần)"* — Trần Trung Hiếu.
/// Hợp đồng đầy đủ ở mục "Giữ chỗ — /seat-holds" của docs/api-contract.md.
///
/// Yêu cầu đăng nhập nhưng không policy vai trò — hành khách tự gia hạn phiên giữ chỗ của mình,
/// quyền sở hữu kiểm ngay trong truy vấn theo UserId.
/// </summary>
public interface ISeatHoldExtendService
{
    /// <summary>
    /// Gia hạn phiên giữ chỗ thêm 10 phút, tối đa 1 lần cho mỗi phiên (US 3). Chỉ tìm trong phiên
    /// của <paramref name="userId"/> — phiên không tồn tại và phiên của người khác cùng là
    /// <see cref="ServiceResult{T}.NotFound"/>. Trả về phiên với hạn mới; <c>canExtend</c> của kết
    /// quả luôn <c>false</c> vì vừa dùng hết lượt.
    /// </summary>
    Task<ServiceResult<SeatHoldSessionResponse>> ExtendAsync(
        Guid userId,
        string sessionCode,
        CancellationToken cancellationToken = default);
}
