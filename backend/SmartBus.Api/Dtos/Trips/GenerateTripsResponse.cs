namespace SmartBus.Api.Dtos.Trips;

/// <summary>
/// Kết quả của POST /api/routes/{routeId}/trips/generate — danh sách chuyến vừa sinh ra
/// (chưa gồm các chuyến cũ của tuyến). <see cref="Total"/> = <see cref="Items"/> đếm được:
/// màn hình hiển thị câu "đã sinh N chuyến" mà không phải tự đếm mảng.
/// </summary>
public class GenerateTripsResponse
{
    public IReadOnlyList<TripResponse> Items { get; set; } = [];

    public int Total { get; set; }
}
