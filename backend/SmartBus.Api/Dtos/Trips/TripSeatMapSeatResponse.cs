namespace SmartBus.Api.Dtos.Trips;

/// <summary>
/// Một ghế trong sơ đồ của <see cref="TripSeatMapResponse"/> — tên trường bám đúng cột của entity
/// <c>Seat</c> (Sprint 3, Vàng Thị Dăm): <c>id</c>, <c>seatNumber</c>, <c>floor</c>, <c>rowIndex</c>,
/// <c>columnIndex</c>, <c>seatType</c>. Hai trường suy ra theo chuyến là <see cref="Status"/> và
/// <see cref="Price"/>.
/// </summary>
public class TripSeatMapSeatResponse
{
    /// <summary>Khoá ghế (<c>Seats.Id</c>) — màn hình gửi lại cho API giữ ghế (US 3).</summary>
    public Guid Id { get; set; }

    /// <summary>Số ghế hiển thị — "A1"; xe hai tầng thêm tiền tố tầng "T2-A1" (docs/26 §2).</summary>
    public string SeatNumber { get; set; } = string.Empty;

    /// <summary>Tầng của ghế, bắt đầu từ 1.</summary>
    public int Floor { get; set; }

    /// <summary>Hàng trong tầng, 1 là hàng đầu.</summary>
    public int RowIndex { get; set; }

    /// <summary>Cột trong hàng, 1 là cột trái cùng.</summary>
    public int ColumnIndex { get; set; }

    /// <summary>Loại ghế — "Standard" hoặc "Vip" (<see cref="Entities.SeatType"/> ghi chuỗi, quy ước A3).</summary>
    public string SeatType { get; set; } = string.Empty;

    /// <summary>
    /// Trạng thái ghế CHO CHUYẾN NÀY — "Available" / "Held" / "Paid". Suy ra ở tầng service từ
    /// SeatHolds và Tickets, không phải cột của bảng Seats: cùng một ghế, chuyến này đã bán còn
    /// chuyến khác vẫn trống. Quy tắc đầy đủ ở mục "GET /trips/{id}/seats" của docs/api-contract.md.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Giá ghế này (VND) = <see cref="TripSeatMapResponse.PricePerSeat"/> + phụ trội VIP khi có
    /// nguồn dữ liệu cho nó. <c>pricePerSeat</c> là null → null.
    /// </summary>
    public decimal? Price { get; set; }
}
