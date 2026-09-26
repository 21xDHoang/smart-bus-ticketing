using System.Text.Json;
using Microsoft.AspNetCore.Routing;
using SmartBus.Api.Entities;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Api.Services;

/// <summary>
/// Middleware tự động ghi nhật ký mọi thao tác thay đổi dữ liệu (US 23).
/// Task story 23 — Vàng Thị Dăm.
///
/// Hành động suy ra từ HTTP verb chứ không từ từng controller: POST → Create, PUT/PATCH → Update,
/// DELETE → Delete (xem <see cref="AuditAction"/>). Nhờ vậy màn hình của sprint sau tự động được
/// ghi nhật ký, không ai phải nhớ thêm một dòng gọi log vào controller mới.
///
/// Ba điều kiện để một request được ghi:
///   1. Verb thuộc nhóm thay đổi dữ liệu — GET/HEAD/OPTIONS chỉ đọc nên không ghi.
///   2. Response là 2xx. Thao tác thất bại thì dữ liệu chưa hề đổi, ghi vào nhật ký chỉ làm
///      nhật ký sai sự thật; thao tác lỗi đã có log lỗi riêng của máy chủ.
///   3. Đường dẫn không thuộc nhóm tự ghi log lấy — xem <see cref="IgnoredPaths"/>.
///
/// Đăng ký ở Program.cs bằng <c>app.UseAuditLog()</c>, đặt SAU <c>UseAuthentication()</c>
/// (để có sẵn người thực hiện trong <see cref="JwtMiddleware.CurrentUserKey"/>) và TRƯỚC
/// <c>MapControllers()</c> (để bọc được lời gọi controller).
/// </summary>
public sealed class AuditLogMiddleware
{
    /// <summary>
    /// Nhóm đường dẫn tự ghi nhật ký lấy, middleware phải tránh để không sinh ra bản ghi thứ hai.
    ///
    /// /api/auth: đăng nhập, đăng xuất và đăng nhập thất bại là các hành động Login/Logout/LoginFailed,
    /// không suy ra được từ HTTP verb — chúng do chính luồng xác thực gọi <see cref="IAuditLogService"/>
    /// để ghi (task của Hiếu). Nếu middleware cũng ghi thì mỗi lần đăng nhập có hai dòng, trong đó
    /// dòng của middleware ghi đối tượng là "Auth" — một bảng không tồn tại.
    ///
    /// Đánh đổi đã biết: đăng ký tài khoản nằm dưới /api/auth nên middleware không ghi. Việc tạo
    /// tài khoản qua màn hình quản trị (POST /api/admin/users) vẫn được ghi bình thường.
    /// </summary>
    private static readonly PathString[] IgnoredPaths = [new("/api/auth")];

    private readonly RequestDelegate _next;

