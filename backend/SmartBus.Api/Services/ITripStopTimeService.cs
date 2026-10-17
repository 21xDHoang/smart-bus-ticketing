using SmartBus.Api.Dtos.Trips;

namespace SmartBus.Api.Services;

/// <summary>
/// Giờ đến TỪNG TRẠM của một chuyến. Hợp đồng đầy đủ ở docs/api-contract.md mục
/// "Giờ đến từng trạm — /routes/{routeId}/trips/{id}/stop-times".
///
/// <para>
/// Giờ dự kiến được SUY RA khi đọc (từ <c>RouteStops.DistanceKm</c> + giờ chạy của chuyến),
/// không lưu sẵn. Bảng <c>TripStopTimeOverrides</c> chỉ giữ các mốc quản trị viên sửa tay —
/// nhờ vậy đổi khoảng cách trên tuyến là giờ dự kiến của mọi chuyến tự cập nhật.
/// </para>
///
/// <para>
/// Việc chặn người không phải Admin/Quản lý là của <see cref="RbacPolicies.ManagerOrAbove"/>
/// gắn ở Controller, không lặp lại ở đây — Service không biết ai đang gọi.
/// </para>
/// </summary>
public interface ITripStopTimeService
{
    /// <summary>
    /// Dòng thời gian đầy đủ của chuyến: mọi trạm theo <c>StopOrder</c>, kèm giờ đến đang có
    /// hiệu lực và giờ dự kiến. Chuyến không thuộc tuyến trong đường dẫn → 404.
    /// </summary>
    Task<ServiceResult<TripStopTimesResponse>> GetAsync(
        Guid routeId,
        Guid tripId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ghi đè giờ các trạm TRUNG GIAN, thay thế toàn phần. Mảng rỗng xoá hết mốc ghi đè.
    /// Trạm đầu / bến cuối không nhận ghi đè — hai mốc đó là giờ chạy của chuyến.
    /// Chuyến đã huỷ hoặc đã chạy xong → 409.
    /// </summary>
    Task<ServiceResult<TripStopTimesResponse>> UpdateAsync(
        Guid routeId,
        Guid tripId,
        UpdateTripStopTimesRequest request,
        CancellationToken cancellationToken = default);
}
