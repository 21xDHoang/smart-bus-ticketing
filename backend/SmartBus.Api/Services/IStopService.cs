using SmartBus.Api.Dtos.Stops;

namespace SmartBus.Api.Services;

/// <summary>
/// Nghiệp vụ trạm dừng — CRUD /api/stops, story 12, Trần Trung Hiếu.
/// Hợp đồng đầy đủ ở mục "Trạm dừng — /stops" của docs/api-contract.md.
/// </summary>
public interface IStopService
{
    /// <summary>Danh sách toàn bộ trạm dừng, xếp theo tên để màn hình quản lý dễ tra cứu.</summary>
    Task<ServiceResult<IReadOnlyList<StopResponse>>> ListAsync(CancellationToken cancellationToken = default);

    Task<ServiceResult<StopResponse>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ServiceResult<StopResponse>> CreateAsync(StopRequest request, CancellationToken cancellationToken = default);

    Task<ServiceResult<StopResponse>> UpdateAsync(Guid id, StopRequest request, CancellationToken cancellationToken = default);

    /// <summary>Xoá hẳn trạm. Trạm đang nằm trên tuyến đường nào đó bị chặn — trả lỗi Conflict.</summary>
    Task<ServiceResult<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
