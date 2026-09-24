using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Endpoint chỉ tồn tại trong project test — cùng lý do với <see cref="TestProtectedController"/>:
/// API thật chưa có controller nào gắn policy phân quyền nên chưa có gì để kiểm chứng middleware RBAC.
/// Controller này được nạp vào app thật qua AddApplicationPart trong <see cref="TestAppFactory"/>,
/// nhờ vậy API thật không phải mọc thêm endpoint chỉ phục vụ test.
/// </summary>
[ApiController]
[Route("api/_test/rbac")]
public class TestRbacController : ControllerBase
{
    /// <summary>Chỉ Admin — kiểm chứng policy <see cref="RbacPolicies.AdminOnly"/>.</summary>
    [HttpGet("admin-only")]
    [Authorize(Policy = RbacPolicies.AdminOnly)]
    public IActionResult AdminOnly() => Ok();

    /// <summary>Admin hoặc Quản lý — kiểm chứng policy gộp nhiều vai trò.</summary>
    [HttpGet("manager-or-above")]
    [Authorize(Policy = RbacPolicies.ManagerOrAbove)]
    public IActionResult ManagerOrAbove() => Ok();

    /// <summary>
    /// Cùng yêu cầu vai trò Admin nhưng khai báo bằng <c>[Authorize(Roles = ...)]</c> thay vì policy.
    /// Dùng để chứng minh cách trả 403 áp dụng cho MỌI hình thức kiểm tra quyền, không riêng policy
    /// do <see cref="RbacMiddleware"/> khai báo — controller của các bạn khác dùng cách nào cũng đúng.
    /// </summary>
    [HttpGet("admin-by-roles")]
    [Authorize(Roles = RoleCodes.Admin)]
    public IActionResult AdminByRoles() => Ok();
}
