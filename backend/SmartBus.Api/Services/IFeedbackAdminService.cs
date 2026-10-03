using SmartBus.Api.Dtos.Feedbacks;

namespace SmartBus.Api.Services;

/// <summary>
/// Xử lý phản ánh phía quản trị — US 24 "Gửi khiếu nại hoặc đánh giá chất lượng chuyến đi",
/// task "API Admin phản hồi và đổi trạng thái phản ánh" (Sprint 2) — Phùng Duy Hoàng.
/// Hợp đồng đầy đủ ở mục "Phản ánh — /feedbacks" của docs/api-contract.md.
///
/// Bốn endpoint: danh sách (lọc + phân trang), chi tiết, phản hồi (ghi thêm một dòng), đổi trạng
/// thái. Cả bốn nằm sau policy ManagerOrAbove ở controller — tầng này không kiểm quyền sở hữu:
/// mọi quản lý xử lý mọi phản ánh (khác vé tháng — ở đó quyền sở hữu kiểm trong truy vấn).
/// </summary>
public interface IFeedbackAdminService
{
    /// <summary>
    /// Danh sách phản ánh khớp bộ lọc, mới nhất trước, có phân trang.
    /// <c>status</c>/<c>type</c> là mã — mã lạ trả danh sách RỖNG chứ không phải lỗi (cùng lối
    /// GET /routes?status=). Không bao giờ có nhánh lỗi ngoài 400 do DTO chặn ở controller.
    /// </summary>
    Task<ServiceResult<FeedbackListResponse>> ListAsync(
        ListFeedbacksRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Chi tiết một phản ánh kèm toàn bộ luồng phản hồi (cũ → mới); không có → 404.</summary>
    Task<ServiceResult<FeedbackResponse>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ghi thêm MỘT dòng phản hồi của quản lý <paramref name="authorId"/> vào luồng của phản ánh;
    /// trả về phản ánh đầy đủ (đã kèm dòng mới). KHÔNG tự đổi trạng thái — muốn chuyển thì gọi
    /// <see cref="UpdateStatusAsync"/> (hợp đồng nói rõ lý do).
    ///
    /// Phản ánh không tồn tại → 404; <c>content</c> trống hoặc quá 2000 ký tự → 400
    /// <c>errors.content</c> (DTO chặn phần lớn, tầng này chặn lại sau khi cắt khoảng trắng).
    /// </summary>
    Task<ServiceResult<FeedbackResponse>> ReplyAsync(
        Guid id,
        Guid authorId,
        CreateFeedbackReplyRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Đổi trạng thái xử lý; trả về phản ánh đầy đủ. KHÔNG có máy trạng thái — mọi chiều chuyển
    /// đều hợp lệ. Gọi lại đúng trạng thái cũ là no-op: vẫn 200 nhưng không đóng dấu <c>UpdatedAt</c>
    /// giả.
    ///
    /// Phản ánh không tồn tại → 404; <c>status</c> không thuộc ba giá trị → 400 <c>errors.status</c>.
    /// </summary>
    Task<ServiceResult<FeedbackResponse>> UpdateStatusAsync(
        Guid id,
        UpdateFeedbackStatusRequest request,
        CancellationToken cancellationToken = default);
}
