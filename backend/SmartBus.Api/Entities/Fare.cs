namespace SmartBus.Api.Entities;

/// <summary>
/// Bảng Fares — giá vé theo tuyến và theo đối tượng ưu đãi.
/// Mỗi cặp (tuyến, đối tượng) chỉ có đúng một giá (ràng buộc unique ở AppDbContext.Route.cs).
/// Task migrate bảng này là của Vàng Thị Dăm (story 12).
/// </summary>
public class Fare
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RouteId { get; set; }

    public Route? Route { get; set; }

    /// <summary>Lưu dạng chuỗi trong CSDL — xem quy ước A3.</summary>
    public PassengerType PassengerType { get; set; }

    /// <summary>
    /// Giá vé (VND). Cột numeric(12,2) — KHÔNG dùng float/double (quy ước A3):
    /// sai số dấu phẩy động trên tiền là lỗi không sửa được.
    /// </summary>
    public decimal Price { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}
