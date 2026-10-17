namespace SmartBus.Api.Dtos.Subscriptions;

/// <summary>
/// Hình dạng một lượt theo dõi thông báo trả về cho frontend — GET /api/subscriptions (danh sách
/// của tôi) và POST /api/subscriptions (dòng vừa tạo). (US 10 "Thông báo trạm", Sprint 5 dòng 17 —
/// Trần Trung Hiếu.)
///
/// <see cref="RouteCode"/>/<see cref="RouteName"/>/<see cref="StopName"/> là BẢN CHÉP để hiển thị:
/// công tắc bật/tắt theo trạm (dòng 20) vẽ được một mình mà không phải gọi thêm API nào. Khoá
/// nghiệp vụ vẫn là <see cref="RouteId"/>/<see cref="StopId"/>; khoá ngoại Restrict (docs/37 §3.4)
/// bảo đảm tuyến/trạm được trỏ tới luôn còn nên ba trường chép này không bao giờ null.
///
/// Hợp đồng: docs/api-contract.md mục "Theo dõi thông báo — /subscriptions".
/// </summary>
public class SubscriptionResponse
{
    /// <summary>Khoá của lượt theo dõi — dùng cho DELETE /subscriptions/{id}.</summary>
    public Guid Id { get; set; }

    /// <summary>Tuyến được theo dõi — mọi lượt theo dõi đều gắn một tuyến.</summary>
    public Guid RouteId { get; set; }

    /// <summary>Mã tuyến (bảng Routes) — để hiển thị.</summary>
    public string RouteCode { get; set; } = string.Empty;

    /// <summary>Tên tuyến (bảng Routes) — để hiển thị.</summary>
    public string RouteName { get; set; } = string.Empty;

    /// <summary>Trạm được theo dõi; null = theo dõi CẢ TUYẾN (docs/37 §3.1).</summary>
    public Guid? StopId { get; set; }

    /// <summary>Tên trạm (bảng Stops); null cùng <see cref="StopId"/>.</summary>
    public string? StopName { get; set; }

    /// <summary>Thời điểm bấm theo dõi (UTC) — danh sách sắp mới nhất trước theo cột này.</summary>
    public DateTime CreatedAt { get; set; }
}
