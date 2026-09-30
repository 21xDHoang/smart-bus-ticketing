using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Trips;

/// <summary>
/// Tham số lọc + phân trang của GET /api/routes/{routeId}/trips — bind từ query string.
/// Cùng khuôn với <see cref="Routes.ListRoutesRequest"/> và <see cref="AuditLogs.ListAuditLogsRequest"/>.
///
/// Hai tên <c>from</c> / <c>to</c> cố ý trùng với màn hình nhật ký: cùng một cách lọc khoảng thời
/// gian, frontend không phải nói hai thứ tiếng.
/// </summary>
public class ListTripsRequest
{
    /// <summary>
    /// Chỉ lấy chuyến khởi hành từ thời điểm này (tính luôn mốc). ISO 8601 có kèm múi giờ,
    /// ví dụ <c>2026-10-01T00:00:00+07:00</c> — server quy về UTC khi so sánh.
    /// </summary>
    public DateTimeOffset? From { get; set; }

    /// <summary>
    /// Chỉ lấy chuyến khởi hành tới thời điểm này (tính luôn mốc). Nhỏ hơn <c>from</c> → 400
    /// <c>errors.to</c>, cùng lối bộ lọc ngày của <c>GET /audit-logs</c>.
    /// </summary>
    public DateTimeOffset? To { get; set; }

    /// <summary>
    /// Lọc theo trạng thái: "Scheduled" | "Running" | "Completed" | "Cancelled".
    /// Bỏ trống = lấy cả bốn. Giá trị không khớp mã nào trả về danh sách rỗng — cùng lối
    /// <see cref="Routes.ListRoutesRequest.Status"/>: mã lạ có thể là trạng thái hợp lệ trong tương lai.
    /// </summary>
    [StringLength(20, ErrorMessage = "Trạng thái tối đa 20 ký tự")]
    public string? Status { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Số trang phải từ 1 trở lên")]
    public int? Page { get; set; }

    [Range(1, 100, ErrorMessage = "Số dòng mỗi trang phải từ 1 đến 100")]
    public int? PageSize { get; set; }
}
