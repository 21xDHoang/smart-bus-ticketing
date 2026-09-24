using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Middleware phân quyền RBAC theo vai trò: khai báo policy và trả 403 khi không đủ quyền.
/// Task story 22 — Vàng Thị Dăm.
///
/// Việc kiểm tra quyền do <c>AuthorizationMiddleware</c> của framework làm — middleware thật là
/// <c>app.UseAuthorization()</c> trong Program.cs. File này KHÔNG viết lại logic đó, chỉ làm hai việc:
///   1. Khai báo sẵn policy theo vai trò để controller dùng qua <c>[Authorize(Policy = ...)]</c>.
///   2. Đổi cách trả lỗi: mặc định framework trả 403 với body RỖNG, ở đây trả JSON
///      <c>{ message }</c> đúng quy ước lỗi của dự án (docs/01-kien-truc.md) để frontend hiển thị được.
///
/// Cùng khuôn với <see cref="JwtMiddleware"/> — file chứa cấu hình, middleware thật của framework
/// lo phần chạy.
/// </summary>
public static class RbacMiddleware
{
    /// <summary>
    /// Đăng ký policy phân quyền + cách trả 403. Gọi từ Program.cs thay cho <c>AddAuthorization()</c> rỗng.
    /// </summary>
    public static IServiceCollection AddRbacAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            // Quản trị hệ thống: tài khoản, vai trò, nhật ký kiểm toán.
            options.AddPolicy(RbacPolicies.AdminOnly, policy => policy.RequireRole(RoleCodes.Admin));

            // Nghiệp vụ vận hành: tuyến đường, trạm dừng, bảng giá. Admin đương nhiên cũng làm được,
            // nên hai policy xếp chồng chứ không loại trừ nhau.
            options.AddPolicy(
                RbacPolicies.ManagerOrAbove,
                policy => policy.RequireRole(RoleCodes.Admin, RoleCodes.Manager));
        });

        // AddAuthorization đã đăng ký handler mặc định rồi (Transient, trong AddAuthorizationPolicyEvaluator).
        // Phải THAY chứ không phải thêm: thêm nữa thì framework lấy bản đăng ký sau cùng, tức hành vi
        // phụ thuộc thứ tự gọi. Giữ đúng Transient như bản gốc — handler hiện chỉ cần ILogger nên
        // Singleton cũng chạy, nhưng nếu sau này có người inject service scoped (ví dụ DbContext để
        // kiểm tra quyền theo dữ liệu) thì Singleton sẽ vỡ ngay ở môi trường Development.
        services.Replace(
            ServiceDescriptor.Transient<IAuthorizationMiddlewareResultHandler, RbacAuthorizationResultHandler>());

        return services;
    }
}

/// <summary>
/// Tên các policy phân quyền — dùng ở controller: <c>[Authorize(Policy = RbacPolicies.AdminOnly)]</c>.
/// Khai báo thành hằng để gõ sai tên policy là lỗi biên dịch; nếu để chuỗi thô thì tên policy sai
/// chỉ vỡ lúc chạy, mà lại vỡ thành lỗi 500 chứ không phải 403 — rất khó lần ra.
/// </summary>
public static class RbacPolicies
{
    /// <summary>Chỉ Admin: quản lý tài khoản, gán vai trò, nhật ký kiểm toán.</summary>
    public const string AdminOnly = "AdminOnly";

    /// <summary>Admin hoặc Quản lý: nghiệp vụ tuyến đường, trạm dừng, bảng giá vé.</summary>
    public const string ManagerOrAbove = "ManagerOrAbove";
}

/// <summary>
/// Thay <see cref="AuthorizationMiddlewareResultHandler"/> mặc định của framework.
///
/// Giữ nguyên mọi hành vi cũ, chỉ đổi đúng một nhánh: khi người dùng ĐÃ đăng nhập nhưng thiếu quyền
/// (Forbidden) thì trả JSON theo quy ước lỗi của dự án. Nhánh 401 (Challenged) vẫn giao lại cho
/// handler mặc định, nhờ vậy thông báo 401 của <see cref="JwtMiddleware"/> không bị đổi.
///
/// Handler này chạy cho MỌI lần kiểm tra quyền thất bại, nên cả <c>[Authorize(Policy = ...)]</c> lẫn
/// <c>[Authorize(Roles = ...)]</c> đều nhận cùng một kiểu 403 — không cần cấu hình gì thêm ở controller.
/// </summary>
internal sealed class RbacAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    /// <summary>
    /// Handler gốc của framework. <c>AuthorizationMiddlewareResultHandler.HandleAsync</c> không phải
    /// virtual nên không kế thừa được — thay vào đó giữ một bản và giao lại các nhánh không phải 403,
    /// nhờ vậy phần xử lý 401 vẫn là code của framework, không phải bản chép lại có nguy cơ lệch.
    /// </summary>
    private static readonly AuthorizationMiddlewareResultHandler DefaultHandler = new();

    /// <summary>
    /// Câu này CỐ Ý trùng với câu frontend đang dùng làm mặc định cho lỗi 403
    /// (frontend/src/api/axiosClient.ts) — người dùng nhận cùng một câu dù request có tới được
    /// backend hay không.
    /// </summary>
    private const string ForbiddenMessage = "Bạn không có quyền truy cập tính năng này.";

    private readonly ILogger<RbacAuthorizationResultHandler> _logger;

    public RbacAuthorizationResultHandler(ILogger<RbacAuthorizationResultHandler> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (!authorizeResult.Forbidden)
        {
            // 401 (Challenged) và request hợp lệ: giữ nguyên hành vi mặc định của framework.
            await DefaultHandler.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        LogDenied(context, policy);

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json; charset=utf-8";

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(new { message = ForbiddenMessage }),
            context.RequestAborted);
    }

    /// <summary>
    /// Ghi log ca bị từ chối. Vai trò bắt buộc chỉ nằm trong log phía máy chủ, KHÔNG trả ra body:
    /// người gọi biết mình thiếu quyền là đủ, không cần biết hệ thống phân quyền thế nào.
    /// </summary>
    private void LogDenied(HttpContext context, AuthorizationPolicy policy)
    {
        var requiredRoles = policy.Requirements
            .OfType<RolesAuthorizationRequirement>()
            .SelectMany(requirement => requirement.AllowedRoles)
            .Distinct(StringComparer.Ordinal);

        _logger.LogWarning(
            "Từ chối truy cập: người dùng {UserId} (vai trò: {CurrentRoles}) gọi {Method} {Path} nhưng thiếu vai trò {RequiredRoles}.",
            context.User.FindFirstValue(ClaimTypes.NameIdentifier),
            Describe(context.User.FindAll(ClaimTypes.Role).Select(claim => claim.Value)),
            context.Request.Method,
            context.Request.Path.Value,
            Describe(requiredRoles));
    }

    private static string Describe(IEnumerable<string> values)
    {
        var list = values.ToArray();
        return list.Length == 0 ? "(không có)" : string.Join(", ", list);
    }
}
