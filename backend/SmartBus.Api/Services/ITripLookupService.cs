using SmartBus.Api.Dtos.Trips;

namespace SmartBus.Api.Services;

/// <summary>
/// Tra cứu danh sách chuyến theo ngày + lọc theo tuyến (GET /api/trips) — story 13,
/// Phùng Duy Hoàng. Hợp đồng đầy đủ ở mục "Chuyến xe — /trips" của docs/api-contract.md.
///
/// Phần việc này CHỈ ĐỌC: không có thao tác ghi nào, nên không có SaveChangesAsync và không có
/// try/catch DbUpdateException như các service CRUD khác — không có gì để xung đột.
///
/// Cố ý đứng riêng thay vì nối vào <see cref="ITripService"/> (Dăm — chi tiết chuyến) hay
/// <see cref="IRouteTripsService"/> (Hiếu — lịch trình theo tuyến): hai bề mặt khác nhau, hai task
/// thuộc hai người — cùng khuôn cặp <see cref="IStopService"/> (Hiếu) /
/// <see cref="IRouteStopService"/> (Kiên) ở Sprint 1. Ở đây <c>routeId</c> là BỘ LỌC bỏ trống được
/// (không có = mọi tuyến), khác <c>routeId</c> của /routes/{routeId}/trips là một phần định danh.
///
/// Việc chặn người không phải Admin/Quản lý là của <see cref="RbacPolicies.ManagerOrAbove"/> gắn ở
/// Controller, không lặp lại ở đây — Service không biết ai đang gọi.
/// </summary>
public interface ITripLookupService
{
    /// <summary>
    /// Danh sách chuyến khớp bộ lọc, phân trang, xếp theo giờ khởi hành tăng dần.
    ///
    /// <paramref name="routeId"/> không trỏ tới tuyến nào → 404 — tham chiếu cứng tới một tuyến,
    /// không phải bộ lọc mềm như <c>status</c> của <paramref name="request"/> (mã lạ trả danh sách
    /// rỗng). <c>to</c> sớm hơn <c>from</c> → 400.
    /// </summary>
    Task<ServiceResult<TripLookupListResponse>> ListAsync(
        Guid? routeId,
        ListTripsRequest request,
        CancellationToken cancellationToken = default);
}
