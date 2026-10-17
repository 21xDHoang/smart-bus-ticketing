namespace SmartBus.Api.Dtos.Buses;

/// <summary>
/// Tình trạng ghế của xe — <see cref="BusResponse.SeatSummary"/>. Khớp mục "Xe buýt — /buses"
/// của docs/api-contract.md.
///
/// Tách khỏi <c>Bus.Capacity</c> vì hai con số có thể lệch: sức chứa là khai báo của quản lý, còn
/// <see cref="SeatsCreated"/> là số dòng <c>Seats</c> thật đã sinh từ sơ đồ ghế. Lệch nghĩa là xe
/// chưa cấu hình sơ đồ hoặc sơ đồ vừa đổi — màn hình đội xe cần thấy điều đó.
/// </summary>
public class BusSeatSummaryResponse
{
    /// <summary>Số ghế thật đã sinh cho xe (<c>COUNT(Seats)</c> của xe này).</summary>
    public int SeatsCreated { get; set; }

    /// <summary>Sức chứa khai báo trên xe (<c>Bus.Capacity</c>).</summary>
    public int Capacity { get; set; }

    /// <summary><c>true</c> khi đã sinh đủ ghế; <c>false</c> là dữ liệu cần cấu hình lại sơ đồ.</summary>
    public bool IsComplete { get; set; }

    /// <summary>
    /// Sơ đồ ghế của LOẠI xe — mỗi <c>BusType</c> đúng một sơ đồ. Null khi loại xe chưa được
    /// cấu hình sơ đồ.
    /// </summary>
    public BusSeatLayoutSummaryResponse? Layout { get; set; }
}

/// <summary>
/// Hình dạng sơ đồ ghế của loại xe — đọc từ <see cref="Entities.SeatLayout"/>.
/// Không trả danh sách ghế: ghế thuộc về XE, còn đây là mẫu dùng chung cho mọi xe cùng loại.
/// </summary>
public class BusSeatLayoutSummaryResponse
{
    public int NumberOfFloors { get; set; }

    public int RowsPerFloor { get; set; }

    public int ColumnsPerRow { get; set; }

    /// <summary>Tổng số ghế của sơ đồ = số tầng × số hàng × số cột.</summary>
    public int TotalSeats { get; set; }

    /// <summary>Số vị trí ghế VIP trong sơ đồ.</summary>
    public int VipSeatCount { get; set; }
}
