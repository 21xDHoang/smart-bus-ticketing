using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.Auth;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Xác thực: đăng ký, đăng nhập, làm mới token, đăng xuất.
/// Story 22 — đăng ký: Trần Trung Hiếu · đăng nhập/token: Phùng Duy Hoàng.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    /// <summary>Hạn mức chống spam đăng ký: tối đa 5 lần mỗi phút cho mỗi địa chỉ IP.</summary>
    private const int RegisterMaxRequestsPerMinute = 5;

    private static readonly TimeSpan RegisterWindow = TimeSpan.FromMinutes(1);

    private readonly IAuthService _authService;
    private readonly IRateLimitService _rateLimitService;

    public AuthController(IAuthService authService, IRateLimitService rateLimitService)
    {
        _authService = authService;
        _rateLimitService = rateLimitService;
    }

    /// <summary>
    /// Đăng ký tài khoản Hành khách. Dữ liệu hợp lệ thì trả luôn cặp access/refresh token —
    /// người dùng được vào hệ thống ngay sau khi đăng ký, không cần đăng nhập lại.
    /// </summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        // Kiểm tra hạn mức TRƯỚC khi validate dữ liệu: request thiếu/sai trường vẫn bị đếm,
        // kẻ tấn công không lách được bằng cách gửi payload rác.
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (!_rateLimitService.TryAcquire(clientIp, RegisterMaxRequestsPerMinute, RegisterWindow))
        {
            Response.Headers.RetryAfter = "60";
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                message = "Quá nhiều lần đăng ký từ thiết bị này. Vui lòng thử lại sau 1 phút.",
            });
        }

        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _authService.RegisterAsync(request, cancellationToken);

        return result.Success
            ? StatusCode(StatusCodes.Status201Created, result.Data)
            : Conflict(new { message = result.Error, errors = result.Errors });
    }

    /// <summary>Đăng nhập bằng số điện thoại + mật khẩu, cấp access token và refresh token.</summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _authService.LoginAsync(request, cancellationToken);

        return result.Success
            ? Ok(result.Data)
            : Unauthorized(new { message = result.Error });
    }

    /// <summary>
    /// Đổi refresh token lấy cặp token mới. Refresh token cũ bị thu hồi ngay khi gọi,
    /// nên mỗi token chỉ dùng được đúng một lần.
    /// </summary>
    [HttpPost("refresh-token")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _authService.RefreshTokenAsync(request.RefreshToken, cancellationToken);

        return result.Success
            ? Ok(result.Data)
            : Unauthorized(new { message = result.Error });
    }

    /// <summary>
    /// Đăng xuất — thu hồi refresh token, kết thúc phiên.
    /// Access token đã cấp vẫn dùng được tới khi hết hạn (mặc định 30 phút) vì JWT là stateless.
    /// </summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        await _authService.LogoutAsync(request.RefreshToken, cancellationToken);

        return NoContent();
    }

    /// <summary>Cấu trúc lỗi thống nhất của cả dự án — xem docs/01-kien-truc.md.</summary>
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
