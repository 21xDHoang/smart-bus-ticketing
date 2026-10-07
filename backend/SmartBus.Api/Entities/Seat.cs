namespace SmartBus.Api.Entities;

/// <summary>
/// Bảng Seats — ghế ngồi của từng xe buýt (quy ước A9 mục 11: thuộc Buses).
/// Một cặp (xe, số ghế) chỉ xuất hiện một lần — ràng buộc unique ở AppDbContext.Seat.cs.
/// Task migrate bảng này là của Vàng Thị Dăm: dựng ở Sprint 2 (Buses, Seats, Trips), mở rộng ở
/// Sprint 3 (Seats, SeatHolds, SeatLayouts — US 2 "xem sơ đồ xe và chọn vị trí ghế còn trống").
/// </summary>
public class Seat
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BusId { get; set; }

    public Bus? Bus { get; set; }

    // ── Vị trí của ghế trên sơ đồ (Sprint 3). Trước Sprint 3 ghế chỉ có số, đủ để đếm sức chứa
    //    nhưng KHÔNG đủ để vẽ sơ đồ: màn hình chọn ghế cần biết ghế nào tầng nào, hàng nào, cột nào.

    /// <summary>
    /// Sơ đồ ghế mà ghế này được sinh ra từ đó. BẮT BUỘC — một ghế không thuộc sơ đồ nào thì không
    /// có toạ độ để vẽ lên màn hình chọn ghế, tức là dữ liệu không dùng được cho US 2. Vì vậy thứ
    /// tự đúng là: có <see cref="SeatLayout"/> trước, rồi mới sinh ghế cho xe theo sơ đồ đó.
    /// </summary>
    public Guid SeatLayoutId { get; set; }

    public SeatLayout? SeatLayout { get; set; }

    /// <summary>Tầng của ghế — 1 hoặc 2, không vượt quá <see cref="SeatLayout.NumberOfFloors"/>.</summary>
    public int Floor { get; set; } = 1;

    /// <summary>Hàng của ghế trong tầng — 1 là hàng đầu tiên. Cùng <see cref="ColumnIndex"/> tạo toạ độ vẽ sơ đồ.</summary>
    public int RowIndex { get; set; }

    /// <summary>Cột của ghế trong hàng — 1 là cột trái cùng. Đánh số theo sơ đồ, không theo số ghế.</summary>
    public int ColumnIndex { get; set; }

    /// <summary>Ghế phổ thông hay VIP (task "Cấu hình sơ đồ ghế theo loại xe (... ghế VIP)").</summary>
    public SeatType SeatType { get; set; } = SeatType.Standard;

    /// <summary>
    /// Số ghế hiển thị cho hành khách — "A1", "B12". Chuỗi ngắn có giới hạn (quy ước A3),
    /// không phải số nguyên: sơ đồ ghế xe khách thường có cả chữ lẫn số.
    /// </summary>
    public string SeatNumber { get; set; } = string.Empty;

    /// <summary>
    /// Chỉ có CreatedAt, không có UpdatedAt (quy ước A4): đổi sơ đồ ghế là xoá cả dàn rồi
    /// tạo lại theo xe, không sửa từng dòng — nên không có gì để đánh dấu "vừa sửa".
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
