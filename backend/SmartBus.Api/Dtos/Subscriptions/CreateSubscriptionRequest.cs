using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Subscriptions;

/// <summary>
/// Body của POST /api/subscriptions — đăng ký theo dõi thông báo theo tuyến hoặc theo một trạm
/// (US 10 "Thông báo trạm", Sprint 5 dòng 17 — Trần Trung Hiếu).
///
/// <see cref="StopId"/> bỏ trống hoặc null = theo dõi CẢ TUYẾN (mọi trạm + sự cố của tuyến) —
/// hai nghĩa phân biệt bằng NULL, đã chốt ở docs/37 §3.1. Cùng một hành động của người dùng,
/// chỉ khác độ chi tiết, nên một endpoint một hình dạng thay vì hai endpoint.
///
/// Hợp đồng: docs/api-contract.md mục "Theo dõi thông báo — /subscriptions".
/// </summary>
public class CreateSubscriptionRequest
{
    /// <summary>
    /// Tuyến muốn theo dõi. Để <c>Guid?</c> chứ không phải <c>Guid</c>: kiểu không nullable thì
    /// body thiếu hẳn trường này nhận giá trị mặc định Guid.Empty và <c>[Required]</c> không bắt
    /// được — mà Empty sẽ tra nhầm sang "tuyến không tồn tại" (404) thay vì lỗi hình dạng (400)
    /// (cùng lối AssignStopToRouteRequest).
    /// </summary>
    [Required(ErrorMessage = "Tuyến muốn theo dõi là bắt buộc")]
    public Guid? RouteId { get; set; }

    /// <summary>
    /// Trạm muốn theo dõi — bỏ trống hoặc null = theo dõi cả tuyến. Có giá trị thì trạm phải
    /// THUỘC tuyến đã chọn (kiểm ở tầng service qua RouteStops — khoá ngoại không lo hộ, docs/37 §3.1).
    /// </summary>
    public Guid? StopId { get; set; }
}
