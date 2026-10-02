using SmartBus.Api.Dtos.Trips;

namespace SmartBus.Api.Services;

/// <summary>
/// Tìm chuyến cho hành khách (GET /api/trips/search) — story 1, Phùng Duy Hoàng. Hợp đồng đầy đủ
/// ở mục "GET /trips/search" của docs/api-contract.md.
///
/// Phần việc này CHỈ ĐỌC: không có thao tác ghi nào, nên không có <c>SaveChangesAsync</c> và không
/// có <c>try/catch DbUpdateException</c> như các service CRUD khác — không có gì để xung đột.
///
/// Cố ý đứng riêng thay vì nối vào <see cref="ITripLookupService"/> (cùng người làm nhưng khác
/// nghiệp vụ) hay <see cref="ITripService"/> (Dăm — chi tiết chuyến): mỗi bề mặt một service,
/// cùng khuôn cặp <see cref="IStopService"/> (Hiếu) / <see cref="IRouteStopService"/> (Kiên) ở
/// Sprint 1. Khác <see cref="ITripLookupService"/> — thời gian biểu phân trang cho màn hình điều
/// hành — service này nhận thẳng <c>routeId</c> đã chọn, chỉ trả chuyến <c>Scheduled</c> kèm giá
/// vé phổ thông và số ghế còn trống, không phân trang.
///
/// Endpoint là API CÔNG KHAI (không gắn <c>[Authorize]</c>): story 1 là luồng tra cứu của hành
/// khách trước khi đăng nhập, nên service cũng không biết ai đang gọi.
/// </summary>
public interface ITripSearchService
{
    /// <summary>
    /// Danh sách chuyến <c>Scheduled</c> của một tuyến trong khoảng thời gian, xếp theo giờ khởi
    /// hành tăng dần.
    ///
    /// Thiếu <c>routeId</c> → 400 (chặn ở Controller bằng <c>[Required]</c>);
    /// <c>routeId</c> không trỏ tới tuyến nào → 404 — tham chiếu cứng, cùng câu trả lời của
    /// <c>GET /trips</c>. <c>to</c> sớm hơn <c>from</c> → 400.
    /// </summary>
    Task<ServiceResult<IReadOnlyList<TripSearchResult>>> SearchAsync(
        SearchTripsRequest request,
        CancellationToken cancellationToken = default);
}
