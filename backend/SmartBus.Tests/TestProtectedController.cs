using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Services;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Endpoint chỉ tồn tại trong project test. API thật chưa có controller nào gắn
/// <c>[Authorize]</c> nên chưa có gì để kiểm chứng middleware xác thực JWT; controller này
/// được nạp vào app qua <c>AddApplicationPart</c> trong <see cref="JwtAuthTests"/> để giữ cho
/// API thật không phải mọc thêm endpoint chỉ phục vụ test.
/// </summary>
[ApiController]
[Route("api/_test/protected")]
[Authorize]
public class TestProtectedController : ControllerBase
{
    /// <summary>
    /// Trả về đúng những gì middleware đã gắn vào request, để test khẳng định được
    /// "gắn user/role vào request" chứ không chỉ khẳng định status code.
    /// </summary>
    [HttpGet]
    public IActionResult Get()
    {
        // phoneNumber chỉ lấy được từ entity trong HttpContext.Items — không có trong token,
        // nên nó chứng minh middleware đã nạp và gắn user từ CSDL vào request.
        var attachedUser = HttpContext.Items[JwtMiddleware.CurrentUserKey] as UserEntity;

        return Ok(new
        {
            userId = User.FindFirstValue(ClaimTypes.NameIdentifier),
            fullName = User.Identity?.Name,
            isAuthenticated = User.Identity?.IsAuthenticated ?? false,
            roles = User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).OrderBy(code => code).ToArray(),
            phoneNumber = attachedUser?.PhoneNumber,
        });
    }
}
