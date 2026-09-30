using SmartBus.Api.Dtos.Buses;

namespace SmartBus.Api.Services;

/// <summary>
/// Nghiệp vụ đội xe — CRUD /api/buses, story 14, Trần Trung Hiếu.
/// Hợp đồng đầy đủ ở mục "Xe buýt — /buses" của docs/api-contract.md.
/// </summary>
public interface IBusService
{
    /// <summary>Danh sách xe, lọc theo từ khóa/trạng thái và phân trang.</summary>
    Task<ServiceResult<BusListResponse>> ListAsync(
        ListBusesRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<BusResponse>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ServiceResult<BusResponse>> CreateAsync(
        CreateBusRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<BusResponse>> UpdateAsync(
        Guid id,
        UpdateBusRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Xoá mềm — chuyển xe về Inactive (quy ước A4), không xoá dữ liệu: còn chuyến cũ
    /// tham chiếu tới xe (A5 đặt Restrict nên xoá cứng cũng không được phép).
    /// Trả về xe đã ngừng khai thác để màn hình cập nhật luôn dòng đang hiển thị.
    /// </summary>
    Task<ServiceResult<BusResponse>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
