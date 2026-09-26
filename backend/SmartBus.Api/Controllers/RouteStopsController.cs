using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.RouteStops;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Gán trạm vào tuyến và sắp xếp lại thứ tự trạm — story 12, Nguyễn Duy Kiên.
/// Hợp đồng đầy đủ ở mục "Trạm trên tuyến — /routes/{routeId}/stops" của docs/api-contract.md.
///
/// Controller riêng thay vì thêm method vào <c>RoutesController</c>: bảng RouteStops là nghiệp vụ
/// của task này, còn <c>RoutesController</c>/<c>RouteService</c> là file của Trần Trung Hiếu —
/// quy ước không cho sửa file của người khác. Cùng lối <c>FaresController</c> đã đi với
/// <c>/api/routes/{routeId}/fares</c>.
///
/// Phân quyền gắn ở cả lớp: story 12 nói "Là quản lý, tôi muốn thêm/sửa/xóa … danh sách trạm
/// dừng", nên Admin và Quản lý dùng được, các vai trò khác nhận 403.
/// </summary>
[ApiController]
[Route("api/routes/{routeId:guid}/stops")]
[Authorize(Policy = RbacPolicies.ManagerOrAbove)]
public class RouteStopsController : ControllerBase
{
    private readonly IRouteStopService _routeStopService;

    public RouteStopsController(IRouteStopService routeStopService)
        => _routeStopService = routeStopService;

    /// <summary>
    /// Danh sách trạm của tuyến, xếp theo thứ tự xe chạy.
    /// Tuyến chưa gán trạm nào trả về mảng rỗng chứ không phải 404.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ListByRoute(Guid routeId, CancellationToken cancellationToken)
    {
        var result = await _routeStopService.ListByRouteAsync(routeId, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>
    /// Gán một trạm vào cuối tuyến. Trạm đã nằm trên tuyến là 409.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Assign(
        Guid routeId,
        [FromBody] AssignStopToRouteRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _routeStopService.AssignAsync(routeId, request, cancellationToken);

        // Không có endpoint GET một dòng RouteStop (4 endpoint đã chốt, không mở thêm bề mặt),
        // nên Location trỏ về chính danh sách chứa nó. CreatedAtAction với một action không tồn
        // tại sẽ ném lỗi ngay lúc sinh URL — tức là 500 ở đúng ca thành công.
        return result.Success
            ? CreatedAtAction(nameof(ListByRoute), new { routeId }, result.Data)
            : Failure(result);
    }

    /// <summary>
    /// Sắp xếp lại thứ tự toàn bộ trạm của tuyến. Gửi đủ và đúng tập trạm hiện có, theo thứ tự
    /// mới; server tự suy ra <c>stopOrder</c> từ vị trí trong mảng.
    /// </summary>
    [HttpPut("order")]
    public async Task<IActionResult> Reorder(
        Guid routeId,
        [FromBody] ReorderRouteStopsRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _routeStopService.ReorderAsync(routeId, request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>
    /// Gỡ một trạm khỏi tuyến, các trạm sau dồn lên liền mạch.
    ///
    /// <paramref name="id"/> là khoá của DÒNG bảng nối (giá trị <c>id</c> mà GET/POST/PUT trả về),
    /// KHÔNG phải <c>stopId</c>. Tham số buộc phải tên <c>id</c>: <c>AuditLogMiddleware</c> chỉ
    /// nhận ra tham số khoá bản ghi khi nó đúng tên này, đổi tên là dòng nhật ký mất id đối tượng.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Remove(Guid routeId, Guid id, CancellationToken cancellationToken)
    {
        var result = await _routeStopService.RemoveAsync(routeId, id, cancellationToken);

        return result.Success ? NoContent() : Failure(result);
    }

    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };

    // Cùng một hàm ở AuthController, AdminUserController, FaresController, RoutesController và
    // StopsController. Cố ý chép lại thay vì tách thành lớp dùng chung: tách ra thì phải sửa
    // controller của người khác, mà luật nhóm không cho. Các bản giống nhau là cái giá rẻ hơn.
    private IActionResult ValidationError() => BadRequest(new
    {
        message = "Dữ liệu đầu vào không hợp lệ",
        errors = ModelState.Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray()),
    });
}
