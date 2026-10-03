using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Feedbacks;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IFeedbackAdminService"/> — hợp đồng đầy đủ ở mục "Phản ánh — /feedbacks"
/// của docs/api-contract.md.
///
/// Hai bảng đằng sau service này CHƯA có migration (việc của Vàng Thị Dăm — xem
/// docs/24-huong-dan-migrate-feedbacks.md); test tích hợp chạy trên InMemory nên phần này không
/// chờ CSDL.
/// </summary>
public class FeedbackAdminService : IFeedbackAdminService
{
    /// <summary>Mặc định của hợp đồng khi query không truyền pageSize.</summary>
    private const int DefaultPageSize = 10;

    /// <summary>Trần độ dài nội dung phản hồi — khớp [StringLength] của CreateFeedbackReplyRequest.</summary>
    private const int MaxReplyLength = 2000;

    private const string NotFoundMessage = "Không tìm thấy phản ánh";
    private const string StatusInvalidMessage = "Trạng thái không hợp lệ";
    private const string TypeInvalidMessage = "Loại phản ánh không hợp lệ";
    private const string ContentRequiredMessage = "Nội dung phản hồi không được để trống";
    private const string ContentTooLongMessage = "Nội dung phản hồi tối đa 2000 ký tự";

    private readonly AppDbContext _db;

    public FeedbackAdminService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<FeedbackListResponse>> ListAsync(
        ListFeedbacksRequest request,
        CancellationToken cancellationToken = default)
    {
        var page = request.Page ?? 1;
        var pageSize = request.PageSize ?? DefaultPageSize;

        var query = _db.Feedbacks.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!TryParseFeedbackStatus(request.Status, out var status))
            {
                // Mã trạng thái lạ: không báo lỗi mà trả danh sách rỗng — cùng lối
                // TripLookupService/RouteService: mã lạ có thể là trạng thái hợp lệ trong tương lai.
                return EmptyPage(page, pageSize);
            }

            query = query.Where(f => f.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(request.Type))
        {
            if (!TryParseFeedbackType(request.Type, out var type))
            {
                return EmptyPage(page, pageSize);
            }

            query = query.Where(f => f.Type == type);
        }

        var total = await query.CountAsync(cancellationToken);

        // Nhân bằng long rồi mới ép về int — cùng chiêu TripLookupService/RouteTripsService:
        // (page - 1) * pageSize tính bằng int sẽ tràn thành số âm khi page tiến gần int.MaxValue.
        var skip = (long)(page - 1) * pageSize;

        var rows = await query
            // Màn hình xử lý đọc mới nhất trước; trùng thời điểm (phản ánh gửi cùng giây) xếp tiếp
            // theo Id để phân trang không trùng hay thiếu dòng.
            .OrderByDescending(f => f.CreatedAt)
            .ThenBy(f => f.Id)
            .Skip((int)Math.Min(skip, int.MaxValue))
            .Take(pageSize)
            .Select(f => new
            {
                Feedback = f,
                // FK bắt buộc nên thực tế luôn có tài khoản; vế null là phòng xa theo đúng câu chữ
                // của hợp đồng ("null khi không tìm thấy tài khoản").
                UserFullName = f.User != null ? f.User.FullName : null,
                ReplyCount = f.Replies.Count,
            })
            .ToListAsync(cancellationToken);

