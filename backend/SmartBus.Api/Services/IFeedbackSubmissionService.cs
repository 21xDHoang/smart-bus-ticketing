using SmartBus.Api.Dtos.Feedbacks;

namespace SmartBus.Api.Services;

/// <summary>
/// Gửi một phản ánh mới của chính người gọi (POST /api/feedbacks) — task "API gửi phản ánh:
/// chọn chuyến, loại phản ánh, nội dung, đính kèm ảnh" (US 24, Sprint 2).
/// </summary>
public interface IFeedbackSubmissionService
{
    Task<ServiceResult<FeedbackSubmissionResponse>> SubmitAsync(
        CreateFeedbackRequest request,
        Guid userId,
        CancellationToken cancellationToken = default);
}
