using SmartBus.Api.Dtos.Trips;

namespace SmartBus.Api.Services;

/// <summary>
/// Sơ đồ ghế theo chuyến cho hành khách — GET /api/trips/{id}/seats (US 2 "Chọn vị trí ghế").
/// Task *"API lấy sơ đồ ghế theo chuyến + trạng thái từng ghế"* — Trần Trung Hiếu.
/// Hợp đồng đầy đủ ở mục "GET /trips/{id}/seats" của docs/api-contract.md.
///
/// Chỉ đọc, công khai có chủ đích: hành khách xem sơ đồ để chọn ghế trước khi đăng nhập,
/// đăng nhập là bước của API giữ ghế (US 3).
/// </summary>
public interface ITripSeatMapService
{
    /// <summary>
    /// Trả sơ đồ ghế của một chuyến kèm trạng thái từng ghế (Available / Held / Paid).
    /// Chuyến không tồn tại hoặc xe của chuyến đã bị xoá → <see cref="ServiceResult{T}.NotFound"/>.
    /// </summary>
    Task<ServiceResult<TripSeatMapResponse>> GetSeatMapAsync(
        Guid tripId,
        CancellationToken cancellationToken = default);
}
