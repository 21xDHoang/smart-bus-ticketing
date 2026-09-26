using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.AuditLogs;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Xuất nhật ký kiểm toán ra Excel — US 23, Phùng Duy Hoàng.
/// Hợp đồng đầy đủ ở mục "Nhật ký kiểm toán" của docs/api-contract.md.
///
/// Chỉ Admin: doc-comment của <see cref="RbacPolicies.AdminOnly"/> đã ghi rõ "nhật ký kiểm toán"
/// thuộc nhóm quyền đó.
///
/// Cố ý đặt ở controller RIÊNG thay vì thêm action vào <c>AuditLogsController</c>: controller đó
/// là việc của Nguyễn Duy Kiên (API truy vấn danh sách, cùng story 23), và hai người cùng mở một
/// file mới trong cùng sprint là nguồn conflict. Dùng chung tiền tố <c>api/audit-logs</c> vẫn hợp
/// lệ vì template đầy đủ khác nhau, và đoạn literal <c>export</c> thắng đoạn tham số — nên
/// <c>GET /api/audit-logs/{id}</c> thêm sau này không nuốt mất đường dẫn này.
/// </summary>
[ApiController]
[Route("api/audit-logs")]
[Authorize(Policy = RbacPolicies.AdminOnly)]
public class AuditLogExportController : ControllerBase
{
    private const string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly IAuditLogExportService _auditLogExportService;

    public AuditLogExportController(IAuditLogExportService auditLogExportService)
        => _auditLogExportService = auditLogExportService;

    /// <summary>
    /// Tải nhật ký trong khoảng lọc ra file .xlsx, mới nhất trước.
    /// Bỏ trống <c>from</c>/<c>to</c> thì lấy 30 ngày gần nhất. Vượt trần 10.000 dòng → 400.
    /// </summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] ExportAuditLogsRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        // ClaimTypes.Name do TokenService phát ra chính là FullName — xem Services/TokenService.cs.
        var result = await _auditLogExportService.ExportAsync(
            request,
            User.FindFirstValue(ClaimTypes.Name),
            cancellationToken);

        if (!result.Success)
        {
            return Failure(result);
        }

        var file = result.Data!;

        // Tên file là ASCII không dấu là CỐ Ý, không phải lười. FileContentResult sanitize tham
        // số filename (mọi ký tự ngoài 0x20–0x7E thành '_') và chỉ đặt tên gốc ở filename* theo
        // RFC 5987 — client không hỗ trợ filename* (script, công cụ tải cũ) sẽ nhận
        // "nh_t-k_-ki_m-to_n_….xlsx", tức đúng cái ngược lại của "dễ đọc". Tiếng Việt để ở tiêu
        // đề cột BÊN TRONG file, nơi không bị mã hoá gì.
        return File(file.Content, XlsxContentType, file.FileName);
    }

    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };

    // Cùng một hàm ở AdminUserController, AuthController, FaresController, RouteStopsController,
    // RoutesController và StopsController. Cố ý chép lại thay vì tách thành lớp dùng chung: tách
    // ra thì phải sửa controller của người khác, mà luật nhóm không cho. Bảy bản giống nhau là
    // cái giá rẻ hơn.
    private IActionResult ValidationError() => BadRequest(new
    {
        message = "Dữ liệu đầu vào không hợp lệ",
        errors = ModelState.Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray()),
    });
}
