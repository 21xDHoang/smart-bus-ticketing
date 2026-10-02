using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Trips;

/// <summary>
/// Tham số của GET /api/trips/search — bind từ query string. Cùng khuôn
/// <see cref="ListTripsRequest"/>: hai tên <c>from</c>/<c>to</c> cố ý trùng nhau để frontend
/// không phải nói hai thứ tiếng khi lọc theo khoảng thời gian.
///
/// <c>routeId</c> bắt buộc (khác bộ lọc bỏ trống được của <c>GET /trips</c>): endpoint trả lời
/// "chuyến của tuyến X", còn việc tìm ra tuyến từ điểm đi/điểm đến là task "API tìm kiếm chuyến
/// theo điểm đi, điểm đến, ngày giờ" của Trần Trung Hiếu (story 1) — xem ghi chú ranh giới ở
/// mục "GET /trips/search" của docs/api-contract.md.
/// </summary>
public class SearchTripsRequest
{
    /// <summary>Bắt buộc. GUID không trỏ tới tuyến nào → 404.</summary>
    [Required(ErrorMessage = "Tuyến đường không được để trống")]
    public Guid? RouteId { get; set; }

    /// <summary>
    /// Chỉ lấy chuyến khởi hành từ thời điểm này (tính luôn mốc). ISO 8601 có kèm múi giờ,
    /// ví dụ <c>2026-10-01T00:00:00+07:00</c> — server quy về UTC khi so sánh.
    /// </summary>
    public DateTimeOffset? From { get; set; }

    /// <summary>
    /// Chỉ lấy chuyến khởi hành tới thời điểm này (tính luôn mốc). Nhỏ hơn <c>from</c> → 400
    /// <c>errors.to</c>, cùng lối bộ lọc ngày của <c>GET /trips</c>.
    /// </summary>
    public DateTimeOffset? To { get; set; }
}
