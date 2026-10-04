using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Feedbacks;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IFeedbackSubmissionService"/> — hợp đồng đầy đủ ở mục "Phản ánh — /feedbacks",
/// phần "Gửi phản ánh" của docs/api-contract.md.
///
/// Task "API gửi phản ánh: chọn chuyến, loại phản ánh, nội dung, đính kèm ảnh" (US 24, Sprint 2,
/// Trần Trung Hiếu). Chỉ ghi vào bảng Feedbacks (entity + cấu hình của Vàng Thị Dăm ở
/// Data/AppDbContext.Feedback.cs, migration 20261003124532_Sprint2_Feedbacks_FeedbackReplies) —
/// phản hồi của quản lý nằm ở bảng FeedbackReplies, là nghiệp vụ của FeedbackAdminService.
///
/// Bám khuôn <see cref="FeedbackAdminService"/> (cùng bảng, cùng story): cùng lối so khớp tên enum
/// thay vì Enum.TryParse, cùng lối trim nội dung trước khi ghi. Khác ở nghiệp vụ: phản ánh mới
/// luôn Status = New, tác giả là người gọi đọc từ JWT chứ không có trong body.
/// </summary>
public class FeedbackSubmissionService : IFeedbackSubmissionService
{
    private const string TripNotFoundMessage = "Không tìm thấy chuyến đã chọn.";

    private const string TypeInvalidMessage = "Loại phản ánh không hợp lệ";

    private readonly AppDbContext _db;

    public FeedbackSubmissionService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<FeedbackSubmissionResponse>> SubmitAsync(
        CreateFeedbackRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // tripId khi có giá trị là tham chiếu cứng: trỏ tới chuyến không tồn tại là lỗi gọi, không
        // phải "không có gì để phản ánh" — cùng câu trả lời của đăng ký vé tháng với tuyến. Null
        // thì hợp lệ (A9 #20): phản ánh về tuyến/giá vé/ứng dụng không có chuyến để trỏ vào.
        if (request.TripId is not null)
        {
            var tripExists = await _db.Trips
                .AsNoTracking()
                .AnyAsync(t => t.Id == request.TripId, cancellationToken);

            if (!tripExists)
            {
                return ServiceResult<FeedbackSubmissionResponse>.NotFound(TripNotFoundMessage);
            }
        }

        // Loại phản ánh tra theo tên enum — mã lạ là lỗi gọi, không im lặng chấp nhận như bộ lọc
        // của GET /admin/feedbacks (ở đó mã lạ chỉ lọc ra rỗng, ở đây nó sẽ ghi xuống CSDL).
        if (!TryParseFeedbackType(request.Type, out var type))
        {
            return InvalidType();
        }

        var feedback = new Feedback
        {
            UserId = userId,
            TripId = request.TripId,
            Type = type,
            Content = request.Content.Trim(),
            // Chuỗi trắng/rỗng coi như không đính kèm — cột để null, không để chuỗi rỗng.
            AttachmentUrl = string.IsNullOrWhiteSpace(request.AttachmentUrl)
                ? null
                : request.AttachmentUrl.Trim(),
            Rating = request.Rating,
            // Status nhận mặc định New của entity: phản ánh mới luôn bắt đầu ở "Mới".
        };

        _db.Feedbacks.Add(feedback);
        await _db.SaveChangesAsync(cancellationToken);

        return ServiceResult<FeedbackSubmissionResponse>.Ok(ToResponse(feedback));
    }

    /// <summary>
    /// So khớp với danh sách tên của enum thay vì dùng <c>Enum.TryParse</c> — cùng lý do và cùng
    /// cài đặt FeedbackAdminService.TryParseFeedbackType: TryParse chấp nhận cả chuỗi số, trái quy
    /// ước A3. Bỏ qua hoa/thường khi đọc, nhưng giá trị lưu xuống CSDL luôn là tên chuẩn của enum.
    /// </summary>
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
    /// Danh sách mã hợp lệ lấy từ chính enum, không chép tay vào câu chữ — thêm hay bớt một loại
    /// thì thông báo lỗi tự đúng theo. Cùng lối FeedbackAdminService.StatusMessage.
    /// </summary>
    private static ServiceResult<FeedbackSubmissionResponse> InvalidType()
    {
        var message = $"{TypeInvalidMessage}. Chấp nhận: {string.Join(", ", Enum.GetNames<FeedbackType>())}";

        return ServiceResult<FeedbackSubmissionResponse>.Invalid(
            message,
            new Dictionary<string, string[]> { ["type"] = [message] });
    }

    private static FeedbackSubmissionResponse ToResponse(Feedback feedback) => new()
    {
        Id = feedback.Id,
        TripId = feedback.TripId,
        // Tên chuỗi của enum — đúng giá trị đang nằm trong cột Type (HasConversion<string>).
        Type = feedback.Type.ToString(),
        Content = feedback.Content,
        AttachmentUrl = feedback.AttachmentUrl,
        Rating = feedback.Rating,
        // Cùng lối Type: tên chuỗi của enum, đúng giá trị trong cột Status.
        Status = feedback.Status.ToString(),
        CreatedAt = feedback.CreatedAt,
    };
}
