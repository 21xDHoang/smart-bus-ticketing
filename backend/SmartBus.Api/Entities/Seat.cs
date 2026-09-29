namespace SmartBus.Api.Entities;

/// <summary>
/// Bảng Seats — ghế ngồi của từng xe buýt (quy ước A9 mục 11: thuộc Buses).
/// Một cặp (xe, số ghế) chỉ xuất hiện một lần — ràng buộc unique ở AppDbContext.Trip.cs.
/// Task migrate bảng này là của Vàng Thị Dăm (story 13).
/// </summary>
public class Seat
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BusId { get; set; }

    public Bus? Bus { get; set; }

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
