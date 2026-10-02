using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.Trips;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Đổi xe / đổi tài xế khi có sự cố — US 14 "Phân công điều xe", Phùng Duy Hoàng.
/// Hợp đồng đầy đủ ở mục "PATCH /trips/{id}/assignment" của docs/api-contract.md.
///
/// Đứng ở controller riêng cùng bề mặt <c>api/trips</c> với <c>TripLookupController</c> (tra cứu
/// danh sách — cùng người làm) và <c>TripsController</c> (chi tiết chuyến — Vàng Thị Dăm):
/// mỗi bề mặt một controller, cùng lối cặp <c>StopsController</c> / <c>RouteStopsController</c>
/// đã có từ Sprint 1. Route template khác nhau nên không tranh chấp.
///
/// ⚠️ Tên tham số đường dẫn phải đúng là <c>{id}</c>: <c>AuditLogMiddleware</c> suy tên bảng từ
/// đoạn đứng ngay trước tham số <c>{id}</c> để ghi Target "Trips:{id}" — đổi tên thành
/// <c>{tripId}</c> là nhật ký ghi sai đối tượng (ra "Assignment"), trong khi "ghi log thay đổi"
/// chính là một yêu cầu của task.
///
/// Phân quyền gắn ở cả lớp: story 14 là nghiệp vụ điều hành của quản lý — Admin và Quản lý dùng
/// được, các vai trò khác nhận 403.
/// </summary>
[ApiController]
[Route("api/trips")]
[Authorize(Policy = RbacPolicies.ManagerOrAbove)]
public class TripAssignmentController : ControllerBase
{
    private readonly ITripAssignmentService _assignmentService;

    public TripAssignmentController(ITripAssignmentService assignmentService)
        => _assignmentService = assignmentService;

    /// <summary>
    /// Đổi xe và/hoặc tài xế của chuyến. Ít nhất một trong hai trường phải có giá trị.
    /// Trùng lịch điều xe KHÔNG chặn — trả 200 kèm cảnh báo ở <c>conflicts</c>.
    /// Chuyến đã hủy/hoàn thành → 409; chuyến/xe/tài xế không tồn tại → 404.
    /// </summary>
    [HttpPatch("{id:guid}/assignment")]
    public async Task<IActionResult> Reassign(
        Guid id,
        [FromBody] ReassignTripRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _assignmentService.ReassignAsync(id, request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };

    // Cùng một hàm ở RouteTripsController, RoutesController, FaresController và AdminUserController.
    // Cố ý chép lại thay vì tách thành lớp dùng chung: tách ra thì phải sửa cả controller của
    // người khác, mà luật nhóm không cho sửa file của người khác. Các bản giống nhau là cái giá rẻ hơn.
    private IActionResult ValidationError() => BadRequest(new
    {
        message = "Dữ liệu đầu vào không hợp lệ",
        errors = ModelState.Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray()),
    });
}
