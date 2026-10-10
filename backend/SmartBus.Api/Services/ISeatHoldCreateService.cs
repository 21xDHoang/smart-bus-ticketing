using SmartBus.Api.Dtos.SeatHolds;

namespace SmartBus.Api.Services;

/// <summary>
/// Giữ ghế tạm thời theo phiên — POST /api/seat-holds (US 3 "Giữ chỗ tạm thời").
/// Task *"API giữ ghế tạm thời (khóa ghế theo phiên)"* — Nguyễn Duy Kiên.
/// Hợp đồng đầy đủ ở mục "Giữ chỗ — /seat-holds" của docs/api-contract.md.
///
/// Đây là endpoint SINH mã phiên của cả bề mặt: ba endpoint kia (tra trạng thái, gia hạn, nhả ghế)
/// đều hỏi theo <c>sessionCode</c> do lần gọi này trả về.
///
/// Yêu cầu đăng nhập nhưng không policy vai trò — hành khách tự giữ ghế cho mình; người giữ là
/// <paramref name="userId"/> lấy từ claim <c>NameIdentifier</c>, không có trong body.
/// </summary>
public interface ISeatHoldCreateService
{
    /// <summary>
    /// Giữ tạm <see cref="CreateSeatHoldRequest.SeatIds"/> của chuyến trong 10 phút và trả về phiên
    /// vừa tạo (<c>status = "Holding"</c>, <c>canExtend = true</c>).
    ///
    /// Ghế đã có người giữ (<c>Status = 'Holding'</c>) → <see cref="ServiceResult{T}.Conflict"/>;
    /// ghế không thuộc xe của chuyến hoặc danh sách ghế không hợp lệ →
    /// <see cref="ServiceResult{T}.Invalid"/> kèm lỗi theo trường <c>seatIds</c>; chuyến không tồn
    /// tại → <see cref="ServiceResult{T}.NotFound"/>; chuyến đã huỷ hoặc đã chạy xong →
    /// <see cref="ServiceResult{T}.Conflict"/>.
    /// </summary>
    Task<ServiceResult<SeatHoldSessionResponse>> CreateAsync(
        Guid userId,
        CreateSeatHoldRequest request,
        CancellationToken cancellationToken = default);
}
