using SmartBus.Api.Dtos.Routes;

namespace SmartBus.Api.Services;

/// <summary>
/// Nghiệp vụ tuyến đường — CRUD /api/routes, story 12, Trần Trung Hiếu.
/// Hợp đồng đầy đủ ở mục "Tuyến đường — /routes" của docs/api-contract.md.
/// </summary>
public interface IRouteService
{
    /// <summary>Danh sách tuyến, lọc theo từ khóa/trạng thái và phân trang.</summary>
    Task<ServiceResult<RouteListResponse>> ListAsync(
        ListRoutesRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<RouteResponse>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ServiceResult<RouteResponse>> CreateAsync(
        CreateRouteRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<RouteResponse>> UpdateAsync(
        Guid id,
        UpdateRouteRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Xoá mềm — chuyển tuyến về Inactive (quy ước A4), không xoá dữ liệu.
    /// Trả về tuyến đã ngừng khai thác để màn hình cập nhật luôn dòng đang hiển thị.
    /// </summary>
    Task<ServiceResult<RouteResponse>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
