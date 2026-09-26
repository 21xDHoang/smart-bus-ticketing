using SmartBus.Api.Dtos.RouteStops;

namespace SmartBus.Api.Services;

/// <summary>
/// Nghiệp vụ gán trạm vào tuyến và sắp xếp lại thứ tự trạm (story 12) — Nguyễn Duy Kiên.
/// Hợp đồng đầy đủ ở mục "Trạm trên tuyến — /routes/{routeId}/stops" của docs/api-contract.md.
///
/// Việc chặn người không phải Admin/Quản lý là của <see cref="RbacPolicies.ManagerOrAbove"/>
/// gắn ở Controller, không lặp lại ở đây — Service không biết ai đang gọi.
///
/// Mọi thao tác đều đi qua tuyến: routeId là một phần của định danh chứ không phải bộ lọc,
/// nên tuyến không tồn tại luôn là 404 chứ không phải "danh sách rỗng".
/// </summary>
public interface IRouteStopService
{
    /// <summary>
    /// Danh sách trạm của tuyến, xếp theo thứ tự chạy.
    /// Tuyến chưa gán trạm nào trả về danh sách RỖNG, không phải 404 — chỉ tuyến không tồn
    /// tại mới là 404.
    /// </summary>
    Task<ServiceResult<IReadOnlyList<RouteStopResponse>>> ListByRouteAsync(
        Guid routeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gán một trạm vào CUỐI tuyến. Trạm đã nằm trên tuyến là xung đột — mỗi cặp
    /// (tuyến, trạm) chỉ có đúng một dòng, đã ràng buộc bằng unique index ở CSDL.
    /// </summary>
    Task<ServiceResult<RouteStopResponse>> AssignAsync(
        Guid routeId,
        AssignStopToRouteRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sắp xếp lại thứ tự toàn bộ trạm của tuyến. Mảng gửi lên phải khớp ĐÚNG tập trạm hiện
    /// có: thiếu, thừa, trùng hoặc rỗng đều bị từ chối để một lần kéo-thả lỗi không âm thầm
    /// gỡ mất trạm.
    /// </summary>
    Task<ServiceResult<IReadOnlyList<RouteStopResponse>>> ReorderAsync(
        Guid routeId,
        ReorderRouteStopsRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gỡ một trạm khỏi tuyến rồi dồn số các trạm còn lại thành 1..N liên tục, để thứ tự
    /// không bao giờ có lỗ hổng.
    ///
    /// <paramref name="id"/> là khoá của DÒNG bảng nối (RouteStop.Id — giá trị <c>id</c> mà
    /// GET/POST/PUT trả về cho từng dòng), KHÔNG phải <c>StopId</c>. Tên tham số phải là
    /// <c>id</c> vì <see cref="AuditLogMiddleware"/> dựa vào đúng tên đó để ghi id đối tượng
    /// vào Target của nhật ký hoạt động.
    /// </summary>
    Task<ServiceResult<bool>> RemoveAsync(
        Guid routeId,
        Guid id,
        CancellationToken cancellationToken = default);
}
