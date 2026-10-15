using SmartBus.Api.Dtos.Trips;

namespace SmartBus.Api.Services;

/// <summary>
/// Hợp đồng cập nhật vị trí hiện tại của chuyến (task *"API cập nhật vị trí chuyến
/// (CurrentLat/CurrentLng/CurrentStopId)"*, US 10 "Thông báo trạm", Sprint 4 — Trần Trung Hiếu).
///
/// Chỉ TÀI XẾ được phân công của chuyến mới ghi được — quyền sở hữu kiểm ngay trong truy vấn
/// theo <c>DriverId</c> (cùng lối "vé của tôi" của <see cref="IMyTicketsService"/>), controller
/// chỉ cần đảm bảo vai trò Driver. Vị trí ghi vào cột <c>CurrentLat/Lng/CurrentStopId</c> của
/// bảng <c>Trips</c> (quy ước A8.6 — trên Trips, không trên Buses) và làm mới
/// <c>PositionUpdatedAt</c>.
///
/// Dữ liệu ghi ở đây là đầu vào của service "bắn thông báo khi xe cách trạm dưới 500m" (Nguyễn
/// Duy Kiên) cùng story — vì vậy trạm báo lên PHẢI thuộc tuyến của chuyến, không nhận trạm lạ.
/// </summary>
public interface ITripLocationService
{
    /// <summary>
    /// Ghi một ảnh chụp vị trí (toạ độ + trạm gần nhất) vào chuyến. Ném không — mọi nhánh lỗi
    /// trả qua <see cref="ServiceResult{T}"/>: chuyến không tồn tại hoặc không thuộc tài xế này
    /// → NotFound; chuyến đã hoàn thành/huỷ → Conflict; trạm không thuộc tuyến → Invalid.
    /// </summary>
    Task<ServiceResult<TripLocationResponse>> UpdateAsync(
        Guid tripId,
        Guid driverUserId,
        UpdateTripLocationRequest request,
        CancellationToken cancellationToken);
}
