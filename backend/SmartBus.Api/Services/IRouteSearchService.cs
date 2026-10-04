using SmartBus.Api.Dtos.Routes;

namespace SmartBus.Api.Services;

/// <summary>
/// Tìm tuyến cho hành khách (GET /api/routes/search) — story 1, Trần Trung Hiếu. Hợp đồng đầy đủ
/// ở mục "Tra cứu tuyến — /routes/search" của docs/api-contract.md.
///
/// Phần việc này CHỈ ĐỌC: không có thao tác ghi nào, nên không có <c>SaveChangesAsync</c> và không
/// có <c>try/catch DbUpdateException</c> như các service CRUD khác — không có gì để xung đột.
///
/// Cố ý đứng riêng thay vì nối vào <see cref="IRouteService"/> (CRUD tuyến của quản lý, cùng
/// người làm) hay <see cref="IRouteStopService"/> (trạm trên tuyến — Kiên): mỗi bề mặt một
/// service. Endpoint là API CÔNG KHAI (không gắn <c>[Authorize]</c>): story 1 là luồng tra cứu
/// của hành khách trước khi đăng nhập, nên service cũng không biết ai đang gọi.
/// </summary>
public interface IRouteSearchService
{
    /// <summary>
    /// Tuyến <c>Active</c> khớp điểm đi/điểm đến: chứa trạm có tên khớp <paramref name="request"/>.
    /// <c>Origin</c> và trạm có tên khớp <c>Destination</c>, trạm đi đứng trước trạm đến theo
    /// <c>stopOrder</c> — chiều chạy (quy ước A8.6). Có <c>Date</c> thì chỉ giữ tuyến có ít nhất
    /// một chuyến <c>Scheduled</c> khởi hành trong trọn ngày đó giờ Việt Nam.
    ///
    /// Thiếu <c>origin</c>/<c>destination</c> → 400 (chặn ở Controller bằng <c>[Required]</c>,
    /// khoảng trắng chặn ở đây); hai điểm trùng nhau → 400; <c>date</c> sai định dạng → 400.
    /// Không có tuyến nào khớp → mảng rỗng, không phải 404 — bộ lọc mềm.
    /// </summary>
    Task<ServiceResult<IReadOnlyList<RouteSearchResult>>> SearchAsync(
        RouteSearchRequest request,
        CancellationToken cancellationToken = default);
}
