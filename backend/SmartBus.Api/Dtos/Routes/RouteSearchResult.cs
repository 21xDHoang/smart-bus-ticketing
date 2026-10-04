namespace SmartBus.Api.Dtos.Routes;

/// <summary>
/// Một tuyến khớp điểm đi/điểm đến — khớp mục "Tra cứu tuyến — /routes/search" của
/// docs/api-contract.md. Đổi tên trường ở đây là đổi hình dạng API: phải sửa api-contract.md
/// trước rồi báo người viết frontend (Băng, Hạnh, Thịnh).
///
/// Cố ý đứng riêng thay vì tái dùng <see cref="RouteResponse"/>: response CRUD của quản lý mang
/// trạng thái và mốc thời gian, còn kết quả tra cứu của hành khách mang trạm và giá — trộn hai
/// thứ vào một lớp thì màn hình tra cứu phải nhận cả những trường nó không dùng.
/// </summary>
public class RouteSearchResult
{
    public Guid RouteId { get; set; }

    /// <summary>Mã tuyến hiển thị cho hành khách — "01", "B10"…</summary>
    public string RouteCode { get; set; } = string.Empty;

    /// <summary>Tên tuyến, ví dụ "Bến Thành — Chợ Lớn".</summary>
    public string RouteName { get; set; } = string.Empty;

    /// <summary>Điểm đầu của tuyến — hiển thị, không phải kết quả khớp (khớp theo tên trạm).</summary>
    public string Origin { get; set; } = string.Empty;

    /// <summary>Điểm cuối của tuyến.</summary>
    public string Destination { get; set; } = string.Empty;

    /// <summary>Tổng chiều dài tuyến (km). Cột numeric(6,2) — không bao giờ là float/double (quy ước A3).</summary>
    public decimal DistanceKm { get; set; }

    /// <summary>Trạm của tuyến theo đúng thứ tự chạy. Rỗng nếu tuyến chưa gán trạm nào.</summary>
    public List<RouteSearchStop> Stops { get; set; } = [];

    /// <summary>
    /// Giá vé thấp nhất trong bảng giá của tuyến (VND) — màn hình hiển thị "giá từ …".
    /// <c>null</c> khi tuyến chưa cấu hình giá: trạng thái dữ liệu bình thường, không phải lỗi.
    /// </summary>
    public decimal? MinPrice { get; set; }
}
