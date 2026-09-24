using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.Admin;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Quản trị người dùng: CRUD tài khoản, khóa/mở khóa, gán/thu hồi vai trò.
/// Story 22 — Nguyễn Duy Kiên.
///
/// Cả controller chỉ dành cho Admin (<see cref="RbacPolicies.AdminOnly"/>); người đã đăng nhập
/// nhưng không đủ quyền nhận 403 dạng JSON từ <see cref="RbacMiddleware"/>, không phải 404.
/// </summary>
[ApiController]
[Route("api/admin/users")]
[Authorize(Policy = RbacPolicies.AdminOnly)]
public class AdminUserController : ControllerBase
{
    private readonly IAdminUserService _adminUserService;

    public AdminUserController(IAdminUserService adminUserService)
    {
        _adminUserService = adminUserService;
    }

    /// <summary>Danh sách tài khoản — tìm kiếm, lọc theo vai trò chính và trạng thái, phân trang.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(AdminUserListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] ListAdminUsersRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _adminUserService.ListAsync(request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>Chi tiết một tài khoản.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AdminUserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _adminUserService.GetByIdAsync(id, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>Tạo tài khoản mới với vai trò chỉ định.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(AdminUserResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(
        [FromBody] CreateAdminUserRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _adminUserService.CreateAsync(request, cancellationToken);

        // 201 kèm Location trỏ tới GET chi tiết — client biết ngay lấy lại tài khoản vừa tạo ở đâu.
        return result.Success
            ? CreatedAtAction(nameof(GetById), new { id = result.Data!.Id }, result.Data)
            : Failure(result);
    }

    /// <summary>Sửa hồ sơ tài khoản (họ tên, SĐT, email).</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(AdminUserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateAdminUserRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _adminUserService.UpdateAsync(id, request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>
    /// Xoá tài khoản — xoá MỀM: khóa tài khoản, dữ liệu vẫn còn nguyên trong CSDL.
    /// Quy ước A4 cấm thêm cột <c>IsDeleted</c> và chỉ cho dùng cột trạng thái sẵn có.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(AdminUserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return MissingCurrentUser();
        }

        var result = await _adminUserService.DeleteAsync(id, currentUserId, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>Khóa hoặc mở khóa tài khoản. Không tự khóa được tài khoản của chính mình.</summary>
    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(typeof(AdminUserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetStatus(
        Guid id,
        [FromBody] UpdateUserStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return MissingCurrentUser();
        }

        var result = await _adminUserService.SetStatusAsync(
            id,
            request.IsActive!.Value,
            currentUserId,
            cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>
    /// Gán và thu hồi vai trò trong cùng một thao tác: gửi lên danh sách vai trò đầy đủ mà
    /// tài khoản cần giữ, hệ thống tự thêm cái còn thiếu và bỏ cái không còn trong danh sách.
    /// Không tự thu hồi được vai trò Admin của chính mình.
    /// </summary>
    [HttpPut("{id:guid}/roles")]
    [ProducesResponseType(typeof(AdminUserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetRoles(
        Guid id,
        [FromBody] SetUserRolesRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return MissingCurrentUser();
        }

        var result = await _adminUserService.SetRolesAsync(id, request, currentUserId, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>
    /// Đổi kết quả nghiệp vụ thành HTTP. Service đã nói lỗi thuộc loại nào
    /// (<see cref="ServiceErrorKind"/>) nên ở đây chỉ còn việc ánh xạ, không phán đoán lại.
    /// </summary>
    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),

        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),

        // Xung đột với trạng thái hiện tại: trùng SĐT/email, tự khóa chính mình…
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };

    /// <summary>
    /// Id người đang gọi API, lấy từ claim mà <see cref="JwtMiddleware"/> đã gắn.
    /// Policy AdminOnly bảo đảm luôn có, nhưng vẫn kiểm tra thay vì tin tưởng mù —
    /// Guid.Empty lọt vào tầng nghiệp vụ sẽ thành lỗi rất khó lần ra.
    /// </summary>
    private bool TryGetCurrentUserId(out Guid userId)
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private IActionResult MissingCurrentUser()
        => Unauthorized(new { message = "Không xác định được người dùng đang đăng nhập." });

    /// <summary>
    /// Cấu trúc lỗi thống nhất của cả dự án — xem docs/03-quy-uoc.md mục D3.
    /// Trùng với bản trong AuthController vì đó là hàm private của controller kia;
    /// tách ra lớp dùng chung sẽ phải sửa file của Hiếu, không đáng cho 8 dòng.
    /// </summary>
    private IActionResult ValidationError() => BadRequest(new
    {
        message = "Dữ liệu đầu vào không hợp lệ",
        errors = ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray()),
    });
}
