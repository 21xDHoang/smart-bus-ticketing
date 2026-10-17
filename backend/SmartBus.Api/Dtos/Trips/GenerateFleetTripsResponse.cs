namespace SmartBus.Api.Dtos.Trips;

/// <summary>
/// Kết quả <c>POST /api/routes/{routeId}/trips/generate-fleet</c> — các chuyến vừa sinh kèm bảng
/// tải theo từng xe. Hợp đồng ở docs/api-contract.md.
/// </summary>
public class GenerateFleetTripsResponse
{
    /// <summary>Các chuyến vừa sinh, xếp theo giờ khởi hành tăng dần.</summary>
    public IReadOnlyList<TripResponse> Items { get; set; } = [];

    /// <summary>Số chuyến vừa sinh — bằng <c>Items.Count</c>.</summary>
    public int Total { get; set; }

    /// <summary>
    /// Số chuyến mỗi xe được chia, ĐÚNG thứ tự <c>busIds</c> gửi lên. Có cả xe 0 chuyến: màn hình
    /// cần thấy xe nào không được dùng để người điều hành biết mà bỏ khỏi danh sách.
    /// </summary>
    public IReadOnlyList<FleetBusLoadResponse> BusLoads { get; set; } = [];
}

/// <summary>Một dòng trong bảng tải của lượt sinh nhiều xe.</summary>
public class FleetBusLoadResponse
{
    public Guid BusId { get; set; }

    public string BusLicensePlate { get; set; } = string.Empty;

    /// <summary>Số chuyến xe này nhận trong lượt sinh; 0 khi các xe khác đã phủ hết giờ.</summary>
    public int TripCount { get; set; }
}