        return ServiceResult<FeedbackListResponse>.Ok(new FeedbackListResponse
        {
            Items = rows
                .Select(row => ToListItem(row.Feedback, row.UserFullName, row.ReplyCount))
                .ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize,
        });
    }

    public async Task<ServiceResult<FeedbackResponse>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var row = await _db.Feedbacks
            .AsNoTracking()
            .Where(f => f.Id == id)
            .Select(f => new
            {
                Feedback = f,
                UserFullName = f.User != null ? f.User.FullName : null,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return ServiceResult<FeedbackResponse>.NotFound(NotFoundMessage);
        }

        var replies = await LoadRepliesAsync(id, cancellationToken);

        return ServiceResult<FeedbackResponse>.Ok(ToResponse(row.Feedback, row.UserFullName, replies));
    }

    public async Task<ServiceResult<FeedbackResponse>> ReplyAsync(
        Guid id,
        Guid authorId,
        CreateFeedbackReplyRequest request,
        CancellationToken cancellationToken = default)
    {
        // Cắt khoảng trắng hai đầu rồi mới kiểm: [Required] của DTO đã chặn chuỗi toàn khoảng
        // trắng, đây là chốt thứ hai — tầng service không tin DTO (cùng lối RouteService).
        var content = request.Content?.Trim();

        if (string.IsNullOrEmpty(content))
        {
            return InvalidField("content", ContentRequiredMessage);
        }

        if (content.Length > MaxReplyLength)
        {
            return InvalidField("content", ContentTooLongMessage);
        }

        var feedback = await _db.Feedbacks.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (feedback is null)
        {
            return ServiceResult<FeedbackResponse>.NotFound(NotFoundMessage);
        }

        // Chỉ ghi thêm dòng mới. Cố ý KHÔNG chạm feedback.UpdatedAt và KHÔNG đổi Status: dòng
        // Feedbacks là bản ghi của hành khách, trả lời xong mà còn chờ khách phản hồi lại là ca
        // có thật — tự chuyển trạng thái là nói sai giúp người dùng (hợp đồng nói rõ).
        _db.FeedbackReplies.Add(new FeedbackReply
        {
            FeedbackId = feedback.Id,
            UserId = authorId,
            Content = content,
        });

        await _db.SaveChangesAsync(cancellationToken);

        // Trả về phản ánh đầy đủ (đã kèm dòng mới) để màn hình chi tiết cập nhật ngay, không phải
        // gọi lại GET — đọc lại qua GetByIdAsync cho khỏi chép tay hai lần một phép dựng hình dạng.
        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task<ServiceResult<FeedbackResponse>> UpdateStatusAsync(
        Guid id,
        UpdateFeedbackStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseFeedbackStatus(request.Status, out var status))
        {
            return InvalidField("status", StatusMessage());
        }

        var feedback = await _db.Feedbacks.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (feedback is null)
        {
            return ServiceResult<FeedbackResponse>.NotFound(NotFoundMessage);
        }

        // Không có máy trạng thái: mọi chiều chuyển đều hợp lệ, kể cả mở lại Resolved →
        // InProgress. Gọi lại đúng trạng thái cũ là no-op — vẫn 200 nhưng không đóng dấu thời
        // gian giả (UpdatedAt chỉ đổi khi dữ liệu thật sự đổi).
        if (feedback.Status != status)
        {
            feedback.Status = status;
            feedback.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);
        }

        return await GetByIdAsync(id, cancellationToken);
    }

    /// <summary>Luồng phản hồi của một phản ánh, sắp cũ → mới; trùng thời điểm xếp tiếp theo Id.</summary>
    private async Task<IReadOnlyList<FeedbackReplyResponse>> LoadRepliesAsync(
        Guid feedbackId,
        CancellationToken cancellationToken)
    {
        var replies = await _db.FeedbackReplies
            .AsNoTracking()
            .Where(r => r.FeedbackId == feedbackId)
            .OrderBy(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .Select(r => new FeedbackReplyResponse
            {
                Id = r.Id,
                UserId = r.UserId,
                UserFullName = r.User != null ? r.User.FullName : null,
                Content = r.Content,
                CreatedAt = r.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        return replies;
    }

    private static ServiceResult<FeedbackListResponse> EmptyPage(int page, int pageSize)
        => ServiceResult<FeedbackListResponse>.Ok(new FeedbackListResponse
        {
            Items = [],
            Total = 0,
            Page = page,
            PageSize = pageSize,
        });

    /// <summary>
    /// So khớp với danh sách tên của enum thay vì dùng <c>Enum.TryParse</c> — cùng lý do
    /// RouteService/FareService/TripLookupService: TryParse chấp nhận cả chuỗi số, trái quy ước A3.
    /// Bỏ qua hoa/thường khi đọc, nhưng giá trị lưu xuống CSDL luôn là tên chuẩn của enum.
    /// </summary>
    private static bool TryParseFeedbackStatus(string? value, out FeedbackStatus status)
    {
        var trimmed = value?.Trim();

        foreach (var name in Enum.GetNames<FeedbackStatus>())
        {
            if (string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                status = Enum.Parse<FeedbackStatus>(name);
                return true;
            }
        }

        status = default;
        return false;
    }

    /// <summary>Cùng lối <see cref="TryParseFeedbackStatus"/> nhưng cho loại phản ánh.</summary>
    private static bool TryParseFeedbackType(string? value, out FeedbackType type)
    {
        var trimmed = value?.Trim();

        foreach (var name in Enum.GetNames<FeedbackType>())
        {
            if (string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                type = Enum.Parse<FeedbackType>(name);
                return true;
            }
        }

        type = default;
        return false;
    }

    /// <summary>
    /// Danh sách mã hợp lệ lấy từ chính enum, không chép tay vào câu chữ — thêm hay bớt một trạng
    /// thái thì thông báo lỗi tự đúng theo, không có chỗ nào để quên sửa.
    /// </summary>
    private static string StatusMessage()
        => $"{StatusInvalidMessage}. Chấp nhận: {string.Join(", ", Enum.GetNames<FeedbackStatus>())}";

    private static ServiceResult<FeedbackResponse> InvalidField(string field, string message)
        => ServiceResult<FeedbackResponse>.Invalid(
            message,
            new Dictionary<string, string[]> { [field] = [message] });

    private static FeedbackResponse ToResponse(
        Feedback feedback,
        string? userFullName,
        IReadOnlyList<FeedbackReplyResponse> replies) => new()
    {
        Id = feedback.Id,
        UserId = feedback.UserId,
        UserFullName = userFullName,
        TripId = feedback.TripId,
        // Tên chuỗi của enum — đúng giá trị đang nằm trong cột (HasConversion<string>).
        Type = feedback.Type.ToString(),
        Content = feedback.Content,
        AttachmentUrl = feedback.AttachmentUrl,
        Rating = feedback.Rating,
        Status = feedback.Status.ToString(),
        CreatedAt = feedback.CreatedAt,
        UpdatedAt = feedback.UpdatedAt,
        Replies = replies,
    };

    private static FeedbackListItemResponse ToListItem(
        Feedback feedback,
        string? userFullName,
        int replyCount) => new()
    {
        Id = feedback.Id,
        UserId = feedback.UserId,
        UserFullName = userFullName,
        TripId = feedback.TripId,
        Type = feedback.Type.ToString(),
        Content = feedback.Content,
        AttachmentUrl = feedback.AttachmentUrl,
        Rating = feedback.Rating,
        Status = feedback.Status.ToString(),
        CreatedAt = feedback.CreatedAt,
        UpdatedAt = feedback.UpdatedAt,
        ReplyCount = replyCount,
    };
}
