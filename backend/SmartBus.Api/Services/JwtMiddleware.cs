using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Middleware xác thực JWT: verify token, gắn user + vai trò vào request, xử lý 401.
/// Task story 22 — Vàng Thị Dăm.
///
/// Middleware thật là <c>app.UseAuthentication()</c> trong Program.cs; file này chứa toàn bộ
/// cấu hình cho nó — scheme JwtBearer, tham số kiểm tra token, và hai sự kiện
/// <c>OnTokenValidated</c> (gắn user/vai trò) và <c>OnChallenge</c> (trả 401).
/// </summary>
public static class JwtMiddleware
{
    /// <summary>
    /// Khoá <see cref="HttpContext.Items"/> giữ entity <see cref="User"/> đã nạp từ CSDL,
    /// để tầng dưới (ví dụ middleware ghi nhật ký) dùng lại mà không phải truy vấn lần nữa.
    /// </summary>
    public const string CurrentUserKey = "SmartBus.CurrentUser";

    /// <summary>
    /// Khoá <see cref="HttpContext.Items"/> giữ lý do trả 401, truyền từ <c>OnTokenValidated</c>
    /// sang <c>OnChallenge</c>.
    /// </summary>
    public const string ChallengeReasonKey = "SmartBus.JwtChallengeReason";

    /// <summary>HMAC-SHA256 đòi khoá tối thiểu 256 bit.</summary>
    private const int MinimumSigningKeyBytes = 32;

    private const string MissingToken = "Chưa đăng nhập. Vui lòng gửi kèm access token trong header Authorization.";

    private const string ExpiredToken = "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.";

    private const string InvalidToken = "Access token không hợp lệ.";

    /// <summary>Trùng thông báo với <see cref="AuthService"/> để người dùng nhận cùng một câu.</summary>
    private const string AccountLocked = "Tài khoản đã bị khóa";

    private const string AccountNotFound = "Tài khoản không còn tồn tại";

    /// <summary>
    /// Đăng ký scheme xác thực JWT. Gọi từ Program.cs thay cho <c>AddAuthentication()</c> rỗng.
    /// </summary>
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        // TokenService đọc cùng section này qua IOptions<JwtOptions> — xem Program.cs.
        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

