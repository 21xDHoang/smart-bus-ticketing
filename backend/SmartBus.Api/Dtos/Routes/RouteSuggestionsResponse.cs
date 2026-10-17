namespace SmartBus.Api.Dtos.Routes;

/// <summary>
/// Kết quả của GET /api/routes/suggestions — hợp đồng ở mục "Gợi ý tuyến cho ngày đã chọn —
/// /routes/suggestions" của docs/api-contract.md.
///
/// Khác <see cref="RouteSearchResult"/> ở chỗ tách <b>sự tồn tại của tuyến</b> khỏi <b>lịch
/// chạy</b>: tuyến vẫn nằm trong <see cref="Matches"/> dù ngày đã chọn không có chuyến, kèm
/// ngày gần nhất và mã lý do trống để màn hình không rơi vào ô "Không tìm thấy tuyến".
/// </summary>
public class RouteSuggestionsResponse
{
    /// <summary>Ngày đã chọn sau chuẩn hoá — <c>yyyy-MM-dd</c>.</summary>
    public string SelectedDate { get; set; } = string.Empty;

    /// <summary>Ngày xa nhất trong cửa sổ lịch chạy — <c>yyyy-MM-dd</c>.</summary>
    public string BookingHorizonEnd { get; set; } = string.Empty;

    public StopSuggestionInfo Origin { get; set; } = new();

    public StopSuggestionInfo Destination { get; set; } = new();

    /// <summary>Tuyến Active nối đúng hai trạm, <b>không phụ thuộc ngày</b>.</summary>
    public List<RouteSuggestion> Matches { get; set; } = [];

    /// <summary>Gợi ý thay thế — chỉ có dữ liệu khi <see cref="Matches"/> rỗng.</summary>
    public RouteSuggestionsAlternatives Alternatives { get; set; } = new();

    /// <summary>Mã lý do khi không có chuyến phù hợp; <c>null</c> khi có tuyến và có chuyến.</summary>
    public string? EmptyReason { get; set; }
}

/// <summary>Trạm đã giải được từ id hoặc tên — đủ để màn hình hiển thị lại lựa chọn.</summary>
public class StopSuggestionInfo
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    /// <summary>Trạm có thuộc ít nhất một tuyến <c>Active</c> không.</summary>
    public bool OnActiveRoute { get; set; }
}

/// <summary>
/// Một tuyến gợi ý — đúng các trường của <see cref="RouteSearchResult"/> cộng thông tin lịch
/// chạy. Kế thừa thay vì chép lại: màn hình dùng chung lưới kết quả với /routes/search, chỉ đọc
/// thêm vài trường.
/// </summary>
public class RouteSuggestion : RouteSearchResult
{
    /// <summary>Có ít nhất một chuyến <c>Scheduled</c> trong trọn ngày <c>selectedDate</c>.</summary>
    public bool HasTripsOnDate { get; set; }

    /// <summary>Có chuyến mỗi ngày từ hôm nay đến <c>bookingHorizonEnd</c>.</summary>
    public bool RunsDaily { get; set; }

    /// <summary>Các ngày trong cửa sổ có chuyến — <c>yyyy-MM-dd</c>, tăng dần.</summary>
    public List<string> AvailableDates { get; set; } = [];

    /// <summary>Ngày gần <c>selectedDate</c> nhất có chuyến; hoà khoảng cách chọn ngày tương lai.</summary>
    public string? NearestAvailableDate { get; set; }

    /// <summary>Giờ khởi hành sớm nhất trong <see cref="NearestAvailableDate"/> (offset +07:00).</summary>
    public DateTimeOffset? NextDeparture { get; set; }

    /// <summary>Số chuyến <c>Scheduled</c> trong <see cref="NearestAvailableDate"/>.</summary>
    public int DepartureCountOnNearestDate { get; set; }
}

/// <summary>Các tuyến thay thế khi không có tuyến nào nối đúng hai trạm.</summary>
public class RouteSuggestionsAlternatives
{
    /// <summary>Tuyến Active đi qua điểm đi nhưng không tới điểm đến.</summary>
    public List<RouteSuggestion> FromOrigin { get; set; } = [];

    /// <summary>Tuyến Active đi qua điểm đến nhưng không từ điểm đi.</summary>
    public List<RouteSuggestion> ToDestination { get; set; } = [];
}

/// <summary>
/// Mã lý do trống — hợp đồng chốt ở bảng "Mã emptyReason" của docs/api-contract.md. Hằng số
/// thường trong lớp <c>…Codes</c> theo quy ước đặt tên của dự án.
/// </summary>
public static class RouteSuggestionEmptyReasons
{
    public const string NoActiveRoute = "noActiveRoute";

    public const string OriginNotServed = "originNotServed";

    public const string DestinationNotServed = "destinationNotServed";

    public const string NoRouteBetweenStops = "noRouteBetweenStops";

    public const string DateBeyondHorizon = "dateBeyondHorizon";

    public const string NoTripsThatDay = "noTripsThatDay";
}
