namespace SmartBus.Api.Entities;

/// <summary>
/// Bảng Stops — trạm dừng xe buýt.
/// Task migrate bảng này là của Vàng Thị Dăm (story 12).
/// </summary>
public class Stop
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Tên trạm hiển thị cho hành khách — "Trạm Cầu Giấy".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Địa chỉ đầy đủ, dùng để hiển thị và tìm kiếm.</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// Vĩ độ. Cột double precision — KHÔNG dùng real/float4 (quy ước A3):
    /// real chỉ có ~7 chữ số, sai lệch hàng mét khi tính khoảng cách tới trạm.
    /// </summary>
    public double Latitude { get; set; }

    /// <summary>Kinh độ — double precision, cùng lý do trên.</summary>
    public double Longitude { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    /// <summary>Các tuyến đi qua trạm này (bảng nối RouteStops).</summary>
    public ICollection<RouteStop> RouteStops { get; set; } = new List<RouteStop>();
}
