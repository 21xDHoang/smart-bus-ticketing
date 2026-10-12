namespace SmartBus.Api.Dtos.SeatLayouts;

/// <summary>
/// Body của PUT /api/seat-layouts/{id} — sửa hình học và tập ghế VIP của một sơ đồ.
///
/// Cố ý KHÔNG có <c>BusType</c>: loại xe là khoá nghiệp vụ (chỉ mục unique ở
/// AppDbContext.Seat.cs), đổi nó là xoá-rồi-tạo — cùng lối <c>UpdateFareRequest</c> không cho
/// đổi đối tượng hành khách, và <c>PUT /api/admin/users/{id}</c> không cho đổi thứ dùng làm khoá.
///
/// Sửa hình học/VIP của sơ đồ mà ghế đã bị giữ chỗ hoặc bán thì service trả 409 (khoá ngoại
/// Restrict ở SeatHolds/Tickets — quy ước A5); chưa ai đụng thì ghế cũ bị xoá và sinh lại theo
/// lưới mới. Xem mục "Lưu sơ đồ là ghế có ngay (tự sinh)" của docs/api-contract.md.
/// </summary>
public class UpdateSeatLayoutRequest
{
    /// <summary>Số tầng mới: 1 hoặc 2. Biên kiểm tra giống <see cref="CreateSeatLayoutRequest.NumberOfFloors"/>.</summary>
    public int NumberOfFloors { get; set; } = 1;

    /// <summary>Số hàng mỗi tầng — 1 đến 26. Biên kiểm tra giống <see cref="CreateSeatLayoutRequest.RowsPerFloor"/>.</summary>
    public int RowsPerFloor { get; set; }

    /// <summary>Số ghế mỗi hàng — 1 đến 10. Biên kiểm tra giống <see cref="CreateSeatLayoutRequest.ColumnsPerRow"/>.</summary>
    public int ColumnsPerRow { get; set; }

    /// <summary>
    /// Toạ độ ghế VIP mới, cùng định dạng với <see cref="CreateSeatLayoutRequest.VipSeatPositions"/>.
    /// Danh sách rỗng = bỏ hết ghế VIP. Tập hợp so theo nghĩa tập (thứ tự không quan trọng).
    /// </summary>
    public List<string> VipSeatPositions { get; set; } = new();
}
