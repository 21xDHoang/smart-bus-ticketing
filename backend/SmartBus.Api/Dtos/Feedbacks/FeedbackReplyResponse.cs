namespace SmartBus.Api.Dtos.Feedbacks;

/// <summary>
/// Một phản hồi của quản lý trong luồng đối thoại của phản ánh — hợp đồng "Phản ánh — /feedbacks"
/// của docs/api-contract.md (mục Entity <c>FeedbackReply</c>).
///
/// Không có <c>updatedAt</c>: bảng chỉ ghi thêm, sửa tại chỗ thì mất lịch sử đối thoại.
/// </summary>
public class FeedbackReplyResponse
{
    public Guid Id { get; set; }

    /// <summary>Quản lý đã phản hồi.</summary>
    public Guid UserId { get; set; }

    /// <summary>Chỉ để hiển thị, ghép từ <c>Users</c> — null khi không tìm thấy tài khoản.</summary>
    public string? UserFullName { get; set; }

    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
