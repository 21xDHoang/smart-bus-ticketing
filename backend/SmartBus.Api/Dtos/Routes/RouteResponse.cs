namespace SmartBus.Api.Dtos.Routes;

/// <summary>
/// Một tuyến đường — khớp mục "Tuyến đường — /routes" của docs/api-contract.md.
/// Đổi tên trường ở đây là đổi hình dạng API: phải sửa api-contract.md trước rồi báo
/// người viết frontend (Băng, Hạnh, Thịnh).
///
/// Thư mục đặt tên số nhiều "Routes" có chủ ý: namespace <c>SmartBus.Api.Dtos.Route</c> (số ít)
/// sẽ trùng tên với entity <c>SmartBus.Api.Entities.Route</c>, và trong file nào import cả hai
/// thì compiler báo "Route is a namespace but is used like a type" — cùng lý do thư mục "Fares".
/// </summary>
public class RouteResponse
{
    public Guid Id { get; set; }

    /// <summary>Mã tuyến hiển thị cho hành khách — "01", "B10"…</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Tên tuyến, ví dụ "Bến Thành — Chợ Lớn".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Điểm đầu của tuyến.</summary>
    public string Origin { get; set; } = string.Empty;

    /// <summary>Điểm cuối của tuyến.</summary>
    public string Destination { get; set; } = string.Empty;

    /// <summary>Tổng chiều dài tuyến (km). Cột numeric(6,2) — không bao giờ là float/double (quy ước A3).</summary>
    public decimal DistanceKm { get; set; }

    /// <summary>
    /// "Active" | "Inactive" — tên chuỗi của enum <see cref="Entities.RouteStatus"/>.
    /// Trả về CHUỖI chứ không phải enum: Program.cs không đăng ký JsonStringEnumConverter,
    /// nên kiểu enum sẽ serialize thành 0, 1, 2… — đọc không hiểu, trái tinh thần quy ước A3.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    /// <summary>null khi tuyến chưa được sửa lần nào.</summary>
    public DateTime? UpdatedAt { get; set; }
}
