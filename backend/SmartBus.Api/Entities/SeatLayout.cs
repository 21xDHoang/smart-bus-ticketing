namespace SmartBus.Api.Entities;

/// <summary>
/// Bảng SeatLayouts — sơ đồ ghế của một LOẠI XE. Task Sprint 3 "Migrate bảng Seats, SeatHolds,
/// SeatLayouts" (Vàng Thị Dăm) + dòng 9 "Cấu hình sơ đồ ghế theo loại xe (số tầng, số ghế, ghế VIP)".
///
/// Đây là MẪU, không phải ghế thật: nó trả lời "loại xe này có mấy tầng, tổng bao nhiêu ghế".
/// Ghế thật của từng xe nằm ở bảng <c>Seats</c> và trỏ về đây bằng <see cref="Seat.SeatLayoutId"/> —
/// nhờ vậy sửa sơ đồ là chuyện của mẫu, còn ghế đã sinh vẫn giữ nguyên vị trí đã in trên vé.
///
/// Khoá nghiệp vụ là <see cref="BusType"/>: mỗi loại xe đúng MỘT sơ đồ (chỉ mục unique ở
/// AppDbContext.Seat.cs). Dùng thẳng chuỗi loại xe thay vì tạo bảng BusTypes riêng vì
/// <c>Buses.BusType</c> từ Sprint 2 đã là chuỗi tự do — thêm bảng tra cứu lúc này là thêm một
/// tầng ánh xạ mà không story nào hỏi tới.
///
/// ⚠️ A9 của docs/03-quy-uoc.md mới liệt kê 20 bảng, CHƯA có SeatLayouts (và SeatHolds). Hai bảng
/// này là chủ trương có sẵn của bảng phân công Sprint 3 — cần nhóm chốt bổ sung vào A9 cho tài
/// liệu khớp CSDL (việc báo nhóm, không phải việc code).
/// </summary>
public class SeatLayout
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Loại xe áp dụng — khớp giá trị <c>Buses.BusType</c> ("Xe buýt 45 chỗ", "Xe buýt điện").
    /// Là khoá nghiệp vụ: hai sơ đồ cùng một loại xe là dữ liệu mâu thuẫn, chặn ở tầng CSDL.
    /// </summary>
    public string BusType { get; set; } = string.Empty;

    /// <summary>Số tầng của xe — 1 với xe một tầng, 2 với xe hai tầng / giường nằm.</summary>
    public int NumberOfFloors { get; set; } = 1;

    /// <summary>
    /// Tổng số ghế của sơ đồ. Phải khớp số dòng <c>Seat</c> sinh ra từ sơ đồ này — cùng lối ràng
    /// buộc "Buses.Capacity phải khớp số dòng Seat" đã ghi ở <see cref="Bus.Capacity"/>: kiểm tra ở
    /// tầng service, CSDL không có check constraint (cùng lối các cột nghiệp vụ khác của dự án).
    /// </summary>
    public int TotalSeats { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Sơ đồ có sửa dữ liệu (đổi số tầng, đổi tổng số ghế) nên có UpdatedAt (A4).</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Ghế của các xe được sinh theo sơ đồ này.</summary>
    public ICollection<Seat> Seats { get; set; } = new List<Seat>();
}
