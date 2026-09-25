namespace SmartBus.Api.Dtos.Stops;

/// <summary>
/// Một trạm dừng — khớp mục "Trạm dừng — /stops" của docs/api-contract.md.
/// Cố ý chỉ có 5 trường: hợp đồng và kiểu <c>Stop</c> bên frontend
/// (frontend/src/api/stopApi.ts) đã chốt đúng hình dạng này, không kèm createdAt/updatedAt.
/// Đổi tên trường ở đây là đổi hình dạng API: phải sửa api-contract.md trước rồi báo
/// người viết frontend (Băng).
/// </summary>
public class StopResponse
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public double Latitude { get; set; }

    public double Longitude { get; set; }
}
