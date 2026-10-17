using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.NotificationTemplates;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="INotificationTemplateService"/> — quản trị mẫu thông báo (US 10, Sprint 5
/// dòng 24 — Dương Thị Hạnh). Hợp đồng đầy đủ ở mục
/// "Màn quản lý mẫu thông báo — /notification-templates" của docs/api-contract.md; thiết kế bảng ở
/// docs/37-csdl-mau-thong-bao-va-theo-doi.md.
///
/// Mẫu là phần GHI ĐÈ, không phải điều kiện để gửi (docs/37 §2.1): bảng này không phải nguồn duy
/// nhất của nội dung thông báo — không có mẫu (hoặc mẫu đang tắt) thì notifier dùng câu mặc định
/// trong code. Vì vậy dịch vụ này chỉ làm đúng hai việc: liệt kê và lưu mẫu; không đụng tới các
/// notifier đang dùng hằng chuỗi.
/// </summary>
public class NotificationTemplateService : INotificationTemplateService
{
    /// <summary>
    /// Chỗ trống hợp lệ THEO TỪNG LOẠI — nguồn để endpoint lưu mẫu bắt lỗi gõ sai tên (docs/37 §6.1).
    ///
    /// Quy ước chỗ trống là <c>{PascalCase}</c> — do docs/37 §6.1 ĐỀ XUẤT, nhóm chưa chốt. Danh sách
    /// dưới đây bám theo dữ liệu mà từng notifier hiện có trong tay (chưa có notifier nào thực sự đọc
    /// bảng mẫu, nên đây là bản đầu cho tới khi §6.1 được chốt):
    /// <list type="bullet">
    /// <item>Hai loại từ chối huỷ/đổi vé có <c>RejectReason</c> — lý do quản lý nhập;</item>
    /// <item>Hai loại trễ/đổi lịch trình có <c>RouteName</c> và bốn mốc giờ cũ/mới của khởi hành/tới
    /// (đúng dữ liệu <see cref="TripScheduleChangeNotifier.BuildBody"/> đang dùng);</item>
    /// <item>Các loại còn lại không có dữ liệu động — số tiền bị cấm trong nội dung thông báo
    /// (docs/api-contract.md mục "Tám loại thông báo"), nên không chỗ trống nào.</item>
    /// </list>
    /// </summary>
    private static readonly IReadOnlyDictionary<NotificationType, string[]> ValidPlaceholders =
        new Dictionary<NotificationType, string[]>
        {
            [NotificationType.CancellationRejected] = ["RejectReason"],
            [NotificationType.TicketChangeRejected] = ["RejectReason"],
            [NotificationType.TripDelayed] =
            [
                "RouteName", "OldDepartureTime", "NewDepartureTime", "OldArrivalTime", "NewArrivalTime",
            ],
            [NotificationType.TripScheduleChanged] =
            [
                "RouteName", "OldDepartureTime", "NewDepartureTime", "OldArrivalTime", "NewArrivalTime",
            ],
        };

    /// <summary>
    /// Bắt mọi <c>{Tên}</c> trong văn bản — tên bắt đầu bằng chữ cái, chỉ chữ và số (PascalCase,
    /// không gạch dưới). Ngoặc chưa đóng hoặc tên sai khuôn thì KHÔNG khớp và được coi là chữ thường —
    /// phép kiểm chỉ nhắm ca "gõ đúng ngoặc nhưng sai tên", đúng ca docs/37 §6.1 cảnh báo.
    /// </summary>
    private static readonly Regex PlaceholderPattern = new(@"\{([A-Za-z][A-Za-z0-9]*)\}");

    private readonly AppDbContext _db;

    public NotificationTemplateService(AppDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<List<NotificationTemplateResponse>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.NotificationTemplates
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // OrderBy trên enum sắp theo giá trị nguyên = đúng thứ tự khai báo NotificationType.
        return rows
            .OrderBy(t => t.Type)
            .Select(ToResponse)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<ServiceResult<NotificationTemplateResponse>> UpsertAsync(
        NotificationType type,
        UpsertNotificationTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidatePlaceholders(type, request.Title!, request.Body!);
        if (validationError is not null)
        {
            return validationError;
        }

        var template = await _db.NotificationTemplates
            .FirstOrDefaultAsync(t => t.Type == type, cancellationToken);

        if (template is null)
        {
            template = new NotificationTemplate { Type = type };
            _db.NotificationTemplates.Add(template);
        }

        template.Title = request.Title!.Trim();
        template.Body = request.Body!;
        template.IsActive = request.IsActive;
        template.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Chỉ mục duy nhất trên Type (docs/37 §2) chặn cuộc đua: hai lượt soạn cùng một loại song
            // song cùng lọt qua phép đọc thì lượt thua nhận lỗi duy nhất ở đây — dịch thành 409 chứ
            // không để nổi thành 500. Admin soạn mẫu hiếm khi đua nhau, nhưng câu catch rẻ hơn một 500.
            return ServiceResult<NotificationTemplateResponse>.Conflict(
                "Mẫu của loại thông báo này vừa được người khác tạo. Vui lòng làm mới danh sách.");
        }

        return ServiceResult<NotificationTemplateResponse>.Ok(ToResponse(template));
    }

    /// <summary>
    /// Kiểm chỗ trống trước khi lưu: mọi <c>{Tên}</c> trong Title/Body phải nằm trong danh sách hợp lệ
    /// của <paramref name="type"/>. Trả null khi hợp lệ; ngược lại trả lỗi Invalid kèm
    /// <c>errors.title</c>/<c>errors.body</c> để frontend gắn thẳng vào ô nhập.
    /// </summary>
    private static ServiceResult<NotificationTemplateResponse>? ValidatePlaceholders(
        NotificationType type,
        string title,
        string body)
    {
        var allowed = ValidPlaceholders.TryGetValue(type, out var list)
            ? list
            : [];

        var invalidInTitle = FindInvalidPlaceholders(title, allowed);
        var invalidInBody = FindInvalidPlaceholders(body, allowed);

        if (invalidInTitle.Count == 0 && invalidInBody.Count == 0)
        {
            return null;
        }

        var errors = new Dictionary<string, string[]>();
        if (invalidInTitle.Count > 0)
        {
            errors["title"] = invalidInTitle.Select(name => DescribeInvalid(name, allowed)).ToArray();
        }

        if (invalidInBody.Count > 0)
        {
            errors["body"] = invalidInBody.Select(name => DescribeInvalid(name, allowed)).ToArray();
        }

        return ServiceResult<NotificationTemplateResponse>.Invalid(
            "Chỗ trống trong mẫu không hợp lệ.",
            errors);
    }

    /// <summary>Các tên chỗ trống trong <paramref name="text"/> không nằm trong danh sách cho phép.</summary>
    private static List<string> FindInvalidPlaceholders(string text, string[] allowed)
    {
        var allowedSet = new HashSet<string>(allowed, StringComparer.Ordinal);

        return PlaceholderPattern.Matches(text)
            .Select(match => match.Groups[1].Value)
            .Where(name => !allowedSet.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Câu lỗi cho một chỗ trống sai — nêu luôn danh sách hợp lệ để người soạn tự sửa.</summary>
    private static string DescribeInvalid(string name, string[] allowed) =>
        allowed.Length == 0
            ? $"Chỗ trống {{{name}}} không được dùng cho loại thông báo này."
            : $"Chỗ trống {{{name}}} không hợp lệ. Các chỗ trống được dùng: {string.Join(", ", allowed)}.";

    private static NotificationTemplateResponse ToResponse(NotificationTemplate t) => new()
    {
        Id = t.Id,
        Type = t.Type.ToString(),
        Title = t.Title,
        Body = t.Body,
        IsActive = t.IsActive,
        CreatedAt = t.CreatedAt,
        UpdatedAt = t.UpdatedAt,
    };
}
