using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Subscriptions;
using SmartBus.Api.Entities;
using RouteEntity = SmartBus.Api.Entities.Route;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="ISubscriptionService"/> — đăng ký / huỷ theo dõi thông báo theo tuyến và
/// trạm (US 10, task Sprint 5 dòng 17 — Trần Trung Hiếu). Hợp đồng đầy đủ ở mục
/// "Theo dõi thông báo — /subscriptions" của docs/api-contract.md; thiết kế bảng ở
/// docs/37-csdl-mau-thong-bao-va-theo-doi.md.
///
/// Ba điều dễ sai, đều đã được chốt trong docs/37 và ghim lại ở đây:
/// <list type="number">
/// <item><b>Trạm phải THUỘC tuyến</b> — khoá ngoại <c>StopId</c> chỉ nói trạm CÓ TỒN TẠI, không
/// nói nó nằm trên tuyến đã chọn (docs/37 §3.1). Kiểm ở đây qua <c>RouteStops</c>; sai thì 400
/// theo trường stopId, không phải 404.</item>
/// <item><b>Trùng đăng ký → 409 ở cả hai tầng.</b> Tầng truy vấn kiểm trước để trả lời rõ; tầng
/// CSDL có hai chỉ mục duy nhất bộ phận (docs/37 §3.3) chặn cuộc đua thật — lượt thua nhận
/// <c>DbUpdateException</c> và được dịch thành 409, không bao giờ nổi lên thành 500. Chỉ mục
/// PHẢI có điều kiện <c>WHERE "StopId" IS NULL</c> vì trong PostgreSQL hai NULL được coi là khác
/// nhau — UNIQUE thường không chặn được ca "theo dõi cả tuyến" hai lần.</item>
/// <item><b>Huỷ theo dõi là XOÁ dòng</b>, không lật cờ — bảng không có cột nào sửa được nên
/// không có <c>UpdatedAt</c> (docs/37 §3.2). Phạm vi "của tôi" nằm ngay trong câu xoá theo
/// <c>UserId</c>: lượt của người khác và lượt không tồn tại cùng một câu 404.</item>
/// </list>
///
/// ⚠️ Provider InMemory KHÔNG cưỡng chế chỉ mục duy nhất lẫn khoá ngoại (docs/37 §7) — test trên
/// InMemory chỉ chứng minh nhánh kiểm-trước; nhánh "lượt thua cuộc đua" chỉ sống thật ở PostgreSQL.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private readonly AppDbContext _db;

    public SubscriptionService(AppDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<List<SubscriptionResponse>> ListMineAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.Subscriptions
            .AsNoTracking()
            .Include(s => s.Route)
            .Include(s => s.Stop)
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);

        return rows.Select(ToResponse).ToList();
    }

    /// <inheritdoc />
    public async Task<ServiceResult<SubscriptionResponse>> SubscribeAsync(
        Guid userId,
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        // Tuyến phải tồn tại — đọc cả dòng vì hai trường hiển thị của response lấy từ đây.
        var route = await _db.Routes
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == request.RouteId, cancellationToken);

        if (route is null)
        {
            return ServiceResult<SubscriptionResponse>.NotFound("Không tìm thấy tuyến xe.");
        }

        // Trạm (nếu chọn) phải tồn tại — đọc cả dòng vì stopName của response lấy từ đây.
        var stopId = request.StopId;
        Stop? stop = null;
        if (stopId is not null)
        {
            stop = await _db.Stops
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == stopId.Value, cancellationToken);

            if (stop is null)
            {
                return ServiceResult<SubscriptionResponse>.NotFound("Không tìm thấy trạm dừng.");
            }

            // Khoá ngoại không lo hộ "trạm này thuộc tuyến này" (docs/37 §3.1) — kiểm ở đây.
            var thuocTuyen = await _db.RouteStops
                .AnyAsync(rs => rs.RouteId == route.Id && rs.StopId == stopId.Value, cancellationToken);

            if (!thuocTuyen)
            {
                return ServiceResult<SubscriptionResponse>.Invalid(
                    "Trạm dừng không thuộc tuyến đã chọn",
                    new Dictionary<string, string[]> { ["stopId"] = ["Trạm dừng không thuộc tuyến đã chọn"] });
            }
        }

        // Chốt chống trùng tầng truy vấn — trả 409 kèm lời rõ trước khi chạm CSDL. Hai nhánh so
        // tách bạch: so `StopId == null` bằng tham số nullable sẽ dịch thành `= @p` (NULL) và
        // không khớp dòng nào trong SQL — phải viết đúng nghĩa IS NULL.
        if (stopId is null
            ? await _db.Subscriptions.AnyAsync(
                s => s.UserId == userId && s.RouteId == route.Id && s.StopId == null, cancellationToken)
            : await _db.Subscriptions.AnyAsync(
                s => s.UserId == userId && s.RouteId == route.Id && s.StopId == stopId, cancellationToken))
        {
            return ServiceResult<SubscriptionResponse>.Conflict(
                stopId is null
                    ? "Bạn đã theo dõi tuyến này rồi."
                    : "Bạn đã theo dõi trạm này rồi.");
        }

        var subscription = new Subscription
        {
            UserId = userId,
            RouteId = route.Id,
            StopId = stopId,
        };
        _db.Subscriptions.Add(subscription);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Chốt chống trùng tầng CSDL (docs/37 §3.3): hai lượt đăng ký song song cùng lọt qua
            // phép kiểm ở trên thì lượt thua nhận lỗi chỉ mục duy nhất ở đây — dịch thành 409
            // chứ không để nổi thành 500. Ca FK mất giữa hai bước (quản trị xoá tuyến ngay lúc
            // này) cũng rơi vào đây; 409 vẫn là câu trả lời an toàn cho khách (bấm lại sau).
            return ServiceResult<SubscriptionResponse>.Conflict(
                "Bạn đã theo dõi tuyến hoặc trạm này rồi.");
        }

        return ServiceResult<SubscriptionResponse>.Ok(ToResponse(subscription, route, stop));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<bool>> UnsubscribeAsync(
        Guid userId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default)
    {
        // Đọc theo ĐÚNG hai khoá (id + userId): lượt của người khác và lượt không tồn tại cho
        // cùng một kết quả — không để lộ lượt theo dõi nào đang tồn tại (hợp đồng đã chốt).
        var subscription = await _db.Subscriptions
            .FirstOrDefaultAsync(
                s => s.Id == subscriptionId && s.UserId == userId,
                cancellationToken);

        if (subscription is null)
        {
            return ServiceResult<bool>.NotFound("Không tìm thấy lượt theo dõi.");
        }

        // "Huỷ theo dõi" là XOÁ dòng chứ không lật cờ (docs/37 §3.2) — xoá có theo dõi chứ không
        // dùng ExecuteDeleteAsync: provider InMemory không hỗ trợ xoá hàng loạt, và dòng đã nằm
        // trong change tracker nên đường này cũng là đường ngắn nhất.
        _db.Subscriptions.Remove(subscription);
        await _db.SaveChangesAsync(cancellationToken);

        return ServiceResult<bool>.Ok(true);
    }

    private static SubscriptionResponse ToResponse(Subscription s)
        => ToResponse(s, s.Route, s.Stop);

    private static SubscriptionResponse ToResponse(Subscription s, RouteEntity? route, Stop? stop) => new()
    {
        Id = s.Id,
        RouteId = s.RouteId,
        RouteCode = route?.Code ?? string.Empty,
        RouteName = route?.Name ?? string.Empty,
        StopId = s.StopId,
        StopName = stop?.Name,
        CreatedAt = s.CreatedAt,
    };
}
