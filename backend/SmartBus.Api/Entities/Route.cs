namespace SmartBus.Api.Entities;

/// <summary>
/// Bảng Routes — tuyến đường xe buýt.
/// Task migrate bảng này là của Vàng Thị Dăm (story 12).
/// </summary>
public class Route
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Mã tuyến hiển thị cho hành khách — "01", "B10"… Duy nhất toàn hệ thống.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Tên tuyến, ví dụ "Bến Thành — Chợ Lớn".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Điểm đầu của tuyến. Lưu tên địa danh, không phải khoá ngoại tới Stops.</summary>
    public string Origin { get; set; } = string.Empty;

    /// <summary>Điểm cuối của tuyến.</summary>
    public string Destination { get; set; } = string.Empty;

    /// <summary>
    /// Tổng chiều dài tuyến (km). Cộng dồn từ <see cref="RouteStop.DistanceKm"/> khi cần đối chiếu.
    /// </summary>
    public decimal DistanceKm { get; set; }

    /// <summary>Lưu dạng chuỗi trong CSDL — xem quy ước A3.</summary>
    public RouteStatus Status { get; set; } = RouteStatus.Active;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    /// <summary>Các trạm trên tuyến, kèm thứ tự chạy (bảng nối RouteStops).</summary>
    public ICollection<RouteStop> RouteStops { get; set; } = new List<RouteStop>();

    /// <summary>Bảng giá vé của tuyến — mỗi đối tượng ưu đãi một dòng.</summary>
    public ICollection<Fare> Fares { get; set; } = new List<Fare>();
}