    public AuditLogMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IAuditLogService auditLog)
    {
        if (!TryMapAction(context.Request.Method, out var action) || IsIgnored(context.Request.Path))
        {
            await _next(context);
            return;
        }

        // Chỉ POST cần đệm response: id của bản ghi vừa tạo không nằm trên đường dẫn mà nằm trong
        // body trả về. PUT/PATCH/DELETE có id ngay trên đường dẫn nên không phải đệm — response
        // vẫn đi thẳng ra client như khi không có middleware này.
        var buffer = action == AuditAction.Create ? new MemoryStream() : null;
        var originalBody = context.Response.Body;

        if (buffer is not null)
        {
            context.Response.Body = buffer;
        }

        try
        {
            await _next(context);
        }
        catch
        {
            // Controller ném lỗi: trả lại stream thật rồi để lỗi đi tiếp đúng như khi không có
            // middleware. Không ghi nhật ký vì thao tác đã thất bại.
            if (buffer is not null)
            {
                context.Response.Body = originalBody;
            }

            throw;
        }

        if (buffer is not null)
        {
            context.Response.Body = originalBody;
        }

        if (IsSuccess(context.Response.StatusCode))
        {
            await auditLog.RecordAsync(
                action,
                CurrentUserId(context),
                ResolveTarget(context, buffer),
                ClientIpAddress(context));
        }

        if (buffer is not null)
        {
            // Trả nội dung đã đệm cho client. Phải chạy SAU khi ghi nhật ký, vì đọc id bản ghi
            // vừa tạo cần buffer còn nguyên.
            buffer.Position = 0;
            await buffer.CopyToAsync(originalBody, context.RequestAborted);
            await buffer.DisposeAsync();
        }
    }

    /// <summary>HTTP verb → hành động trong nhật ký. Trả false với verb chỉ đọc.</summary>
    private static bool TryMapAction(string method, out AuditAction action)
    {
        if (HttpMethods.IsPost(method))
        {
            action = AuditAction.Create;
            return true;
        }

        if (HttpMethods.IsPut(method) || HttpMethods.IsPatch(method))
        {
            // Gộp PUT và PATCH vào cùng Update: quy ước A3 lưu hành động dạng chuỗi và nhóm chỉ
            // cần phân biệt thêm/sửa/xoá. Khác biệt PUT (thay cả bản ghi) và PATCH (sửa một phần)
            // là chi tiết của HTTP, không phải chi tiết kiểm toán cần tra.
            action = AuditAction.Update;
            return true;
        }

        if (HttpMethods.IsDelete(method))
        {
            action = AuditAction.Delete;
            return true;
        }

        action = default;
        return false;
    }

    private static bool IsIgnored(PathString path)
        => IgnoredPaths.Any(ignored => path.StartsWithSegments(ignored));

    /// <summary>2xx — thao tác đã chạy xong và dữ liệu thật sự đổi.</summary>
    private static bool IsSuccess(int statusCode) => statusCode is >= 200 and < 300;

    /// <summary>
    /// Người thực hiện, lấy từ entity mà <see cref="JwtMiddleware"/> đã nạp sẵn vào
    /// <see cref="HttpContext.Items"/> — không truy vấn lại CSDL.
    ///
    /// NULL khi request chưa xác thực. Vẫn ghi nhật ký, chỉ là không biết ai làm: bỏ hẳn bản ghi
    /// vì thiếu người thực hiện là mất luôn dấu vết "có người đã đổi dữ liệu lúc nào".
    /// </summary>
    private static Guid? CurrentUserId(HttpContext context)
        => context.Items.TryGetValue(JwtMiddleware.CurrentUserKey, out var value) && value is UserEntity user
            ? user.Id
            : null;

    /// <summary>
    /// IP người gọi.
    ///
    /// Cố ý KHÔNG đọc header X-Forwarded-For: header do client gửi nên giả mạo được, ghi một IP giả
    /// vào nhật ký kiểm toán còn tệ hơn để trống. Sau proxy thì RemoteIpAddress là IP của proxy —
    /// muốn có IP thật phải bật <c>UseForwardedHeaders</c> ở Program.cs (file của Hoàng); khi đó
    /// RemoteIpAddress tự đổi thành IP thật và hàm này không phải sửa.
    /// </summary>
    private static string? ClientIpAddress(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress;

        if (ip is null)
        {
            return null;
        }

        // Kestrel nghe trên cả IPv4 lẫn IPv6 nên khách IPv4 hiện ra dạng ::ffff:127.0.0.1.
        // Chuẩn hoá về IPv4 cho nhật ký dễ đọc.
        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        return ip.ToString();
    }

    /// <summary>
    /// Đối tượng bị tác động, dạng <c>&lt;Tên bảng&gt;:&lt;Id&gt;</c> — ví dụ <c>"Fares:3f2a1b0c-…"</c>.
    /// Không xác định được tên bảng thì trả NULL; bản ghi nhật ký vẫn được ghi, chỉ thiếu đối tượng.
    /// </summary>
    private static string? ResolveTarget(HttpContext context, MemoryStream? buffer)
    {
        var table = ResolveTableName(context);

        if (table is null)
        {
            return null;
        }

        var id = CreatedIdFromResponse(buffer) ?? RouteId(context);

        // Endpoint không trả về id bản ghi vừa tạo thì ghi lại tên bảng. Bảng AuditLogs không có cột
        // đường dẫn nên Target là chỗ DUY NHẤT nói được thao tác chạm vào đâu; để trống thì dòng
        // nhật ký chỉ còn "có người đã tạo một thứ gì đó lúc mấy giờ", gần như vô dụng khi kiểm toán.
        return id is null ? table : $"{table}:{id}";
    }

    /// <summary>
    /// Tên bảng suy ra từ mẫu route của endpoint: lấy đoạn đứng ngay TRƯỚC tham số <c>{id}</c>,
    /// không lấy đoạn đầu tiên.
    ///
    /// Vì sao: <c>/api/routes/{routeId}/fares/{id}</c> tác động lên bảng Fares chứ không phải Routes —
    /// lấy đoạn đầu sẽ ghi sai bảng. Route không có <c>{id}</c> (POST tạo mới) thì lấy đoạn tĩnh cuối.
    ///
    /// Ví dụ: api/routes/{routeId:guid}/fares/{id:guid} → "Fares"
    ///        api/admin/users/{id:guid}               → "Users"
    ///        api/routes/{id:guid}/stops              → "Routes"
    ///        api/monthly-passes                      → "MonthlyPasses"
    ///
    /// ⚠️ Giới hạn đã biết: route KHÔNG có <c>{id}</c> mà đoạn cuối là một hành động
    /// (ví dụ <c>POST /api/tickets/validate</c> ở Sprint 4) sẽ ra tên bảng là "Validate".
    /// Không có cách phân biệt bằng cú pháp: <c>api/admin/users</c> cần lấy đoạn cuối, còn
    /// <c>api/tickets/validate</c> lại cần lấy đoạn trước nó. Chấp nhận được vì bản ghi vẫn có
    /// id đối tượng trong Target nên tra ngược được — endpoint nào cần chính xác thì đặt id
    /// lên đường dẫn.
    /// </summary>
    private static string? ResolveTableName(HttpContext context)
    {
        var template = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;

        if (string.IsNullOrWhiteSpace(template))
        {
            return null;
        }

        var segments = template.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Phải đúng tên "id" mới là khoá của bản ghi bị tác động; {routeId} là khoá của bản ghi cha.
        var idIndex = Array.FindIndex(segments, IsIdParameter);

        var nameIndex = idIndex > 0
            ? idIndex - 1
            : Array.FindLastIndex(segments, segment => !IsParameter(segment));

        if (nameIndex < 0)
        {
            return null;
        }

        var name = segments[nameIndex];

        // Đoạn trước {id} lại là một tham số (route lồng nhau không có đoạn tĩnh nào ở giữa)
        // thì không có tên tài nguyên để suy ra.
        return IsParameter(name) ? null : ToPascalCase(name);
    }

    private static bool IsIdParameter(string segment)
        => segment.Equals("{id}", StringComparison.OrdinalIgnoreCase)
           || segment.StartsWith("{id:", StringComparison.OrdinalIgnoreCase);

    private static bool IsParameter(string segment) => segment.StartsWith('{');

    /// <summary>"monthly-passes" → "MonthlyPasses" — tên bảng là PascalCase số nhiều (quy ước A2).</summary>
    private static string ToPascalCase(string segment)
        => string.Concat(segment
            .Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    /// <summary>Id trên đường dẫn — PUT/PATCH/DELETE luôn có nên không phải đệm response.</summary>
    private static string? RouteId(HttpContext context)
        => context.Request.RouteValues.TryGetValue("id", out var value)
           && value?.ToString() is { Length: > 0 } id
            ? id
            : null;

    /// <summary>
    /// Id bản ghi vừa tạo, đọc từ trường "id" trong body trả về — mọi DTO response của dự án đều có
    /// trường này, và JSON mặc định của ASP.NET Core đặt tên camelCase.
    ///
    /// Trả NULL khi body rỗng, không phải JSON, hoặc không có trường id. Đó không phải lỗi:
    /// endpoint không trả về id thì nhật ký thiếu id, không phải mất cả bản ghi.
    /// </summary>
    private static string? CreatedIdFromResponse(MemoryStream? buffer)
    {
        if (buffer is null || buffer.Length == 0)
        {
            return null;
        }

        try
        {
            buffer.Position = 0;

            using var document = JsonDocument.Parse(buffer);

            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("id", out var id)
                || id.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return id.GetString();
        }
        catch (JsonException)
        {
            // Endpoint trả về không phải JSON (file, văn bản thuần…) — chỉ thiếu id trong nhật ký.
            return null;
        }
    }
}

/// <summary>Đăng ký <see cref="AuditLogMiddleware"/> — gọi từ Program.cs.</summary>
public static class AuditLogMiddlewareExtensions
{
    public static IApplicationBuilder UseAuditLog(this IApplicationBuilder app)
        => app.UseMiddleware<AuditLogMiddleware>();
}
