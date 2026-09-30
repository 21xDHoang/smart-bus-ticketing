namespace SmartBus.Api.Dtos.Buses;

/// <summary>
/// Một xe buýt — khớp mục "Xe buýt — /buses" của docs/api-contract.md.
/// Đổi tên trường ở đây là đổi hình dạng API: phải sửa api-contract.md trước rồi báo
/// người viết frontend (Băng, Hạnh, Thịnh).
/// </summary>
public class BusResponse
{
    public Guid Id { get; set; }

    /// <summary>Biển số xe — "29B-123.45". Duy nhất toàn hệ thống.</summary>
    public string LicensePlate { get; set; } = string.Empty;

    /// <summary>Loại xe — "Xe buýt 45 chỗ", "Xe buýt điện".</summary>
    public string BusType { get; set; } = string.Empty;

    /// <summary>Sức chứa theo số ghế.</summary>
    public int Capacity { get; set; }

    /// <summary>
    /// "Active" | "Maintenance" | "Inactive" — tên chuỗi của enum <see cref="Entities.BusStatus"/>.
    /// Trả về CHUỖI chứ không phải enum — cùng lý do <see cref="Routes.RouteResponse.Status"/>
    /// (Program.cs không đăng ký JsonStringEnumConverter).
    /// </summary>
    public string Status { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    /// <summary>null khi xe chưa được sửa lần nào.</summary>
    public DateTime? UpdatedAt { get; set; }
}
