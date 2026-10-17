using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.Exports;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Xuất báo cáo lớn chạy ở NỀN — task *"Xử lý xuất file lớn bất đồng bộ + thông báo khi xong"*
/// (Sprint 5 dòng 64, Vàng Thị Dăm). Hợp đồng đầy đủ ở mục "Xuất file bất đồng bộ — /exports" của
/// docs/api-contract.md.
///
/// <para>
/// Ba endpoint thay cho một endpoint chạy lâu:
/// </para>
///
/// <list type="bullet">
/// <item><c>POST /api/exports</c> — nhận yêu cầu, trả 202 kèm trạng thái <c>Pending</c>.</item>
/// <item><c>GET /api/exports/{id}</c> — frontend hỏi lại trạng thái; không kéo theo nội dung file.</item>
/// <item><c>GET /api/exports/{id}/download</c> — tải file khi đã <c>Succeeded</c>.</item>
/// </list>
///
/// <para>
/// <b>Quyền:</b> Admin và Quản lý — cùng nhóm với ba bản xuất đồng bộ. Riêng nhật ký kiểm toán
/// chỉ Admin, đúng policy của <c>GET /api/audit-logs/export</c>; kiểm trong action thay vì policy
/// riêng vì quyền phụ thuộc GIÁ TRỊ của body chứ không phụ thuộc route.
/// </para>
///
/// <para>
/// <b>Không có endpoint danh sách job.</b> Task chỉ cần đặt, theo dõi và tải; màn "lịch sử xuất
/// báo cáo" không thuộc story nào đang mở, và thêm nó là mở một bề mặt API phải nuôi bằng tài
/// liệu + test mà không ai gọi.
/// </para>
/// </summary>
[ApiController]
[Route("api/exports")]
[Authorize(Policy = RbacPolicies.ManagerOrAbove)]
public class ExportJobsController : ControllerBase
{
    private readonly IExportJobService _exportJobService;

    public ExportJobsController(IExportJobService exportJobService)
        => _exportJobService = exportJobService;

    /// <summary>
    /// Đặt một lượt xuất báo cáo ở nền. Trả <b>202 Accepted</b> — không phải 201, vì tài nguyên
    /// được tạo là một YÊU CẦU đang chờ chứ chưa phải file đã sẵn sàng.
    ///
    /// <c>reportType</c>/<c>format</c> lạ, khoảng ngày ngược/quá 366 ngày, khung giờ/độ gộp lạ →
    /// 400; tuyến không tồn tại → 404; đã có 3 job chưa xong → 409; <c>audit-logs</c> mà không phải
    /// Admin → 403.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ExportJobResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateExportJobRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        if (!TryGetCurrentUserId(out var userId))
        {
            return MissingCurrentUser();
        }

        // Nhật ký kiểm toán là dữ liệu quản trị: Manager gọi được mọi loại báo cáo khác nhưng
        // không được đọc/ghi nhật ký — cùng ranh giới RbacPolicies.AdminOnly.
        if (IsAuditLogsRequest(request.ReportType) && !User.IsInRole(RoleCodes.Admin))
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new { message = "Chỉ quản trị viên được xuất nhật ký kiểm toán." });
        }

        // ClaimTypes.Name do TokenService phát ra chính là FullName — xem Services/TokenService.cs.
        var result = await _exportJobService.EnqueueAsync(
            request,
            userId,
            User.FindFirstValue(ClaimTypes.Name),
            cancellationToken);

        if (!result.Success)
        {
            return Failure(result);
        }

        return AcceptedAtAction(
            nameof(Get),
            new { id = result.Data!.Id },
            result.Data);
    }

    /// <summary>
    /// Trạng thái job của chính người gọi. Job không tồn tại, của người khác hoặc đã hết hạn đều
    /// trả 404 — không phân biệt để không tiết lộ job nào có thật trong hệ thống.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ExportJobResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return MissingCurrentUser();
        }

        var result = await _exportJobService.GetAsync(id, userId, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>
    /// Tải file của job đã xong. Chưa xong (hoặc đã thất bại) → <b>409</b>: job có thật, chỉ là
    /// chưa có file — frontend dùng mã này để biết nên tiếp tục hỏi trạng thái hay dừng.
    /// </summary>
    [HttpGet("{id:guid}/download")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return MissingCurrentUser();
        }

        var result = await _exportJobService.GetFileAsync(id, userId, cancellationToken);

        if (!result.Success)
        {
            return Failure(result);
        }

        var file = result.Data!;

        // Tên file ASCII không dấu là CỐ Ý — cùng lý do đã ghi ở AuditLogExportController: tên có
        // dấu bị FileContentResult mã hoá thành gạch dưới. Tiếng Việt nằm bên trong file.
        return File(file.Content, file.ContentType, file.FileName);
    }

    /// <summary>
    /// Chỉ đọc <c>reportType</c> để quyết định quyền. Giá trị lạ trả <see langword="false"/> và để
    /// service dựng lỗi 400 đúng trường — controller không nhân bản bảng mã báo cáo.
    /// </summary>
    private static bool IsAuditLogsRequest(string? reportType)
        => ExportReportTypes.TryParse(reportType, out var parsed)
            && parsed == ExportReportType.AuditLogs;

    private bool TryGetCurrentUserId(out Guid userId)
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private IActionResult MissingCurrentUser()
        => Unauthorized(new { message = "Không xác định được người dùng đang đăng nhập." });

    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };

    // Cùng một hàm ở các controller khác — chép lại cố ý: tách thành lớp dùng chung thì phải sửa
    // controller của người khác, mà luật nhóm không cho.
    private IActionResult ValidationError() => BadRequest(new
    {
        message = "Dữ liệu đầu vào không hợp lệ",
        errors = ModelState.Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray()),
    });
}
