namespace SmartBus.Api.Dtos.Stops;

/// <summary>
/// Một trạm gợi ý cho ô điểm đi/điểm đến của màn tra cứu tuyến — hợp đồng ở mục
/// "Gợi ý trạm cho tra cứu tuyến — /stops/suggestions" của docs/api-contract.md.
///
/// Khác <see cref="StopResponse"/> ở <see cref="Routes"/>: hợp đồng mới cần mã tuyến để màn
/// hình hiển thị badge "01TL-PY" và để hành khách phân biệt hai trạm trùng tên nằm trên hai
/// tuyến khác nhau.
/// </summary>
public class StopSuggestionResponse
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    /// <summary>
    /// Các tuyến <c>Active</c> đi qua trạm này trong đúng ngữ cảnh lọc. Có ít nhất một phần tử:
    /// trạm không thuộc tuyến active nào không bao giờ được trả về.
    /// </summary>
    public List<StopSuggestionRoute> Routes { get; set; } = [];
}

/// <summary>Một tuyến đi qua trạm gợi ý — đủ để hiển thị badge và giải thích vì sao trạm xuất hiện.</summary>
public class StopSuggestionRoute
{
    public Guid RouteId { get; set; }

    /// <summary>Mã tuyến hiển thị cho hành khách — "01", "B10"…</summary>
    public string RouteCode { get; set; } = string.Empty;

    public string RouteName { get; set; } = string.Empty;
}
