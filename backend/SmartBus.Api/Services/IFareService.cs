using SmartBus.Api.Dtos.Fares;

namespace SmartBus.Api.Services;

/// <summary>
/// Nghiệp vụ bảng giá vé theo tuyến và theo đối tượng ưu đãi (US 12) — Phùng Duy Hoàng.
///
/// Việc chặn người không phải Admin/Quản lý là của <see cref="RbacPolicies.ManagerOrAbove"/>
/// gắn ở Controller, không lặp lại ở đây: Service không biết ai đang gọi.
///
/// Mọi thao tác đều đi qua tuyến — <c>routeId</c> là một phần của định danh, không phải bộ lọc.
/// </summary>
public interface IFareService
{
    /// <summary>
    /// Bảng giá của một tuyến, xếp theo thứ tự khai báo của <c>PassengerType</c>.
    /// Tuyến chưa có dòng giá nào trả về danh sách rỗng, KHÔNG phải 404.
    /// </summary>
    Task<ServiceResult<IReadOnlyList<FareResponse>>> ListByRouteAsync(
        Guid routeId,
        CancellationToken cancellationToken = default);

    /// <summary>Một dòng giá. Tuyến không tồn tại hoặc dòng giá không thuộc tuyến đều là 404.</summary>
    Task<ServiceResult<FareResponse>> GetByIdAsync(
        Guid routeId,
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Thêm một dòng giá cho tuyến. Trùng cặp (tuyến, đối tượng) là xung đột — mỗi đối tượng
    /// chỉ có đúng một giá trên một tuyến, đã ràng buộc bằng unique index ở CSDL.
    /// </summary>
    Task<ServiceResult<FareResponse>> CreateAsync(
        Guid routeId,
        CreateFareRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Sửa giá của một dòng đã có. Chỉ giá, không đổi đối tượng.</summary>
    Task<ServiceResult<FareResponse>> UpdateAsync(
        Guid routeId,
        Guid id,
        UpdateFareRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Xoá hẳn dòng giá. Bảng Fares không bị bảng nào tham chiếu tới nên xoá cứng được;
    /// không có cột IsDeleted theo quy ước A4.
    /// </summary>
    Task<ServiceResult<bool>> DeleteAsync(
        Guid routeId,
        Guid id,
        CancellationToken cancellationToken = default);
}
