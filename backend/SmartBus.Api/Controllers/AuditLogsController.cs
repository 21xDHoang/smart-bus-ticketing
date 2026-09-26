using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.AuditLogs;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Truy vấn danh sách nhật ký kiểm toán — US 23, Nguyễn Duy Kiên.
/// Hợp đồng đầy đủ ở mục "Nhật ký kiểm toán" của docs/api-contract.md.
///
/// Chỉ Admin: doc-comment của <see cref="RbacPolicies.AdminOnly"/> đã ghi rõ "nhật ký kiểm toán"
/// thuộc nhóm quyền đó.
///
/// Tên và tiền tố đường dẫn không phải tự đặt: <c>AuditLogExportController</c> và Program.cs đều
/// đã chỉ đích danh controller này là "việc của Nguyễn Duy Kiên (API truy vấn danh sách, cùng
/// story 23)". Hai controller dùng chung tiền tố <c>api/audit-logs</c> vẫn hợp lệ vì template đầy
/// đủ khác nhau — <c>GET /api/audit-logs</c> ở đây và <c>GET /api/audit-logs/export</c> ở kia.
///
/// KHÔNG khai <c>[HttpGet("export")]</c> ở đây: trùng khít với controller kia và ASP.NET Core sẽ
/// ném AmbiguousMatchException lúc chạy. Và nếu sau này thêm <c>GET /api/audit-logs/{id}</c> thì
/// bắt buộc là <c>{id:guid}</c>, để không nuốt mất đường dẫn <c>/export</c>.
/// </summary>
[ApiController]
[Route("api/audit-logs")]
[Authorize(Policy = RbacPolicies.AdminOnly)]
public class AuditLogsController : ControllerBase
{
    private readonly IAuditLogQueryService _auditLogQueryService;

    public AuditLogsController(IAuditLogQueryService auditLogQueryService)
        => _auditLogQueryService = auditLogQueryService;

    /// <summary>
    /// Danh sách nhật ký khớp bộ lọc, mới nhất trước, có phân trang.
    /// Bỏ trống <c>from</c>/<c>to</c> thì lấy 30 ngày gần nhất.
    /// Bộ lọc không khớp bản ghi nào là 200 với danh sách rỗng, không phải 404.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] ListAuditLogsRequest request,
        CancellationToken cancellationToken)
    {
        // Program.cs đặt SuppressModelStateInvalidFilter = true nên [ApiController] không tự trả
        // 400 — controller phải tự kiểm, và trả về đúng cấu trúc { message, errors } của mục D3.
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _auditLogQueryService.ListAsync(request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        // Đường truy vấn danh sách hiện không có nhánh NotFound nào (bộ lọc không khớp là danh
        // sách rỗng, không phải 404). Giữ nguyên nhánh này để hàm giống hệt bảy bản còn lại —
        // lệch đi thì lần sau người đọc phải phân vân không biết đó là chủ ý hay thiếu sót.
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };

    // Cùng một hàm ở AdminUserController, AuthController, FaresController, RouteStopsController,
    // RoutesController, StopsController và AuditLogExportController. Cố ý chép lại thay vì tách
    // thành lớp dùng chung: tách ra thì phải sửa controller của người khác, mà luật nhóm không
    // cho. Tám bản giống nhau là cái giá rẻ hơn.
    private IActionResult ValidationError() => BadRequest(new
    {
        message = "Dữ liệu đầu vào không hợp lệ",
        errors = ModelState.Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray()),
    });
}
