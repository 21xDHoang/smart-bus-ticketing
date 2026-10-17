using SmartBus.Api.Dtos.Trips;

namespace SmartBus.Api.Services;

/// <summary>
/// Sinh chuyến hàng loạt và tự chia vòng tròn cho nhiều xe — nợ của đợt mở rộng đội xe 17/10/2026.
///
/// <para>
/// Vì sao là service RIÊNG thay vì thêm phương thức vào <see cref="IRouteTripsService"/>: file đó
/// của Trần Trung Hiếu (luật E1), và hình dạng request/response khác hẳn
/// <see cref="RouteTripsService.GenerateAsync"/> (một xe, không có bảng tải). Endpoint
/// <c>POST /routes/{routeId}/trips/generate</c> cũ vẫn giữ nguyên hành vi một xe để không phá người
/// đang gọi; màn hình "Cấu hình tần suất" chuyển sang endpoint mới.
/// </para>
///
/// <para>
/// Hợp đồng đầy đủ ở mục "POST /routes/{routeId}/trips/generate-fleet" của docs/api-contract.md.
/// </para>
/// </summary>
public interface IFleetTripGenerationService
{
    /// <summary>
    /// Sinh các chuyến cách đều <c>frequencyMinutes</c> trong <c>[startTime, endTime]</c>, mỗi chuyến
    /// giao cho xe RẢNH đầu tiên theo vòng tròn <c>busIds</c>. Nguyên tử: một giờ không còn xe rảnh
    /// thì không chuyến nào được ghi. Giờ đến được chốt ngay khi tạo (cùng công thức ước lượng của
    /// <see cref="ArrivalEstimatingRouteTripsService.EstimateTravelMinutes"/>).
    /// </summary>
    Task<ServiceResult<GenerateFleetTripsResponse>> GenerateAsync(
        Guid routeId,
        GenerateFleetTripsRequest request,
        CancellationToken cancellationToken = default);
}
