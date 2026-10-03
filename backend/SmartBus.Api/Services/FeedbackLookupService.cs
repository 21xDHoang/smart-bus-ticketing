using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Feedbacks;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IFeedbackLookupService"/> — hợp đồng đầy đủ ở mục "Phản ánh — /feedbacks",
/// phần "Phản ánh của tôi" của docs/api-contract.md.
///
/// Ba quyết định đáng đọc trước khi sửa:
///
/// 1. <b>Lọc theo UserId ngay trong truy vấn</b>, không lọc sau khi đọc rồi kiểm ở tầng trên — cùng
///    lối MonthlyPassLookupService: phản ánh của người khác không có đường lọt vào kết quả, và ca
///    "của người khác" tự rơi vào 404 giống hệt ca "không tồn tại", không cần nhánh 403 riêng.
/// 2. <b>Ba trường chuyến ghép từ Trips → Routes ngay trong projection</b> — hành khách không tự
///    tra được chuyến (GET /api/trips/{id} nằm sau policy ManagerOrAbove), nên không có chúng thì
///    <c>tripId</c> là một GUID vô nghĩa với người đọc. Cùng lối ghép <c>userFullName</c> của
///    nhóm admin: trường chỉ để hiển thị, null khi không có dữ liệu.
/// 3. <b>Không bao giờ trả <c>replies</c> ở dòng danh sách</b> — chỉ con đếm <c>replyCount</c>, đúng
///    luật của hợp đồng (mục "Entity Feedback"); luồng đầy đủ nằm ở <see cref="GetMineAsync"/>.
///
/// Hai bảng đằng sau service này CHƯA có migration (việc của Vàng Thị Dăm — xem
/// docs/24-huong-dan-migrate-feedbacks.md); test tích hợp chạy trên InMemory nên phần này không chờ
/// CSDL.
/// </summary>
public class FeedbackLookupService : IFeedbackLookupService
{
    private const string NotFoundMessage = "Không tìm thấy phản ánh";

    private readonly AppDbContext _db;

    public FeedbackLookupService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<MyFeedbackListItemResponse>> ListMineAsync(
        Guid userId,
        ListMyFeedbacksRequest request,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Feedbacks
            .AsNoTracking()
            .Where(f => f.UserId == userId);

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!TryParseFeedbackStatus(request.Status, out var status))
            {
                // Mã trạng thái lạ: không báo lỗi mà trả mảng rỗng — cùng lối
                // FeedbackAdminService.ListAsync và TripLookupService: mã lạ có thể là trạng thái
                // hợp lệ trong tương lai, 400 ở đây làm màn hình cũ vỡ khi backend thêm giá trị mới.
                return [];
            }

            query = query.Where(f => f.Status == status);
        }

        var rows = await query
            // Màn hình theo dõi đọc mới nhất trước; trùng thời điểm (gửi cùng giây) xếp tiếp theo Id
            // để thứ tự tất định — cùng lối danh sách của nhóm admin.
            .OrderByDescending(f => f.CreatedAt)
            .ThenBy(f => f.Id)
            .Select(f => new
            {
                Feedback = f,
                // Chuyến nullable (A9 #20) nên phải qua hai lần kiểm null; vế Route null là phòng xa
                // — FK bắt buộc nên thực tế chuyến nào cũng có tuyến.
                RouteCode = f.Trip != null && f.Trip.Route != null ? f.Trip.Route.Code : null,
                RouteName = f.Trip != null && f.Trip.Route != null ? f.Trip.Route.Name : null,
                DepartureTime = f.Trip != null ? f.Trip.DepartureTime : (DateTime?)null,
                ReplyCount = f.Replies.Count,
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => ToListItem(
                row.Feedback, row.RouteCode, row.RouteName, row.DepartureTime, row.ReplyCount))
            .ToList();
    }

    public async Task<ServiceResult<MyFeedbackResponse>> GetMineAsync(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        // Hai điều kiện nằm cùng một chỗ: không có nhánh nào đọc được phản ánh của người khác, kể cả
        // để đếm hay để kiểm sự tồn tại.
        var row = await _db.Feedbacks
            .AsNoTracking()
            .Where(f => f.Id == id && f.UserId == userId)
            .Select(f => new
            {
                Feedback = f,
                RouteCode = f.Trip != null && f.Trip.Route != null ? f.Trip.Route.Code : null,
                RouteName = f.Trip != null && f.Trip.Route != null ? f.Trip.Route.Name : null,
                DepartureTime = f.Trip != null ? f.Trip.DepartureTime : (DateTime?)null,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return ServiceResult<MyFeedbackResponse>.NotFound(NotFoundMessage);
        }

        var replies = await LoadRepliesAsync(id, cancellationToken);

        return ServiceResult<MyFeedbackResponse>.Ok(ToResponse(
            row.Feedback, row.RouteCode, row.RouteName, row.DepartureTime, replies));
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

    /// <summary>
    /// So khớp với danh sách tên của enum thay vì dùng <c>Enum.TryParse</c> — cùng lý do
    /// FeedbackAdminService/RouteService/FareService/TripLookupService: TryParse chấp nhận cả chuỗi
    /// số (Enum.Parse("0") ra giá trị đầu tiên), trái quy ước A3. Bỏ qua hoa/thường khi đọc, nhưng
    /// giá trị lưu xuống CSDL luôn là tên chuẩn của enum.
    ///
    /// Chép lại từ FeedbackAdminService thay vì tách lớp dùng chung: tách ra là phải sửa file của
    /// Hoàng, mà luật nhóm không cho sửa file của người khác — các bản giống nhau là cái giá rẻ hơn
    /// (cùng lối các controller chép ValidationError).
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

    private static MyFeedbackResponse ToResponse(
        Feedback feedback,
        string? routeCode,
        string? routeName,
        DateTime? departureTime,
        IReadOnlyList<FeedbackReplyResponse> replies) => new()
    {
        Id = feedback.Id,
        TripId = feedback.TripId,
        RouteCode = routeCode,
        RouteName = routeName,
        DepartureTime = departureTime,
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

    private static MyFeedbackListItemResponse ToListItem(
        Feedback feedback,
        string? routeCode,
        string? routeName,
        DateTime? departureTime,
        int replyCount) => new()
    {
        Id = feedback.Id,
        TripId = feedback.TripId,
        RouteCode = routeCode,
        RouteName = routeName,
        DepartureTime = departureTime,
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
