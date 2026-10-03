namespace SmartBus.Api.Dtos.Feedbacks;

/// <summary>
/// Vỏ danh sách phản ánh — cùng khuôn <c>{ items, total, page, pageSize }</c> của GET /routes,
/// GET /trips và GET /audit-logs (hợp đồng "Phản ánh — /feedbacks" của docs/api-contract.md).
/// </summary>
public class FeedbackListResponse
{
    public IReadOnlyList<FeedbackListItemResponse> Items { get; set; } = [];

    /// <summary>Tổng số dòng khớp bộ lọc (không phải số dòng của trang hiện tại).</summary>
    public int Total { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }
}
