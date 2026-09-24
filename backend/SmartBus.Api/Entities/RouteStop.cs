namespace SmartBus.Api.Entities;

/// <summary>
/// Bảng RouteStops — bảng nối Routes ↔ Stops, kiêm thứ tự trạm trên tuyến.
/// Một trạm không xuất hiện hai lần trên cùng một tuyến (ràng buộc unique ở AppDbContext.Route.cs).
/// Task migrate bảng này là của Vàng Thị Dăm (story 12).
/// </summary>
public class RouteStop
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RouteId { get; set; }

    public Route? Route { get; set; }

    public Guid StopId { get; set; }

    public Stop? Stop { get; set; }

    /// <summary>
    /// Thứ tự trạm trên tuyến, tính từ 1.
    /// Đây là thứ tự xe chạy thật: xe đi và xe về trên cùng tuyến trùng toạ độ, nên chiều
    /// phải xác định bằng StopOrder chứ không bằng khoảng cách toạ độ (quy ước A8.6).
    /// </summary>
    public int StopOrder { get; set; }

    /// <summary>Khoảng cách từ trạm liền trước tới trạm này (km). Trạm đầu tiên = 0.</summary>
    public decimal DistanceKm { get; set; }
}
