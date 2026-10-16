using SmartBus.Api.Dtos.Subscriptions;

namespace SmartBus.Api.Services;

/// <summary>
/// Nghiệp vụ đăng ký / huỷ theo dõi thông báo theo tuyến và trạm (US 10 "Thông báo trạm" —
/// task Sprint 5 dòng 17, Trần Trung Hiếu). Ghi và xoá dòng bảng <c>Subscriptions</c>; những
/// người đọc bảng này ở Sprint 5 là hai service của Kiên (bắn tin khi xe cách trạm <500m, đẩy
/// sự cố theo tuyến) và component công tắc của Thịnh.
///
/// Phạm vi "của tôi" nằm trong mọi truy vấn theo <c>UserId</c> — không có đường nào đọc hay
/// xoá lượt theo dõi của người khác (hợp đồng: ca "của người khác" trả 404 chứ không 403).
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Danh sách lượt theo dõi của chính người gọi, mới nhất trước — cho công tắc bật/tắt theo
    /// trạm vẽ. Chưa theo dõi gì thì mảng rỗng, không phải lỗi.
    /// </summary>
    Task<List<SubscriptionResponse>> ListMineAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ghi một dòng theo dõi mới. Trả <see cref="ServiceResult{T}.Success"/> kèm dòng vừa tạo;
    /// tuyến/trạm không tồn tại → NotFound; trạm không thuộc tuyến → Invalid (400); đã theo dõi
    /// đúng tuyến/trạm này rồi → Conflict (409) — chặn cả ở tầng truy vấn lẫn tầng CSDL (chỉ mục
    /// duy nhất bộ phận, docs/37 §3.3).
    /// </summary>
    Task<ServiceResult<SubscriptionResponse>> SubscribeAsync(
        Guid userId,
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Xoá một dòng theo dõi của chính người gọi — "huỷ theo dõi" là XOÁ DÒNG chứ không lật cờ
    /// (docs/37 §3.2). Không tồn tại hoặc của người khác → NotFound (cùng một câu 404).
    /// </summary>
    Task<ServiceResult<bool>> UnsubscribeAsync(
        Guid userId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default);
}
