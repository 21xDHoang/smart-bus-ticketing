namespace SmartBus.Api.Dtos.Feedbacks;

/// <summary>
/// Một dòng của bảng đếm theo TUYẾN trong GET /api/admin/feedbacks/statistics — hợp đồng "Phản ánh —
/// /feedbacks", mục "Thống kê phản ánh" của docs/api-contract.md.
///
/// Chỉ có những tuyến ĐÃ có phản ánh: tuyến chưa từng bị phản ánh không xuất hiện (đó là câu hỏi
/// khác — "mọi tuyến, kể cả tuyến sạch"), nên <see cref="RouteId"/> ở đây không bao giờ null. Phản
/// ánh không gắn chuyến không quy được về tuyến nào và nằm ở
/// <see cref="FeedbackStatisticsResponse.WithoutTrip"/>.
///
/// <c>routeCode</c>/<c>routeName</c> ghép từ <c>Routes</c> chỉ để hiển thị — cùng lối
/// <c>userFullName</c> của nhóm admin và ba trường chuyến của "Phản ánh của tôi"; không có chúng thì
/// màn hình thống kê phải gọi thêm GET /routes/{id} cho từng dòng.
/// </summary>
public class FeedbackRouteCountResponse
{
    public Guid RouteId { get; set; }

    public string RouteCode { get; set; } = string.Empty;

    public string RouteName { get; set; } = string.Empty;

    /// <summary>Số phản ánh gắn với chuyến của tuyến này. Luôn ≥ 1 — dòng 0 không được sinh ra.</summary>
    public int Count { get; set; }
}