        EnsureSigningKeyIsUsable(jwt);

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key));

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Token phát ra đã dùng sẵn tên claim dài (ClaimTypes.*) nên không cần ánh xạ gì.
                // Để mặc định (true) thì claim "sub" bị ánh xạ thêm một lần thành
                // ClaimTypes.NameIdentifier — trùng với claim cùng tên vốn đã có trong token.
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKey,
                    // Chỉ nhận đúng thuật toán mình ký, chặn trò đổi "alg" trong header token.
                    ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
                    // Mặc định 5 phút là quá rộng so với access token chỉ sống 30 phút.
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = ClaimTypes.Name,
                    RoleClaimType = ClaimTypes.Role,
                };

                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = AttachCurrentUserAsync,
                    OnChallenge = WriteUnauthorizedAsync,
                };
            });

        return services;
    }

    /// <summary>
    /// Chặn app khởi động khi khoá ký thiếu hoặc quá ngắn. Nếu để lọt, app vẫn khởi động bình
    /// thường nhưng <em>mọi</em> request đều lỗi 500 — khó lần ra hơn nhiều so với chết ngay
    /// lúc start kèm thông báo chỉ rõ chỗ cần sửa.
    /// </summary>
    private static void EnsureSigningKeyIsUsable(JwtOptions jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt.Key))
        {
            throw new InvalidOperationException(
                "Thiếu cấu hình \"Jwt:Key\". Hãy tạo backend/SmartBus.Api/appsettings.Development.json " +
                "từ file appsettings.Development.json.example rồi điền khoá bí mật.");
        }

        var keyBytes = Encoding.UTF8.GetByteCount(jwt.Key);
        if (keyBytes < MinimumSigningKeyBytes)
        {
            throw new InvalidOperationException(
                $"\"Jwt:Key\" quá ngắn ({keyBytes} byte). HMAC-SHA256 cần tối thiểu {MinimumSigningKeyBytes} byte.");
        }
    }

    /// <summary>
    /// Gắn user và vai trò vào request. Vai trò được đọc lại từ CSDL tại thời điểm request
    /// thay vì tin claim có sẵn trong token, nhờ vậy việc khóa tài khoản và gán/thu hồi vai trò
    /// có hiệu lực ngay, không phải chờ access token hết hạn.
    /// </summary>
    private static async Task AttachCurrentUserAsync(TokenValidatedContext context)
    {
        var httpContext = context.HttpContext;

        if (!TryGetUserId(context.Principal, out var userId))
        {
            Fail(httpContext, context, InvalidToken);
            return;
        }

        // Trong event này không có sẵn scoped DI, phải lấy qua RequestServices.
        var db = httpContext.RequestServices.GetRequiredService<AppDbContext>();

        var user = await db.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == userId, httpContext.RequestAborted);

        if (user is null)
        {
            Fail(httpContext, context, AccountNotFound);
            return;
        }

        if (!user.IsActive)
        {
            Fail(httpContext, context, AccountLocked);
            return;
        }

        ReplaceRoleClaims(context.Principal!, CollectRoleCodes(user));

        httpContext.Items[CurrentUserKey] = user;
    }

    /// <summary>Trả 401 kèm body JSON theo đúng cấu trúc lỗi thống nhất của dự án.</summary>
    private static async Task WriteUnauthorizedAsync(JwtBearerChallengeContext context)
    {
        // Chặn response mặc định của framework (rỗng, chỉ có header WWW-Authenticate).
        context.HandleResponse();

        var message = context.HttpContext.Items[ChallengeReasonKey] as string
                      ?? DefaultMessageFor(context.AuthenticateFailure);

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json; charset=utf-8";

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(new { message }),
            context.HttpContext.RequestAborted);
    }

    /// <summary>
    /// Lấy id tài khoản từ principal. Token hiện phát cả "sub" lẫn <see cref="ClaimTypes.NameIdentifier"/>
    /// cùng giá trị; vẫn thử "sub" để chịu được token cũ chỉ có mỗi "sub".
    /// </summary>
    private static bool TryGetUserId(ClaimsPrincipal? principal, out Guid userId)
    {
        var raw = principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);

        return Guid.TryParse(raw, out userId);
    }

    /// <summary>
    /// Gộp vai trò chính (<c>Users.RoleId</c>) với các vai trò ở bảng nối <c>UserRoles</c>.
    /// Phải gộp cả hai vì tính năng "gán/thu hồi vai trò" ghi vào bảng nối — nếu chỉ đọc vai
    /// trò chính thì thao tác gán vai trò sẽ không có tác dụng gì.
    /// </summary>
    private static IEnumerable<string> CollectRoleCodes(User user)
        => new[] { user.Role?.Code }
            .Concat(user.UserRoles.Select(userRole => userRole.Role?.Code))
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code!)
            .Distinct(StringComparer.Ordinal);

    /// <summary>Thay claim vai trò cũ trong token bằng danh sách vai trò vừa đọc từ CSDL.</summary>
    private static void ReplaceRoleClaims(ClaimsPrincipal principal, IEnumerable<string> roleCodes)
    {
        if (principal.Identity is not ClaimsIdentity identity)
        {
            return;
        }

        // Phải chốt danh sách lại trước khi xoá — không sửa được collection đang duyệt.
        foreach (var staleClaim in identity.FindAll(ClaimTypes.Role).ToList())
        {
            identity.RemoveClaim(staleClaim);
        }

        foreach (var code in roleCodes)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, code));
        }
    }

    /// <summary>
    /// Đánh dấu token không dùng được. Lý do được cất vào <see cref="HttpContext.Items"/> để
    /// <c>OnChallenge</c> đọc lại, thay vì bám vào chuỗi message của exception.
    /// </summary>
    private static void Fail(HttpContext httpContext, TokenValidatedContext context, string reason)
    {
        httpContext.Items[ChallengeReasonKey] = reason;
        context.Fail(reason);
    }

    private static string DefaultMessageFor(Exception? failure) => failure switch
    {
        // Không có token nào được gửi lên.
        null => MissingToken,
        SecurityTokenExpiredException => ExpiredToken,
        _ => InvalidToken,
    };
}
